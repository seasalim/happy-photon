using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

// Run each fixture in five fresh Release processes under the measure host lock.
// Report process medians; the envelope applies to the median of all five processes.
public sealed class AutoStraightenTimingTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task WarmBasePressToUiOutcome()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") == "1",
            "Opt-in Crop Auto timing requires the measure host lock");
        Assert.Equal("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_FULL_CPU"));
        Assert.True(Environment.ProcessorCount > 2);
#if DEBUG
        Assert.Fail("Use Release");
#endif
        var name = Environment.GetEnvironmentVariable("HAPPY_PHOTON_STRAIGHTEN_FIXTURE")
            ?? "nikon-d300-colorchecker.nef";
        Assert.Contains(name, new[] { "nikon-d300-colorchecker.nef", "canon-eos-6d-iso-6400.cr2" });
        var positive = name == "nikon-d300-colorchecker.nef";
        var path = GoldenTestPaths.Asset(name);
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));
        var expectedHash = positive
            ? "96c947a3289c21ef34e609640f441bb5ae4f8f85bd9ff7194eeb0ff1d4063ed0"
            : "7727ee0280b44ea1d633962f49942f37f3c7ec6d704d22e108a5223666327c32";

        using (var stream = File.OpenRead(path))
        {
            Assert.Equal(expectedHash, Convert.ToHexStringLower(SHA256.HashData(stream)));
        }

        using var fixture = new CatalogVmFixture("auto-straighten-timing");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog,
            new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsDevelopMode = true;
        var image = new ImageFile(path) { CatalogId = await catalog.GetOrCreateImageAsync(path) };
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        await SettleAsync(vm);
        var settings = image.EditSettings.Clone();
        var detection = new double[11];
        object? token = null;

        for (var i = -3; i < detection.Length; i++)
        {
            var sample = await vm.ImageService.Previews.SamplePreviewBaseAsync(image, settings, pixels =>
            {
                var start = Stopwatch.GetTimestamp();
                var result = HorizonDetection.Detect(pixels);

                return (result, elapsed: Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            });
            Assert.NotNull(sample);
            Assert.InRange(sample.Value.result.HorizonRotation, -5, 5);
            if (positive) Assert.NotEqual(0, sample.Value.result.HorizonRotation);
            token ??= sample.BaseToken;
            Assert.Same(token, sample.BaseToken);
            if (i >= 0) detection[i] = sample.Value.elapsed;
        }

        var presses = new double[11];

        for (var i = -3; i < presses.Length; i++)
        {
            vm.HorizonRotation = 5;
            vm.TransientStatus = null;
            await SettleAsync(vm);
            Assert.True(await vm.ImageService.Previews.IsPreviewBaseCurrentAsync(image, settings, token!));
            double? elapsed = null;
            var start = Stopwatch.GetTimestamp();
            void Observe(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName != nameof(vm.HorizonRotation)) return;

                Assert.True(Dispatcher.UIThread.CheckAccess());
                elapsed ??= Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }

            vm.PropertyChanged += Observe;

            try
            {
                start = Stopwatch.GetTimestamp();
                await vm.AutoStraightenCommand.ExecuteAsync(null);
            }
            finally
            {
                vm.PropertyChanged -= Observe;
            }

            Assert.NotNull(elapsed);
            if (i >= 0) presses[i] = elapsed.Value;
        }

        await SettleAsync(vm);
        output.WriteLine("AUTO_STRAIGHTEN " + JsonSerializer.Serialize(new
        {
            fixture = name, pid = Environment.ProcessId, warmups = 3,
            detection, detectionMedian = detection.Order().ElementAt(5),
            presses, pressMedian = presses.Order().ElementAt(5)
        }));
    }

    private static async Task SettleAsync(MainWindowViewModel vm)
    {
        if (vm.PendingPreviewDebounceTask is { } pending) await pending.WaitAsync(TestWaits.Condition);

        await TestWaits.UntilAsync(() => vm.CaptureBackgroundActivitySnapshot().PreviewCount == 0);
    }
}
