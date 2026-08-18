using System.Globalization;
using System.Text;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal static class PrecisionEditCases
{
    public static void RunExposure(
        StringBuilder payload,
        PrecisionCensusManifest manifest,
        List<string> failures)
    {
        var population = manifest.Population("synthetic-exposure-sweep");
        using var fixture = PrecisionFixture.CreateChromaticAdaptationSweep(
            6504,
            ToFixturePopulation(population));
        foreach (var vector in manifest.ExposureSettings)
        {
            var settings = CreateExposureSettings(vector);
            var capture = PrecisionBoundaryCensus.Capture(
                fixture,
                settings,
                maxDimension: null);
            PrecisionReport.AppendCensusCase(
                payload,
                $"case-3-{vector.Id}",
                vector.Id,
                null,
                fixture,
                capture);
            failures.AddRange(capture.GateFailures.Select(failure =>
                $"case-3-{vector.Id}: {failure}"));
            var gain = Math.Pow(2, vector.ExposureEv);
            var knee = ToneLut.HighlightKnee(vector.Highlights);
            var aboveWhite = 0;
            var returned = 0;
            foreach (var source in fixture.ExpectedLinearRgb)
            {
                var exposed = source * gain;
                if (exposed <= 1)
                {
                    continue;
                }
                aboveWhite++;
                var shouldered = ToneLut.HighlightShoulder(exposed, knee);
                returned += shouldered <= 1 && shouldered >= 0 ? 1 : 0;
            }
            payload.Append("CENSUS_INTRA_STAGE case=case-3-")
                .Append(vector.Id)
                .Append(" population=").Append(population.Id)
                .Append(" location=exposure-before-highlight-shoulder")
                .Append(" storageBoundary=false")
                .Append(" aboveWhiteSamples=").Append(aboveWhite)
                .Append(" returnedToRange=").Append(returned)
                .Append(" channelSamples=").Append(fixture.ExpectedLinearRgb.Length)
                .Append(" basis=exact-full-population").AppendLine();
            if (vector.ExposureEv > 0 && (aboveWhite == 0 || returned == 0))
            {
                failures.Add(
                    $"case-3-{vector.Id} did not cover above-white shoulder recovery");
            }
            var toneBoundary = capture.Boundaries.Single(boundary =>
                boundary.Name == "post-tone");
            PrecisionEvidenceReport.AppendBoundary(
                payload,
                "case-3-exposure-swings",
                population.Id,
                toneBoundary with { Name = $"post-tone/{vector.Id}" },
                capture.WorkingStorageQuality,
                phaseZeroThresholdCrossed: false);
        }
    }

    public static void RunStacked(
        StringBuilder payload,
        PrecisionCensusManifest manifest,
        List<string> failures)
    {
        var population = manifest.Population("synthetic-stacked-pattern");
        using var fixture = PrecisionFixture.CreateChromaticAdaptationSweep(
            6504,
            ToFixturePopulation(population));
        var settings = CreateStackedSettings(manifest.StackedSettings);
        var stacked = PrecisionBoundaryCensus.CaptureStacked(
            fixture,
            settings,
            manifest.StackedSettings.MaxDimension);
        PrecisionReport.AppendCensusCase(
            payload,
            "case-4-stacked-edits",
            "stacked",
            manifest.StackedSettings.MaxDimension,
            fixture,
            stacked.Capture);
        failures.AddRange(stacked.Capture.GateFailures.Select(failure =>
            $"case-4-stacked-edits: {failure}"));
        payload.Append("CENSUS_STAGE_GATE case=case-4-stacked-edits")
            .Append(" population=").Append(population.Id);
        foreach (var stage in stacked.StageExecuted.OrderBy(pair => pair.Key))
        {
            payload.Append(' ').Append(stage.Key).Append('=')
                .Append(stage.Value ? "pass" : "fail");
        }
        payload.AppendLine();
        foreach (var boundary in stacked.Capture.Boundaries.Where(boundary =>
            boundary.Scope == PrecisionBoundaryScope.WorkingStorage))
        {
            stacked.Quality.TryGetValue(boundary.Name, out var quality);
            PrecisionEvidenceReport.AppendBoundary(
                payload,
                "case-4-stacked-edits",
                population.Id,
                boundary,
                quality);
        }
        payload.AppendLine(
            "CENSUS_STACKED_TOTAL case=case-4-stacked-edits " +
            "attribution=per-boundary-first totalOracle=inapplicable " +
            "reason=native-and-detail-boundaries-have-no-oracle");
    }

    private static EditSettings CreateExposureSettings(
        PrecisionExposureManifest vector) =>
        new()
        {
            Exposure = vector.ExposureEv,
            Highlights = vector.Highlights,
            BaseLook = false,
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = 6504,
                Tint = 0
            },
            Detail = new DetailSettings
            {
                CaptureSharpen = 0,
                NoiseReduction = FbddMode.Off,
                ChromaNr = 0
            }
        };

    private static EditSettings CreateStackedSettings(
        PrecisionStackedManifest value) =>
        new()
        {
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = value.Kelvin,
                Tint = value.Tint
            },
            Exposure = value.ExposureEv,
            Brightness = value.Brightness,
            Contrast = value.Contrast,
            Shadows = value.Shadows,
            Highlights = value.Highlights,
            Saturation = value.Saturation,
            Vibrance = value.Vibrance,
            BaseLook = false,
            Detail = new DetailSettings
            {
                CaptureSharpen = value.CaptureSharpen,
                NoiseReduction = FbddMode.Off,
                ChromaNr = value.ChromaNr
            },
            Rotation = checked((int)value.Rotation),
            Crop = new CropRegion
            {
                Left = value.CropLeft,
                Top = value.CropTop,
                Right = value.CropRight,
                Bottom = value.CropBottom
            }
        };

    private static PrecisionFixturePopulation ToFixturePopulation(
        PrecisionPopulationManifest value) =>
        new(value.Id, value.Kind, value.RowSemantics, value.Intensity);
}
