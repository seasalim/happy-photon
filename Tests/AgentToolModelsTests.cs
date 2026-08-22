using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AgentToolModelsTests
{
    private static readonly JsonSerializerOptions Options = AgentToolJson.Options;

    [Fact]
    public void ImageSummary_RoundTripsWithCamelCaseAndStringEnums()
    {
        var summary = new AgentImageSummary(
            Id: @"C:\photos\a.jpg", FileName: "a.jpg", Rating: 3, Flag: "picked",
            HasEdits: true, MetadataLoaded: true, PixelWidth: 6000, PixelHeight: 4000,
            DateTaken: new DateTime(2026, 7, 1, 10, 30, 0), Camera: "Canon EOS R5",
            Iso: 100, FNumber: 2.8, ExposureTime: "1/250", FocalLength: 35,
            LensModel: "RF 35mm F1.8", BurstId: "burst_1", BurstIndex: 2,
            BurstSize: 4)
        {
            SourceAvailability = "requires_hydration"
        };

        var json = JsonSerializer.Serialize(summary, Options);
        Assert.Contains("\"fileName\"", json);
        Assert.Contains("\"picked\"", json);
        Assert.Contains("\"burstId\":\"burst_1\"", json);
        Assert.Contains("\"sourceAvailability\":\"requires_hydration\"", json);
        Assert.Contains("\"colorLabel\":\"none\"", json);

        var back = JsonSerializer.Deserialize<AgentImageSummary>(json, Options);
        Assert.Equal(summary, back);
    }

    [Fact]
    public void ImageSummary_OmitsNullBurstFields()
    {
        var summary = new AgentImageSummary(
            Id: "a", FileName: "a.jpg", Rating: 0, Flag: "unflagged",
            HasEdits: false, MetadataLoaded: false, PixelWidth: 0, PixelHeight: 0,
            DateTaken: null, Camera: null, Iso: null, FNumber: null,
            ExposureTime: null, FocalLength: null, LensModel: null);

        var json = JsonSerializer.Serialize(summary, Options);

        Assert.DoesNotContain("burstId", json);
        Assert.DoesNotContain("burstIndex", json);
        Assert.DoesNotContain("burstSize", json);
    }

    [Fact]
    public void LibraryState_SerializesBurstAnalysisStatus()
    {
        var state = new AgentLibraryState(
            "photos", 3, 2, new AgentFilterState("all", "all", 0), "a", true);

        var json = JsonSerializer.Serialize(state, Options);

        Assert.Contains("\"burstsComputed\":true", json);
    }

    [Fact]
    public void BatchResult_SerializesFailures()
    {
        var result = new AgentBatchResult(
            Succeeded: new List<string> { "a" },
            Failed: new List<AgentBatchFailure> { new("b", "unknown image id") });

        var json = JsonSerializer.Serialize(result, Options);
        Assert.Contains("unknown image id", json);
    }

    [Fact]
    public void BatchFailure_SerializesHydrationCode()
    {
        var failure = new AgentBatchFailure(
            "a.jpg",
            "source requires hydration",
            "hydration_required");

        var json = JsonSerializer.Serialize(failure, Options);

        Assert.Contains("\"code\":\"hydration_required\"", json);
    }

    [Fact]
    public void BatchCap_ValidatesLimits()
    {
        Assert.Null(AgentToolValidation.CheckBatchCap(new string[500], 500));
        Assert.NotNull(AgentToolValidation.CheckBatchCap(new string[501], 500));
        Assert.Contains("500", AgentToolValidation.CheckBatchCap(new string[501], 500));
    }

    [Fact]
    public void FlagParsing_AcceptsKnownValuesCaseInsensitive()
    {
        Assert.Equal(ImageFlag.Picked, AgentToolValidation.ParseFlag("Picked"));
        Assert.Equal(ImageFlag.Rejected, AgentToolValidation.ParseFlag("rejected"));
        Assert.Equal(ImageFlag.Unflagged, AgentToolValidation.ParseFlag("unflagged"));
        Assert.Throws<AgentToolException>(() => AgentToolValidation.ParseFlag("maybe"));
    }

    [Fact]
    public void ColorLabelParsing_RejectsUnknownValues()
    {
        Assert.Equal(ColorLabel.Red, AgentToolValidation.ParseColorLabel("Red"));
        Assert.Equal(ColorLabel.None, AgentToolValidation.ParseColorLabel("none"));
        Assert.Throws<AgentToolException>(() =>
            AgentToolValidation.ParseColorLabel("orange"));
    }

    [Fact]
    public void JsonOptions_SerializeEnumsAsCamelCaseStrings()
    {
        var json = JsonSerializer.Serialize(ImageFlag.Unflagged, Options);

        Assert.Equal("\"unflagged\"", json);
    }

    [Fact]
    public void ExportOptions_RoundTripsFormatAndVariants()
    {
        var options = new AgentExportOptions(
            OutputFolder: "exports", Quality: 90, MaxDimension: null,
            NamingPattern: "{name}_edited", Format: "webp",
            OutputColorSpace: "displayP3",
            Variants: new List<AgentExportVariant>
            {
                new("Hi Res!", null),
                new("web", 1200)
            });

        var json = JsonSerializer.Serialize(options, Options);
        Assert.Contains("\"format\":\"webp\"", json);
        Assert.Contains("\"outputColorSpace\":\"displayP3\"", json);
        Assert.Contains("\"variants\"", json);
        Assert.Contains("\"maxDimension\":1200", json);

        var back = JsonSerializer.Deserialize<AgentExportOptions>(json, Options);
        Assert.NotNull(back);
        Assert.Equal(options.OutputFolder, back.OutputFolder);
        Assert.Equal(options.Quality, back.Quality);
        Assert.Equal(options.NamingPattern, back.NamingPattern);
        Assert.Equal(options.Format, back.Format);
        Assert.Equal(options.OutputColorSpace, back.OutputColorSpace);
        Assert.Equal(options.Variants, back.Variants);
    }

    [Fact]
    public void AgentExportOptions_DefaultOutputColorSpaceIsSrgb()
    {
        var defaults = new AgentExportOptions();
        Assert.Equal(
            OutputColorSpace.Srgb,
            AgentToolValidation.ParseOutputColorSpace(defaults.OutputColorSpace));
    }

    [Theory]
    [InlineData("jpeg", ExportFormat.Jpeg)]
    [InlineData("JPG", ExportFormat.Jpeg)]
    [InlineData("PNG", ExportFormat.Png)]
    [InlineData("WebP", ExportFormat.Webp)]
    [InlineData("tiff", ExportFormat.Tiff)]
    [InlineData("TIF", ExportFormat.Tiff)]
    public void ExportFormatParsing_AcceptsKnownValuesCaseInsensitive(
        string value, ExportFormat expected)
    {
        Assert.Equal(expected, AgentToolValidation.ParseExportFormat(value));
    }

    [Fact]
    public void ExportFormatParsing_RejectsUnknownValue()
    {
        Assert.Throws<AgentToolException>(() =>
            AgentToolValidation.ParseExportFormat("bmp"));
    }

    [Theory]
    [InlineData("srgb", OutputColorSpace.Srgb)]
    [InlineData("DisplayP3", OutputColorSpace.DisplayP3)]
    [InlineData("display-p3", OutputColorSpace.DisplayP3)]
    public void OutputColorSpaceParsing_AcceptsKnownValues(
        string value,
        OutputColorSpace expected) =>
        Assert.Equal(expected, AgentToolValidation.ParseOutputColorSpace(value));

    [Fact]
    public void OutputColorSpaceParsing_RejectsUnknownValue() =>
        Assert.All(
            new string?[] { "adobe-rgb", null },
            value => Assert.Throws<AgentToolException>(() =>
                AgentToolValidation.ParseOutputColorSpace(value)));

    [Theory]
    [InlineData(" Hi Res! ", "hi-res-")]
    [InlineData("SOCIAL_1", "social_1")]
    [InlineData("web/mobile", "web-mobile")]
    public void VariantNameSanitizing_NormalizesSafeFolderName(
        string value, string expected)
    {
        Assert.Equal(expected, AgentToolValidation.SanitizeVariantName(value));
    }

    [Fact]
    public void VariantNameSanitizing_RejectsEmptyName()
    {
        Assert.Throws<AgentToolException>(() =>
            AgentToolValidation.SanitizeVariantName("   "));
    }

    [Fact]
    public void EditSettingsInput_DefaultVersionMapsCurrentSettings()
    {
        var input = new AgentEditSettingsInput(
            1.5, 10, 20, 30, 40, 50, 60);

        var patch = AgentEditSettingsMapper.CreatePatch(input);
        var settings = new EditSettings();
        patch.ApplyTo(settings);

        Assert.Equal(EditSettings.CurrentVersion, settings.Version);
        Assert.Equal(1.5, settings.Exposure);
        Assert.Equal(WbMode.AsShot, settings.Wb.Mode);
        Assert.Equal(60, settings.Highlights);
    }

    [Fact]
    public void EditSettingsInput_SerializesCurrentShapeWithoutTemperature()
    {
        var input = new AgentEditSettingsInput(1, 2, 3, 4, 5, 6, 7);

        var json = JsonSerializer.Serialize(input, Options);

        Assert.Contains("\"version\":3", json);
        Assert.DoesNotContain("temperature", json);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    public void EditSettingsInput_RejectsUnsupportedVersion(int version)
    {
        var input = new AgentEditSettingsInput(
            0, 0, 0, 0, 0, 0, 0, Version: version);

        Assert.Throws<AgentToolException>(() =>
            AgentEditSettingsMapper.CreatePatch(input));
    }

    [Fact]
    public void EditSettingsInput_MapsCurrentColorSettings()
    {
        var input = new AgentEditSettingsInput(
            Exposure: 1,
            Brightness: 2,
            Contrast: 3,
            Saturation: 4,
            Vibrance: 5,
            Shadows: 6,
            Highlights: 7,
            Version: EditSettings.CurrentVersion,
            Wb: new AgentWhiteBalanceInput(
                "custom", Kelvin: 7200, Tint: -10),
            BaseLook: true,
            HlReconstruction: "clip");

        var patch = AgentEditSettingsMapper.CreatePatch(input);
        var settings = new EditSettings();
        patch.ApplyTo(settings);

        Assert.Equal(EditSettings.CurrentVersion, settings.Version);
        Assert.Equal(WbMode.Custom, settings.Wb.Mode);
        Assert.Equal(7200, settings.Wb.Kelvin);
        Assert.Equal(-10, settings.Wb.Tint);
        Assert.True(settings.BaseLook);
        Assert.Equal(HlReconstructionMode.Clip, settings.HlReconstruction);
        Assert.Null(settings.Detail.CaptureSharpen);
        Assert.Equal(FbddMode.Off, settings.Detail.NoiseReduction);
        Assert.Equal(0, settings.Detail.ChromaNr);
    }

    [Fact]
    public void EditSettingsInput_AbsentWidenedFieldsPreserveCurrentValues()
    {
        var input = new AgentEditSettingsInput(
            1, 2, 3, 4, 5, 6, 7, Version: EditSettings.CurrentVersion);
        var target = new EditSettings
        {
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = 6500,
                Tint = 10
            },
            BaseLook = true,
            HlReconstruction = HlReconstructionMode.Clip,
            Detail = new DetailSettings
            {
                CaptureSharpen = 40,
                NoiseReduction = FbddMode.Full,
                ChromaNr = 30
            }
        };

        AgentEditSettingsMapper.CreatePatch(input).ApplyTo(target);

        Assert.Equal(WbMode.Custom, target.Wb.Mode);
        Assert.True(target.BaseLook);
        Assert.Equal(HlReconstructionMode.Clip, target.HlReconstruction);
        Assert.Equal(FbddMode.Full, target.Detail.NoiseReduction);
    }

    [Fact]
    public void EditSettingsInput_ReplaceClearsUnexposedChannelCurves()
    {
        var target = new EditSettings
        {
            CurveRed = new CurveData(),
            CurveGreen = new CurveData(),
            CurveBlue = new CurveData()
        };
        target.CurveRed!.AddPointAndReturnIndex(0.5, 0.7);

        AgentEditSettingsMapper.CreatePatch(new AgentEditSettingsInput(
            0, 0, 0, 0, 0, 0, 0)).ApplyTo(target);

        Assert.Null(target.CurveRed);
        Assert.Null(target.CurveGreen);
        Assert.Null(target.CurveBlue);
    }

    [Fact]
    public void EditSettingsInput_ReplaceClearsEffects()
    {
        var target = new EditSettings
        {
            Effects = new EffectsSettings
            {
                Vignette = -40,
                Grain = 30
            }
        };

        AgentEditSettingsMapper.CreatePatch(new AgentEditSettingsInput(
            0, 0, 0, 0, 0, 0, 0)).ApplyTo(target);

        Assert.Null(target.Effects);
    }

    [Fact]
    public void EditSettingsInput_ReplaceClearsColorMixer()
    {
        var target = new EditSettings { Mixer = new ColorMixerSettings() };
        target.Mixer.Green.Saturation = 40;

        AgentEditSettingsMapper.CreatePatch(new AgentEditSettingsInput(
            0, 0, 0, 0, 0, 0, 0)).ApplyTo(target);

        Assert.Null(target.Mixer);
    }

    [Fact]
    public void EditSettingsInput_RejectsRemovedManualWhiteBalanceMode()
    {
        var input = new AgentEditSettingsInput(
            0, 0, 0, 0, 0, 0, 0,
            Wb: new AgentWhiteBalanceInput("manual", Gains: [1.1, 1, 0.9]));

        Assert.Throws<AgentToolException>(() =>
            AgentEditSettingsMapper.CreatePatch(input));
    }
}
