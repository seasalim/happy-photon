using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class MixerBandMaskGateTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task WarmedBuildMeetsPinnedLocalRangeControl()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in WP14 measure gate");
        Assert.True(Environment.ProcessorCount > 2, "Full CPU required");
#if DEBUG
        throw new InvalidOperationException("Use Release for measurements");
#endif
        const double reference = 9.47;
        var path = GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2");
        GoldenTestPaths.RequireReadableFixture(path);
        using var fixture = new CatalogVmFixture("mixer-mask-gate");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog,
            new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), _ => Task.CompletedTask);
        var settings = new EditSettings
        {
            Locals = [new() { Type = "radial", Rx = .3, Ry = .2, Angle = 30, Feather = .5,
                Hue = new() { Enabled = true, Center = 350 },
                Luminance = new() { Enabled = true, Lower = .47 } }]
        };
        var image = new ImageFile(path) { EditSettings = settings, CatalogId = await catalog.GetOrCreateImageAsync(path) };
        await catalog.SaveEditSettingsAsync(image.CatalogId, settings);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        var size = vm.PreviewImage!.PixelSize;
        var edge = Math.Max(size.Width, size.Height);
        var local = settings.Locals![0];
        local.Rx = Math.Sqrt(.45 * size.Width / edge * size.Height / edge / (Math.PI * 2 / 3));
        local.Ry = local.Rx * 2 / 3;
        var color = HappyPhotonColors.LocalMaskColor;
        var tint = (uint)(color.R << 16 | color.G << 8 | color.B);

        WriteableBitmap Build(bool band)
        {
            using var lease = vm.ImageService.Previews.AcquireLocalRangeBase(image, settings, edge);
            Assert.NotNull(lease);

            return band
                ? MixerBandMaskRenderer.Render(lease.Base, settings, ColorMixerBand.Red, size, tint, CancellationToken.None)
                : LocalRangeMaskRenderer.Render(lease.Base, settings, local, size, tint, CancellationToken.None);
        }

        for (var warmup = 0; warmup < 10; warmup++)
        {
            using var control = Build(false);
            using var band = Build(true);
        }

        var controls = new double[15];
        var bands = new double[15];

        for (var run = 0; run < 15; run++)
        {
            controls[run] = Measure(() => Build(false));
            bands[run] = Measure(() => Build(true));
        }

        var controlMedian = controls.Order().ElementAt(7);
        var bandMedian = bands.Order().ElementAt(7);
        output.WriteLine($"G5 size={size} reference_ms={reference} control_ms={controlMedian:R} band_ms={bandMedian:R} " +
            $"control_samples=[{string.Join(',', controls)}] band_samples=[{string.Join(',', bands)}]");
        Assert.True(Math.Abs(controlMedian - reference) <= Math.Max(3, reference * .15), "Invalid control run: re-measure; never relax");
        // Owner ruling 2026-10-08: a full-frame mask cannot meet the hue-range control; ≤ 30 ms with the neutral skip.
        Assert.True(bandMedian <= 30, "Mixer mask median exceeds 30 ms");
    }

    private static double Measure(Func<WriteableBitmap> build)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var started = Stopwatch.GetTimestamp();
        using var bitmap = build();
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        return elapsed;
    }
}
