using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed class McpServerHost
{
    public const int Port = 7326;
    private const string Privacy =
        " Returns metadata/statistics only; never returns image content.";

    private WebApplication? _app;

    public string GetUrl(string token) => $"http://127.0.0.1:{Port}/{token}";

    public async Task StartAsync(AgentToolService tools, string token)
    {
        if (_app != null) return;
        if (!AgentAccessToken.IsValid(token))
            throw new AgentToolException("The saved agent token is invalid.");

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{Port}");
        builder.Logging.ClearProviders();

        builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "happy-photon",
                Version = AppBuildInfo.Version.ToString(3),
                Title = "Happy Photon Agent Access"
            };
        })
        .WithHttpTransport(options => options.Stateless = true)
        .WithTools(CreateTools(tools));

        var app = builder.Build();
        app.MapMcp($"/{token}");
        try
        {
            await app.StartAsync();
            _app = app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        var app = _app;
        _app = null;
        if (app == null) return;

        await app.StopAsync();
        await app.DisposeAsync();
    }

    private static McpServerTool[] CreateTools(AgentToolService tools) =>
    [
        McpServerTool.Create(
            async (CancellationToken ct) =>
                await InvokeJsonAsync(tools.GetLibraryStateAsync, ct),
            Options("get_library_state",
                "Gets the current folder, counts, filters, and selected image.")),

        McpServerTool.Create(
            async (CancellationToken ct, string? flag = null, int? minRating = null,
                string? fileType = null, int offset = 0, int limit = 100,
                bool loadMetadata = true) =>
                await InvokeJsonAsync(() => tools.ListImagesAsync(new ListImagesRequest(
                    flag, minRating, fileType, offset, limit, loadMetadata)), ct),
            Options("list_images",
                "Lists images in the current folder with filters and pagination. " +
                "Burst fields are populated after background analysis; check " +
                "get_library_state.burstsComputed. loadMetadata is nearly free after the sweep.")),

        McpServerTool.Create(
            async (string[] ids, CancellationToken ct) =>
                await InvokeJsonAsync(() => tools.GetImageStatsAsync(ids), ct),
            Options("get_image_stats",
                "Computes local sharpness, clipping, and luminance statistics.")),

        McpServerTool.Create(
            async (string[] ids, int rating, CancellationToken ct) =>
                await InvokeJsonAsync(() => tools.SetRatingAsync(ids, rating), ct),
            Options("set_rating", "Sets a 0-5 star rating for images.")),

        McpServerTool.Create(
            async (string[] ids, string flag, CancellationToken ct) =>
                await InvokeJsonAsync(() => tools.SetFlagAsync(ids, flag), ct),
            Options("set_flag", "Sets picked, rejected, or unflagged state for images.")),

        McpServerTool.Create(
            async (string[] ids, string colorLabel, CancellationToken ct) =>
                await InvokeJsonAsync(
                    () => tools.SetColorLabelAsync(ids, colorLabel), ct),
            Options("set_color_label",
                "Sets none, red, yellow, green, blue, or purple color labels for images.")),

        McpServerTool.Create(
            async (CancellationToken ct) =>
                await InvokeJsonAsync(tools.ListPresetsAsync, ct),
            Options("list_presets", "Lists available built-in and user presets.")),

        McpServerTool.Create(
            async (string[] ids, string presetId, CancellationToken ct) =>
                await InvokeJsonAsync(() => tools.ApplyPresetAsync(ids, presetId), ct),
            Options("apply_preset",
                "Applies preset color and tonal settings without changing geometry.")),

        McpServerTool.Create(
            async (string[] ids, AgentEditSettingsInput settings, CancellationToken ct) =>
                await InvokeJsonAsync(() => tools.ApplyEditSettingsAsync(ids, settings), ct),
            Options("apply_edit_settings",
                "Replaces color and tonal settings without changing geometry; " +
                "channel curves and effects reset because this tool does not expose them.")),

        McpServerTool.Create(
            async (string[] ids, AgentExportOptions options, CancellationToken ct) =>
                await InvokeJsonAsync(() => tools.ExportImagesAsync(ids, options), ct),
            Options("export_images",
                "Exports jpeg, png, or webp copies below the open folder without " +
                "overwriting files; targets that would overwrite original image " +
                "files are refused. Optional variants have a name and optional " +
                "maxDimension and export into one sub-folder per variant. Result ids " +
                "are paths relative to the output folder; existing files are skipped. " +
                "outputColorSpace accepts srgb (default) or displayP3."))
    ];

    private static McpServerToolCreateOptions Options(string name, string description) => new()
    {
        Name = name,
        Description = description + Privacy,
        SerializerOptions = AgentToolJson.Options
    };

    private static async Task<string> InvokeJsonAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await action();
            return JsonSerializer.Serialize(result, AgentToolJson.Options);
        }
        catch (AgentToolException ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }
}
