using System.Text.Json;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WysiwygCalibrationTests
{
    [Fact]
    public void IntegratedGate_ReportsPreviewExportCalibration()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_WYSIWYG") != "1",
            "Set HAPPY_PHOTON_WYSIWYG=1 to run WYSIWYG calibration.");
        var reportPath = Environment.GetEnvironmentVariable(
            "HAPPY_PHOTON_WYSIWYG_REPORT") ??
            throw new InvalidOperationException(
                "HAPPY_PHOTON_WYSIWYG_REPORT is required.");
        var observations = new List<WysiwygObservation>();
        var renderer = new CurrentPipelineGoldenRenderer();

        foreach (var asset in GoldenTestCases.Assets)
        {
            if (asset.IsHeic &&
                MagickFormatInfo.Create(MagickFormat.Heic) is not
                    { SupportsReading: true })
            {
                observations.Add(new WysiwygObservation(
                    asset.Slug,
                    asset.IsRaw ? "crossing-on" : "crossing-off",
                    null,
                    null,
                    null,
                    "ImageMagick has no HEIC reader"));
                continue;
            }

            using var previewBase = renderer.LoadPreviewBase(asset);
            using var exportBase = renderer.LoadBase(asset);
            foreach (var settingsCase in asset.SettingsCases)
            {
                using var preview = renderer.Render(
                    previewBase,
                    settingsCase.CreateSettings(),
                    RenderIntent.Preview);
                using var export = renderer.Render(
                    exportBase,
                    settingsCase.CreateSettings(),
                    RenderIntent.Export);
                WysiwygTests.AlignForComparison(export, preview);
                var comparison = GoldenImageComparer.Compare(
                    export,
                    preview,
                    GoldenComparisonDomain.DisplaySrgb);
                observations.Add(new WysiwygObservation(
                    asset.Slug,
                    asset.IsRaw ? "crossing-on" : "crossing-off",
                    settingsCase.Slug,
                    comparison.MeanDeltaE,
                    comparison.P99DeltaE,
                    null));
            }
        }

        File.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(
                observations,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }) + Environment.NewLine);
    }

    private sealed record WysiwygObservation(
        string Asset,
        string Regime,
        string? Case,
        double? MeanDeltaE,
        double? P99DeltaE,
        string? SkipReason);
}
