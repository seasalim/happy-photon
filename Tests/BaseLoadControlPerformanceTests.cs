using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class BaseLoadControlPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void FullBaseJpeg_ReportsFirstAndWarmCost_WhenEnabled()
    {
        RequirePerf();
        var loader = new StandardBaseLoader();
        var file = new ImageFile(CullPerfFiles.GeneratedJpeg());
        var report = BaseLoadMeasurement.Measure("jpeg-24mp-full", () =>
        {
            var loaded = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
            Assert.NotNull(loaded);
            Assert.Equal(6000u, loaded.Pixels.Width);
            Assert.Equal(4000u, loaded.Pixels.Height);
            return loaded;
        }, output);
        WriteReport(nameof(FullBaseJpeg_ReportsFirstAndWarmCost_WhenEnabled), report);
    }

    [Fact]
    public void DisplayP3Preview_ReportsFirstAndWarmCost_WhenEnabled()
    {
        RequirePerf();
        var loader = new StandardBaseLoader();
        var file = new ImageFile(GeneratedP3Jpeg());
        var report = BaseLoadMeasurement.Measure("display-p3-24mp-preview", () =>
        {
            var pair = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, CancellationToken.None).Pair;
            Assert.NotNull(pair);
            Assert.True(pair.Interactive.Info.HadIccProfile);
            return pair;
        }, output);
        WriteReport(nameof(DisplayP3Preview_ReportsFirstAndWarmCost_WhenEnabled), report);
    }

    private static string GeneratedP3Jpeg()
    {
        var source = CullPerfFiles.GeneratedJpeg();
        var path = Path.Combine(Path.GetDirectoryName(source)!, "generated-24mp-display-p3.jpg");
        if (!File.Exists(path))
        {
            using var image = new MagickImage(source);
            image.TransformColorSpace(ColorProfiles.SRGB, OutputColorProfiles.Get(OutputColorSpace.DisplayP3));
            image.Quality = 90;
            image.Write(path, MagickFormat.Jpeg);
        }
        using var check = new MagickImage();
        check.Ping(path);
        Assert.NotNull(check.GetColorProfile());
        Assert.Equal(6000u, check.Width);
        Assert.Equal(4000u, check.Height);
        return path;
    }

    private static void RequirePerf()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in baseline.");
        PerfEnvironment.AssertFullCpu();
    }

    private static void WriteReport(string name, object report)
    {
        if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_STAGE_REPORT_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(report, CullPerfFiles.Json));
    }
}
