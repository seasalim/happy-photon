using System.Reflection;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LibRawRuntimeHealthTests
{
    [Fact]
    public void RejectionDiagnostic_NamesComponentAndEveryObservedFact()
    {
        var health = Reject(new(
            1,
            0x001601,
            "0.22.1-Release",
            0x000000C0));

        Assert.Contains("component=LibRaw companion", health.DiagnosticText);
        Assert.Contains("observed bridge ABI=1", health.DiagnosticText);
        Assert.Contains("LibRaw version=0x001601", health.DiagnosticText);
        Assert.Contains("LibRaw version string=0.22.1-Release", health.DiagnosticText);
        Assert.Contains("capability mask=0x000000C0", health.DiagnosticText);
        Assert.Contains("Reinstall Happy Photon", health.DiagnosticText);
    }

    [Fact]
    public void ProcessHealth_EmitsOneDiagnosticAcrossRepeatedConsumers()
    {
        var messages = new List<string>();
        var probes = 0;
        var rejected = Reject(Healthy() with { Capabilities = LibRawCapabilities.Jpeg });
        var health = LibRawNativeSupport.CreateLazy(
            () =>
            {
                probes++;
                return rejected;
            },
            messages.Add);

        _ = health.Value;
        _ = health.Value;
        _ = health.Value;

        Assert.Equal(1, probes);
        Assert.Single(messages);
        Assert.Equal(rejected.DiagnosticText, messages[0]);
    }

    [Theory]
    [MemberData(nameof(Rejections))]
    public async Task EveryRejectionDisablesNativeBranchesAndSelectsFallback(
        LibRawRuntimeHealth health)
    {
        var raw = new RawBaseLoader(health);
        var standard = new StandardBaseLoader((_, _) =>
            new MagickImage(MagickColors.Green, 4, 3));
        var warnings = new List<string>();
        var router = new BaseLoaderRouter(
            raw,
            standard,
            () => false,
            warnings.Add);

        Assert.False(raw.CanLoad(new ImageFile("unavailable.cr2")));
        Assert.False(new LibRawProcessingService(health).IsAvailable);
        using var fallback = router.LoadPreviewBase(
            new ImageFile("fallback.cr2"),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        Assert.NotNull(fallback);
        Assert.Equal(BaseSourceKind.Standard, fallback!.Info.Kind);
        Assert.Empty(warnings);

        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(directory.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var imageService = new ImageService(
            catalog,
            router,
            new SourceAvailabilityService(),
            health);
        Assert.IsType<MagickNetRawService>(RawService(imageService));
    }

    [Theory]
    [MemberData(nameof(Rejections))]
    public void WindowsRafStillRefusesFallbackWhenRuntimeIsRejected(
        LibRawRuntimeHealth health)
    {
        var standard = new StandardBaseLoader((_, _) =>
            new MagickImage(MagickColors.Green, 4, 3));
        var router = new BaseLoaderRouter(
            new RawBaseLoader(health),
            standard,
            () => true,
            _ => throw new Xunit.Sdk.XunitException("Unexpected warning"));

        var result = router.LoadPreviewBase(
            new ImageFile("fallback.raf"),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.Null(result);
    }

    public static IEnumerable<object[]> Rejections()
    {
        yield return [Reject(Healthy() with { BridgeAbiVersion = 2 })];
        yield return [Reject(Healthy() with { LibRawVersionNumber = 0x001601 })];
        yield return [Reject(Healthy() with { Capabilities = LibRawCapabilities.Zlib })];
        yield return [Reject(Healthy() with { Capabilities = LibRawCapabilities.Jpeg })];
        yield return [Reject(new(
            null,
            null,
            null,
            null,
            LibRawRuntimeComponent.Bridge,
            LibRawDeploymentStage.Load,
            "unloadable bridge"))];
    }

    private static LibRawRuntimeObservations Healthy() => new(
        LibRawOutputConfiguration.Version,
        LibRawRuntimeHealthEvaluator.SupportedLibRawVersion,
        "0.22.2-Release",
        LibRawCapabilities.Jpeg | LibRawCapabilities.Zlib);

    private static LibRawRuntimeHealth Reject(LibRawRuntimeObservations observed)
    {
        var health = LibRawRuntimeHealthEvaluator.Evaluate(observed);
        Assert.False(health.IsHealthy);
        return health;
    }

    private static object RawService(ImageService imageService) =>
        typeof(ImageService).GetField(
            "_rawService",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(imageService)!;

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"happy-photon-runtime-health-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }
}
