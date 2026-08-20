using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class ViewportRestingPerformanceTests
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ViewportRestingPerformanceTests(
        AvaloniaTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [WindowsFact]
    public async Task RestingCancellationLeavesNextTickWithinBudget_WhenEnabled()
    {
        _fixture.RequireWindows();
        if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1")
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"HappyPhotonRestingPerf_{Guid.NewGuid():N}");
        try
        {
            using var catalog = new CatalogService(root);
            await catalog.InitializeAsync();
            await using var service = CreateService(catalog);
            var file = new ImageFile(Asset("canon-eos-6d-iso-6400.cr2"));
            var (interactive, _) = await service.ApplyEditsToPreviewAsync(
                file,
                new EditSettings(),
                skipHistogram: true);
            using (interactive)
            {
                Assert.NotNull(interactive);
                var parent = service.TryGetPreviewRenderIdentity(interactive!);
                Assert.NotNull(parent);
                using var stageStarted = new ManualResetEventSlim();
                using var cancellation = new CancellationTokenSource();
                service.RestingStageStarted = stage =>
                {
                    if (stage == "raw-crossing") stageStarted.Set();
                };
                var resting = service.RenderRestingPreviewAsync(
                    file,
                    new EditSettings(),
                    2826,
                    parent!,
                    cancellation.Token);
                Assert.True(stageStarted.Wait(TestWaits.Condition));

                var stopwatch = Stopwatch.StartNew();
                cancellation.Cancel();
                var (next, _) = await service.ApplyEditsToPreviewAsync(
                    file,
                    new EditSettings { Contrast = 25 },
                    skipHistogram: true);
                using (next)
                {
                    stopwatch.Stop();
                    try
                    {
                        using var superseded = await resting;
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    _output.WriteLine(
                        $"resting cancellation to next tick: " +
                        $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms");
                    Assert.NotNull(next);
                    Assert.True(
                        stopwatch.Elapsed.TotalMilliseconds <= 150,
                        "Resting cancellation delayed the next slider tick beyond " +
                        $"150 ms: {stopwatch.Elapsed.TotalMilliseconds:F1} ms.");
                }
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static PreviewService CreateService(CatalogService catalog) =>
        new(
            catalog,
            new BaseLoaderRouter(
                new RawBaseLoader(),
                new StandardBaseLoader()),
            new RenderPipeline(),
            new HistogramService(),
            new PreviewCacheService(catalog),
            new RenderedThumbnailCacheService(catalog),
            createRenderedThumbnail: false);

    private static string Asset(string fileName) =>
        Path.Combine(GoldenTestPaths.AssetDirectory, fileName);
}
