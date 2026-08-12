using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AgentColorLabelTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-agent-label-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task ColorLabelMutation_UsesNormalizedIdsAndRefreshesOnce()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog);
        var first = await CreateImageAsync(catalog, "first.jpg");
        vm.Library.SetImages([first]);
        var refreshes = 0;
        vm.Library.FilterChanged += (_, _) => refreshes++;

        var ids = AgentToolService.NormalizeColorLabelIds(
            [first.FilePath, first.FilePath, "missing"]);
        await vm.SetColorLabelForImagesAsync([first], ColorLabel.Purple);

        Assert.Equal([first.FilePath, "missing"], ids);
        Assert.Equal(ColorLabel.Purple, first.ColorLabel);
        Assert.Equal(
            ColorLabel.Purple,
            (await catalog.LoadImageStatesAsync([first.FilePath]))[first.FilePath]
                .ColorLabel);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public void ReadModels_SerializeColorLabelOnSummaryAndFilterState()
    {
        var summaries = new[]
        {
            new AgentImageSummary(
                "red.jpg", "red.jpg", 0, "unflagged", false, false,
                0, 0, null, null, null, null, null, null, null)
            {
                ColorLabel = "red"
            }
        };
        var state = new AgentLibraryState(
            null, 1, 1, new AgentFilterState("all", "all", 0, "red"), null);

        Assert.Contains("\"colorLabel\":\"red\"",
            JsonSerializer.Serialize(summaries, AgentToolJson.Options));
        Assert.Contains("\"colorLabel\":\"red\"",
            JsonSerializer.Serialize(state, AgentToolJson.Options));
    }

    [Fact]
    public async Task McpHost_RegistersSetColorLabelTool()
    {
        using var catalog = new CatalogService(_root);
        var vm = CreateViewModel(catalog);
        await using var imageService = new ImageService(catalog);
        var service = new AgentToolService(vm, imageService, catalog);
        var createTools = typeof(McpServerHost).GetMethod(
            "CreateTools",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic);
        var tools = Assert.IsAssignableFrom<ModelContextProtocol.Server.McpServerTool[]>(
            createTools!.Invoke(null, [service]));

        Assert.Contains(tools,
            tool => tool.ProtocolTool.Name == "set_color_label");
    }

    private MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        new(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);

    private async Task<ImageFile> CreateImageAsync(
        CatalogService catalog,
        string name)
    {
        var image = new ImageFile(Path.Combine(_root, name));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        return image;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }
}
