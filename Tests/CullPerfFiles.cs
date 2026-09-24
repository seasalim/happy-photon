using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageMagick;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal static class CullPerfFiles
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    internal static string Root => GoldenTestPaths.RepositoryRoot;
    internal static string GatePath => Environment.GetEnvironmentVariable("CULL_PERF_RUN") is { } run &&
        File.Exists(Path.Combine(run, "gates.json")) ? Path.Combine(run, "gates.json") :
        Path.Combine(Root, "Tests", "CullPerfGates.json");
    internal static string Run => Environment.GetEnvironmentVariable("CULL_PERF_RUN")
        ?? throw new InvalidOperationException("Use scripts/cull-perf.ps1.");
    internal static string Hash(string path) => Convert.ToHexStringLower(
        SHA256.HashData(File.ReadAllBytes(path)));
    internal static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)!;
    internal static CullPerfFragment[] ReadBaselineFragments(string path)
    {
        try { return Read<BaselineResult>(path)?.Fragments ?? []; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private sealed record BaselineResult(CullPerfFragment[]? Fragments);

    internal static void WriteNew(string path, object value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(stream, value, value.GetType(), Json);
    }

    internal static string GeneratedJpeg(string? directory = null)
    {
        var generated = directory ?? Path.Combine(Root, "artifacts", "cull-perf-fixtures");
        Directory.CreateDirectory(generated);
        var jpeg = Path.Combine(generated, "generated-24mp.jpg");
        if (!File.Exists(jpeg))
        {
            using var image = new MagickImage("gradient:#182a48-#edce91",
                new MagickReadSettings { Width = 6000, Height = 4000 });
            image.Strip();
            image.Quality = 90;
            image.Write(jpeg, MagickFormat.Jpeg);
        }
        return jpeg;
    }

    internal static (string Root, Dictionary<string, string> Hashes) Fixtures()
    {
        var generated = Path.Combine(Root, "artifacts", "cull-perf-fixtures");
        var jpeg = GeneratedJpeg();
        var sources = new Dictionary<string, string>
        {
            ["jpeg"] = jpeg,
            ["canon"] = GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2"),
            ["fuji"] = GoldenTestPaths.Asset("fujifilm-x30.raf"),
            ["legacy-jpeg"] = GoldenTestPaths.Asset("display-p3-reference.jpg"),
            ["legacy-raw"] = GoldenTestPaths.Asset("canon-eos-350d.cr2")
        };
        var hashes = sources.ToDictionary(pair => pair.Key, pair => Hash(pair.Value));
        var key = Convert.ToHexStringLower(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join(";", hashes.Values))));
        var fixtureRoot = Path.Combine(generated, key + "-v2");
        Directory.CreateDirectory(fixtureRoot);
        var anchors = Path.Combine(fixtureRoot, ".sources");
        Directory.CreateDirectory(anchors);
        foreach (var name in new[] { "jpeg", "canon", "fuji" })
        {
            var anchor = Path.Combine(anchors, Path.GetFileName(sources[name]));
            if (!File.Exists(anchor)) File.Copy(sources[name], anchor);
            if (Hash(anchor) != hashes[name]) throw new IOException("Fixture anchor hash mismatch.");
            sources[name] = anchor;
        }
        for (var i = 0; i < 627; i++)
        {
            Link(sources["jpeg"], Path.Combine(fixtureRoot, $"image-{i:D4}.jpg"));
            var raw = sources[i % 2 == 0 ? "canon" : "fuji"];
            Link(raw, Path.Combine(fixtureRoot, $"image-{i:D4}{Path.GetExtension(raw)}"));
        }
        return (fixtureRoot, hashes);
    }

    internal static async Task PrepareCatalogAsync(string root)
    {
        var template = Path.Combine(root, ".catalog");
        var complete = Path.Combine(root, ".catalog-complete");
        if (File.Exists(complete)) return;
        using (var catalog = new CatalogService(template))
        {
            await catalog.InitializeAsync();
            await catalog.LoadOrCreateImageStatesAsync(Directory.GetFiles(root)
                .Where(path => Path.GetFileName(path).StartsWith("image-", StringComparison.Ordinal)).ToArray());
        }
        File.WriteAllText(complete, "627 pairs; schema from the candidate's CatalogService");
    }

    internal static void CopyCatalog(string root, string destination)
    {
        if (!File.Exists(Path.Combine(root, ".catalog-complete")))
            throw new IOException("Prepare the reusable fixture catalog first.");
        Directory.CreateDirectory(destination);
        foreach (var source in Directory.GetFiles(Path.Combine(root, ".catalog")))
            File.Copy(source, Path.Combine(destination, Path.GetFileName(source)));
    }

    private static void Link(string source, string target)
    {
        if (File.Exists(target)) return;
        if (!OperatingSystem.IsWindows() || !CreateHardLink(target, source, IntPtr.Zero))
            throw new IOException("The required 627-pair fixture needs Windows hard links on one volume.");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string name, string existing, IntPtr security);
}
