using System.Net;
using System.Text;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ColorMixerLookGateTests
{
    [Fact]
    public void ColorChecker_GeneratesBandIsolationSheet()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_MIXER_LOOKGATE") != "1",
            "Set HAPPY_PHOTON_MIXER_LOOKGATE=1 and " +
            "HAPPY_PHOTON_MIXER_LOOKGATE_DIR to generate the review sheet.");
        var outputDirectory = Environment.GetEnvironmentVariable(
            "HAPPY_PHOTON_MIXER_LOOKGATE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(outputDirectory));
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var imageDirectory = Directory.CreateDirectory(Path.Combine(
            outputDirectory,
            "images")).FullName;
        var manifest = ColorCheckerManifest.Load();
        using var baseImage = new RawBaseLoader().LoadPreviewBase(
            new ImageFile(GoldenTestPaths.Asset(manifest.Fixture.FileName)),
            BaseDecodeSettings.Default,
            CancellationToken.None) ?? throw new InvalidOperationException(
                "The ColorChecker mixer fixture did not decode.");
        var pipeline = new RenderPipeline();
        var html = new StringBuilder("""
            <!doctype html><meta charset="utf-8"><title>HSL mixer band isolation</title>
            <style>body{background:#131318;color:#e4e1e9;font:14px sans-serif}
            .grid{display:grid;grid-template-columns:repeat(3,minmax(280px,1fr));gap:16px}
            figure{margin:0;background:#1f1f25;padding:10px;border-radius:8px}
            img{width:100%;display:block}figcaption{margin-top:8px}</style>
            <h1>HSL mixer · ColorChecker band isolation</h1><div class="grid">
            """);
        ushort[]? identity = null;
        var bands = new ColorMixerBand?[] { null }
            .Concat(Enum.GetValues<ColorMixerBand>().Select(
                value => (ColorMixerBand?)value));
        foreach (var band in bands)
        {
            var settings = CreateSettings(band);
            using var result = pipeline.Render(new RenderRequest(
                baseImage,
                settings,
                RenderIntent.Preview,
                600,
                new RenderOptions(false, false)));
            var name = band?.ToString().ToLowerInvariant() ?? "identity";
            var fileName = $"colorchecker-mixer-{name}.png";
            result.Image.Write(
                Path.Combine(imageDirectory, fileName),
                ExportEncoder.CreatePngWriteDefines());
            var pixels = RenderPipelineTestSupport.ReadPixels(result.Image);
            if (identity == null)
            {
                identity = pixels;
            }
            else
            {
                Assert.NotEqual(identity, pixels);
            }
            html.Append("<figure><img src=\"images/")
                .Append(WebUtility.HtmlEncode(fileName))
                .Append("\"><figcaption>")
                .Append(WebUtility.HtmlEncode(band?.ToString() ?? "Identity"))
                .Append(" · Saturation +80</figcaption></figure>");
        }
        html.Append("</div>");
        File.WriteAllText(
            Path.Combine(outputDirectory, "color-mixer-lookgate.html"),
            html.ToString());
    }

    private static EditSettings CreateSettings(ColorMixerBand? band)
    {
        if (band == null)
        {
            return new EditSettings();
        }

        var settings = new EditSettings { Mixer = new ColorMixerSettings() };
        settings.Mixer.GetBand(band.Value).Saturation = 80;
        return settings;
    }
}
