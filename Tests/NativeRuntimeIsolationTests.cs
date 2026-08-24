using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[CollectionDefinition(nameof(NativeRuntimeIsolationCollection), DisableParallelization = true)]
public sealed class NativeRuntimeIsolationCollection;

[Collection(nameof(NativeRuntimeIsolationCollection))]
public sealed class NativeRuntimeIsolationTests
{
    [Fact]
    public async Task PackageResolvedRuntime_LoadsOnlyAuditedRidAssets()
    {
        await RunChildAsync("package", null);
    }

    [Theory]
    [InlineData("missing-bridge")]
    [InlineData("missing-companion")]
    [InlineData("unloadable-bridge")]
    [InlineData("unloadable-companion")]
    public async Task MissingRuntime_DecoysCannotSatisfyHandshakeOrDecodeRaw(
        string mode)
    {
        using var staging = new TemporaryDirectory();
        using var decoys = new TemporaryDirectory();
        var runtime = RuntimeDirectory();
        var bridge = BridgeName();
        var companion = CompanionName();
        var rejectsBridge = mode.EndsWith("bridge", StringComparison.Ordinal);
        var unloadable = mode.StartsWith("unloadable", StringComparison.Ordinal);
        if (unloadable)
        {
            foreach (var file in Directory.GetFiles(runtime))
            {
                File.Copy(file, Path.Combine(staging.Path, Path.GetFileName(file)));
            }
            File.WriteAllText(
                Path.Combine(staging.Path, rejectsBridge ? bridge : companion),
                "not a native library");
        }
        else
        {
            if (!rejectsBridge)
                File.Copy(Path.Combine(runtime, bridge), Path.Combine(staging.Path, bridge));
            if (rejectsBridge)
                File.Copy(Path.Combine(runtime, companion), Path.Combine(staging.Path, companion));
        }
        File.WriteAllText(
            Path.Combine(decoys.Path, rejectsBridge ? bridge : companion),
            "decoy");
        await RunChildAsync(mode, staging.Path, decoys.Path);
    }

    [Fact]
    public async Task RuntimeIsolation_Child()
    {
        var mode = Environment.GetEnvironmentVariable("HAPPY_PHOTON_NATIVE_CHILD");
        Assert.SkipWhen(string.IsNullOrEmpty(mode), "Runs only in an isolated child process.");
        if (mode == "package")
        {
            var runtime = LibRawContext.Runtime;
            Assert.Equal(0x001602u, runtime.LibRawVersionNumber);
            Assert.NotEqual(0u, runtime.Capabilities & LibRawCapabilities.Jpeg);
            Assert.NotEqual(0u, runtime.Capabilities & LibRawCapabilities.Zlib);
            Assert.Equal(
                NativeLibraryResolver.GetDefaultOpenMpThreadCount(
                    Environment.ProcessorCount).ToString(),
                Environment.GetEnvironmentVariable("OMP_NUM_THREADS"));
            AssertLoadedFrom(BridgeName(), RuntimeDirectory());
            AssertLoadedFrom(CompanionName(), RuntimeDirectory());
            Assert.Equal(ExpectedHashes().Bridge, Hash(Path.Combine(
                RuntimeDirectory(), BridgeName())));
            Assert.Equal(ExpectedHashes().Companion, Hash(Path.Combine(
                RuntimeDirectory(), CompanionName())));
            return;
        }

        var health = LibRawNativeSupport.Health;
        Assert.False(health.IsHealthy);
        Assert.Equal(
            mode.EndsWith("bridge", StringComparison.Ordinal)
                ? LibRawRuntimeComponent.Bridge
                : LibRawRuntimeComponent.LibRawCompanion,
            health.RejectedComponent);
        Assert.Equal(
            mode.StartsWith("unloadable", StringComparison.Ordinal)
                ? LibRawDeploymentStage.Load
                : LibRawDeploymentStage.Resolution,
            health.Observations.FailureStage);
        Assert.Contains("component=", health.DiagnosticText);
        Assert.False(LibRawNativeSupport.IsAvailable);
        Assert.False(new LibRawProcessingService().IsAvailable);
        var raw = new RawBaseLoader();
        Assert.False(raw.CanLoad(new ImageFile("unavailable.cr2")));

        var standard = new StandardBaseLoader((_, _) =>
            new MagickImage(MagickColors.Green, 4, 3));
        var router = new BaseLoaderRouter(raw, standard);
        using var result = router.LoadPreviewBase(new ImageFile("fallback.cr2"),
            BaseDecodeSettings.Default, CancellationToken.None);
        Assert.Null(result);

        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(directory.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var imageService = new ImageService(catalog, router);
        var rawService = typeof(ImageService).GetField("_rawService",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(imageService);
        var libRawService = Assert.IsType<LibRawProcessingService>(rawService);
        Assert.False(libRawService.IsAvailable);
    }

    private static async Task RunChildAsync(
        string mode, string? staging, string? decoys = null)
    {
        var configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = GoldenTestPaths.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
        {
            "test", "Tests/HappyPhoton.Tests.csproj", "--configuration", configuration,
            "--no-build", "--no-restore", "-p:UsedAvaloniaProducts=", "--filter",
            "FullyQualifiedName=HappyPhoton.Tests.NativeRuntimeIsolationTests.RuntimeIsolation_Child"
        }) start.ArgumentList.Add(argument);
        start.Environment["HAPPY_PHOTON_NATIVE_CHILD"] = mode;
        start.Environment.Remove("HAPPY_PHOTON_LIBRAW_BRIDGE_DIR");
        start.Environment.Remove("OMP_NUM_THREADS");
        if (staging != null) start.Environment["HAPPY_PHOTON_LIBRAW_BRIDGE_DIR"] = staging;
        if (decoys != null)
        {
            start.Environment.TryGetValue("PATH", out var currentPath);
            start.Environment["PATH"] = decoys + Path.PathSeparator +
                currentPath;
            start.Environment["LD_LIBRARY_PATH"] = decoys;
        }
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        Assert.True(process.ExitCode == 0,
            $"Native child failed ({mode}).{Environment.NewLine}{output}{Environment.NewLine}{error}");
    }

    private static void AssertLoadedFrom(string name, string directory)
    {
        var loaded = name == BridgeName()
            ? NativeLibraryResolver.LoadedBridgePath
            : NativeLibraryResolver.LoadedLibRawPath;
        Assert.NotNull(loaded);
        Assert.Equal(Path.GetFullPath(Path.Combine(directory, name)), loaded,
            ignoreCase: OperatingSystem.IsWindows());
    }

    private static string RuntimeDirectory() => Path.Combine(AppContext.BaseDirectory,
        "runtimes", OperatingSystem.IsWindows() ? "win-x64" :
            OperatingSystem.IsMacOS() ? "osx-arm64" : "linux-x64", "native");
    private static string BridgeName() => OperatingSystem.IsWindows()
        ? "happyphoton_libraw_bridge.dll" : OperatingSystem.IsMacOS()
            ? "libhappyphoton_libraw_bridge.dylib" : "libhappyphoton_libraw_bridge.so";
    private static string CompanionName() => OperatingSystem.IsWindows()
        ? "raw_r.dll" : OperatingSystem.IsMacOS() ? "libraw.25.dylib" : "libraw_r.so.25";
    private static string Hash(string path) => Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(path)));
    private static (string Bridge, string Companion) ExpectedHashes() =>
        OperatingSystem.IsWindows()
            ? ("0D2B21E9972BB712BB8CCB96C9597E5F91BE1FEFBF865C58CCC40907D45DE4D9",
               "FF0B8B51E89B13439DB544C45BCA7EAFCAD18AA0EEBF62F3AE9AC5AF5D3487B0")
            : OperatingSystem.IsMacOS()
                ? ("DF818F7B5D63595ABD7EA8ACC68211FA2B751AB36E8677CA8245F2AE02D3C414",
                   "7C7551653A0C4F0FCBE0934106CBE0A42599BF7BC615305027DF4F4CF802701E")
                : ("E9E7A25B9E07C6A3368C815C4108B0F12131EDC8CD84F142CD86DBC928AF0AD8",
                   "C36261B02F438DF9C918597F508D654F9F218CD72E3F7A954912C9702EDFE86D");
}
