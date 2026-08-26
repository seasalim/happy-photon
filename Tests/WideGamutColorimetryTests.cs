using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[CollectionDefinition(CheckpointCRenderGateCollection.Name,
    DisableParallelization = true)]
public sealed class CheckpointCRenderGateCollection
{
    public const string Name = "Checkpoint C render gates";
}

[Collection(CheckpointCRenderGateCollection.Name)]
public sealed class WideGamutColorimetryTests
{
    private readonly ITestOutputHelper _output;

    public WideGamutColorimetryTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<GoldenSettingsCase> SettingsCases
    {
        get
        {
            var data = new TheoryData<GoldenSettingsCase>();
            foreach (var settingsCase in GoldenTestCases.Assets[0].SettingsCases)
            {
                data.Add(settingsCase);
            }
            return data;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditedSyntheticFinalization_AgreesAcrossTargets(bool outputSharpening)
    {
        using var baseImage = CreateAgreementBase();
        var settings = CreateAgreementSettings();
        using var shared = RenderShared(baseImage, settings);
        using var prepared = PrepareForEligibility(shared, outputSharpening);
        using var srgb = Finalize(shared, OutputColorSpace.Srgb, outputSharpening);
        using var p3 = Finalize(shared, OutputColorSpace.DisplayP3, outputSharpening);
        var comparison = MeanDeltaE00(srgb, p3, prepared);
        var encoded = MeanDeltaE00EightBit(srgb, p3, prepared);
        _output.WriteLine(
            $"synthetic sharpen={outputSharpening}: Q16 mean ΔE00=" +
            $"{comparison.Mean:F4} over {comparison.Count} in-gamut pixels; " +
            $"RGB8 informational={encoded:F4}");

        Assert.Equal(checked((int)(srgb.Width * srgb.Height)), comparison.Count);
        Assert.True(
            comparison.Mean <= 0.034,
            $"Synthetic mean ΔE00 {comparison.Mean:F4} exceeds 0.034.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditedRealRawFinalization_AgreesAcrossTargets(bool outputSharpening)
    {
        var path = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "canon-eos-6d-iso-6400.cr2");
        using var baseImage = new RawBaseLoader().LoadFullBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None) ?? throw new InvalidOperationException(
                "Real-RAW agreement fixture did not decode.");
        using var shared = RenderShared(baseImage, CreateAgreementSettings());
        using var prepared = PrepareForEligibility(shared, outputSharpening);
        using var srgb = Finalize(shared, OutputColorSpace.Srgb, outputSharpening);
        using var p3 = Finalize(shared, OutputColorSpace.DisplayP3, outputSharpening);
        var comparison = MeanDeltaE00(srgb, p3, prepared);
        var encoded = MeanDeltaE00EightBit(srgb, p3, prepared);
        _output.WriteLine(
            $"real-RAW sharpen={outputSharpening}: Q16 mean ΔE00=" +
            $"{comparison.Mean:F4} over {comparison.Count} in-gamut pixels; " +
            $"RGB8 informational={encoded:F4}");

        Assert.True(comparison.Count > 0);
        Assert.True(
            comparison.Mean <= 0.053,
            $"Real-RAW mean ΔE00 {comparison.Mean:F4} exceeds 0.053.");
    }

    [Theory]
    [MemberData(nameof(SettingsCases))]
    public void IntersectionGamutSettings_StayWithinImplementationSanityBound(
        GoldenSettingsCase settingsCase)
    {
        using var baseImage = CreateIntersectionGamutBase();
        var settings = settingsCase.CreateSettings();
        settings.BaseLook = false;
        settings.Detail = new DetailSettings { CaptureSharpen = 0 };

        using var srgb = Render(baseImage, settings, OutputColorSpace.Srgb);
        using var p3 = Render(baseImage, settings, OutputColorSpace.DisplayP3);
        var mean = MeanDeltaE00(srgb.Image, p3.Image);
        _output.WriteLine(
            $"{settingsCase.Slug}: sRGB/P3 common-space mean ΔE00={mean:F4}");

        Assert.True(
            mean <= 5.0,
            $"{settingsCase.Slug}: common-space mean ΔE00 {mean:F4} exceeds 5.0.");
    }

    [Fact]
    public void RawDefaults_NeutralsAgreeColorimetricallyAcrossTargets()
    {
        const int size = 32;
        var samples = new ushort[size * size * 3];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var neutral = (ushort)Math.Round(
                (0.05 + 0.75 * x / (size - 1.0)) * ushort.MaxValue);
            var offset = (y * size + x) * 3;
            samples[offset] = neutral;
            samples[offset + 1] = neutral;
            samples[offset + 2] = neutral;
        }
        using var baseImage = RenderPipelineTestSupport.CreateBase(
            samples,
            isRaw: true,
            height: size);
        var settings = new EditSettings();

        using var srgb = Render(baseImage, settings, OutputColorSpace.Srgb);
        using var p3 = Render(baseImage, settings, OutputColorSpace.DisplayP3);
        var mean = MeanDeltaE00(srgb.Image, p3.Image);

        Assert.True(mean <= 0.02, $"Neutral mean ΔE00 was {mean:F4}.");
    }

    [Fact]
    public void ActualRawDefaultAndEditedDivergence_IsReportedWithoutQualityBound()
    {
        var asset = GoldenTestCases.Assets.Single(
            value => value.Slug == "canon-eos-350d");
        using var baseImage = new RawBaseLoader().LoadFullBase(
            new ImageFile(asset.FilePath),
            BaseDecodeSettings.Default,
            CancellationToken.None) ?? throw new InvalidOperationException(
                "RAW divergence fixture did not decode.");
        var cases = new[]
        {
            GoldenTestCases.Identity,
            asset.SettingsCases.Single(value => value.Slug == "full-combo-tonal")
        };
        foreach (var settingsCase in cases)
        {
            using var srgb = RenderAt500(
                baseImage,
                settingsCase.CreateSettings(),
                OutputColorSpace.Srgb);
            using var p3 = RenderAt500(
                baseImage,
                settingsCase.CreateSettings(),
                OutputColorSpace.DisplayP3);
            var mean = MeanDeltaE00(srgb.Image, p3.Image);
            _output.WriteLine(
                $"actual-raw-{settingsCase.Slug}: whole-image sRGB/P3 " +
                $"mean ΔE00={mean:F4} (reported, not quality-gated)");
            Assert.True(double.IsFinite(mean));
        }
    }

    private static BaseImage CreateIntersectionGamutBase()
    {
        double[][] codes =
        [
            [0.35, 0.30, 0.25],
            [0.25, 0.35, 0.30],
            [0.30, 0.25, 0.35],
            [0.40, 0.40, 0.40]
        ];
        var samples = codes
            .SelectMany(code =>
            {
                var linearSrgb = code.Select(DecodeSrgb).ToArray();
                var working = PrecisionColorCases.Transform(
                    RgbColorSpaceMatrices.LinearSrgbToLinearRec2020,
                    linearSrgb);
                return working.Select(value => (ushort)Math.Round(
                    Math.Clamp(value, 0, 1) * ushort.MaxValue));
            })
            .ToArray();
        return RenderPipelineTestSupport.CreateBase(samples);
    }

    private static BaseImage CreateAgreementBase()
    {
        const int width = 4096;
        double[][] codes =
        [
            [0.18, 0.24, 0.31],
            [0.29, 0.20, 0.16],
            [0.22, 0.34, 0.25],
            [0.42, 0.38, 0.31],
            [0.35, 0.27, 0.43],
            [0.52, 0.48, 0.40],
            [0.12, 0.15, 0.20],
            [0.62, 0.56, 0.48]
        ];
        var samples = new ushort[width * 3];
        for (var x = 0; x < width; x++)
        {
            var code = codes[x % codes.Length];
            var linearSrgb = code.Select(DecodeSrgb).ToArray();
            var working = PrecisionColorCases.Transform(
                RgbColorSpaceMatrices.LinearSrgbToLinearRec2020,
                linearSrgb);
            for (var channel = 0; channel < 3; channel++)
            {
                samples[x * 3 + channel] = (ushort)Math.Round(
                    Math.Clamp(working[channel], 0, 1) * ushort.MaxValue);
            }
        }
        return RenderPipelineTestSupport.CreateBase(samples);
    }

    private static EditSettings CreateAgreementSettings()
    {
        var curve = new CurveData();
        curve.AddPointAndReturnIndex(0.25, 0.22);
        curve.AddPointAndReturnIndex(0.75, 0.79);
        var settings = TestEditSettingsFactory.CreateTonal(
            exposure: 0.35,
            brightness: 4,
            contrast: 15,
            saturation: 12,
            vibrance: 8,
            shadows: 20,
            highlights: -30,
            curve: curve);
        settings.Detail = new DetailSettings
        {
            CaptureSharpen = 40,
            ChromaNr = 40
        };
        return settings;
    }

    private static MagickImage RenderShared(
        BaseImage baseImage,
        EditSettings settings) =>
        new RenderPipeline().RenderDisplayRec2020(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Export,
            null,
            new RenderOptions(false, false)));

    private static MagickImage Finalize(
        MagickImage shared,
        OutputColorSpace outputColorSpace,
        bool outputSharpening) =>
        RenderFinalizer.Finalize(
            shared,
            2048,
            outputColorSpace,
            outputSharpening
                ? OutputSharpeningMode.Screen
                : OutputSharpeningMode.Off,
            wasResized: false);

    private static MagickImage PrepareForEligibility(
        MagickImage shared,
        bool outputSharpening)
    {
        var prepared = new MagickImage(shared);
        RenderColorEncoding.ResizeInLinearLight(prepared, 2048);
        RenderSharpening.ApplyOutput(
            prepared,
            outputSharpening
                ? OutputSharpeningMode.Screen
                : OutputSharpeningMode.Off,
            wasResized: true);
        return prepared;
    }

    private static RenderResult Render(
        BaseImage baseImage,
        EditSettings settings,
        OutputColorSpace outputColorSpace) =>
        new RenderPipeline().Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Export,
            null,
            new RenderOptions(false, false),
            outputColorSpace));

    private static RenderResult RenderAt500(
        BaseImage baseImage,
        EditSettings settings,
        OutputColorSpace outputColorSpace) =>
        new RenderPipeline().Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Export,
            500,
            new RenderOptions(false, false),
            outputColorSpace));

    private static double MeanDeltaE00(MagickImage srgb, MagickImage p3)
    {
        var srgbPixels = RenderPipelineTestSupport.ReadPixels(srgb);
        var p3Pixels = RenderPipelineTestSupport.ReadPixels(p3);
        Assert.Equal(srgbPixels.Length, p3Pixels.Length);
        var sum = 0.0;
        for (var index = 0; index < srgbPixels.Length; index += 3)
        {
            sum += PrecisionDeltaE.Ciede2000(
                ToLab(srgbPixels, index,
                    RgbColorSpaceMatrices.LinearSrgbToXyzD65DerivedExact),
                ToLab(p3Pixels, index,
                    RgbColorSpaceMatrices.LinearDisplayP3ToXyzD65DerivedExact));
        }
        return sum / (srgbPixels.Length / 3);
    }

    private static (double Mean, int Count) MeanDeltaE00(
        MagickImage srgb,
        MagickImage p3,
        MagickImage prepared)
    {
        var srgbPixels = RenderPipelineTestSupport.ReadPixels(srgb);
        var p3Pixels = RenderPipelineTestSupport.ReadPixels(p3);
        var sharedPixels = RenderPipelineTestSupport.ReadPixels(prepared);
        double sum = 0;
        var count = 0;
        for (var index = 0; index < sharedPixels.Length; index += 3)
        {
            if (!IsSrgbInGamut(sharedPixels, index))
            {
                continue;
            }
            sum += PrecisionDeltaE.Ciede2000(
                ToLab(srgbPixels, index,
                    RgbColorSpaceMatrices.LinearSrgbToXyzD65DerivedExact),
                ToLab(p3Pixels, index,
                    RgbColorSpaceMatrices.LinearDisplayP3ToXyzD65DerivedExact));
            count++;
        }
        return (sum / count, count);
    }

    private static double MeanDeltaE00EightBit(
        MagickImage srgb,
        MagickImage p3,
        MagickImage prepared)
    {
        var srgbPixels = srgb.GetPixelsUnsafe().ToByteArray(PixelMapping.RGB) ?? [];
        var p3Pixels = p3.GetPixelsUnsafe().ToByteArray(PixelMapping.RGB) ?? [];
        var sharedPixels = RenderPipelineTestSupport.ReadPixels(prepared);
        double sum = 0;
        var count = 0;
        for (var index = 0; index < sharedPixels.Length; index += 3)
        {
            if (!IsSrgbInGamut(sharedPixels, index))
            {
                continue;
            }
            sum += PrecisionDeltaE.Ciede2000(
                ToLab(srgbPixels, index,
                    RgbColorSpaceMatrices.LinearSrgbToXyzD65DerivedExact),
                ToLab(p3Pixels, index,
                    RgbColorSpaceMatrices.LinearDisplayP3ToXyzD65DerivedExact));
            count++;
        }
        return sum / count;
    }

    private static bool IsSrgbInGamut(ushort[] pixels, int offset)
    {
        var rec2020 = new[]
        {
            DecodeSrgb(pixels[offset] / (double)ushort.MaxValue),
            DecodeSrgb(pixels[offset + 1] / (double)ushort.MaxValue),
            DecodeSrgb(pixels[offset + 2] / (double)ushort.MaxValue)
        };
        var srgb = PrecisionColorCases.Transform(
            RgbColorSpaceMatrices.LinearRec2020ToLinearSrgb,
            rec2020);
        return srgb.All(value => value is >= 0 and <= 1);
    }

    private static PrecisionLab ToLab(
        ushort[] pixels,
        int offset,
        double[,] rgbToXyz)
    {
        var rgb = new[]
        {
            DecodeSrgb(pixels[offset] / (double)ushort.MaxValue),
            DecodeSrgb(pixels[offset + 1] / (double)ushort.MaxValue),
            DecodeSrgb(pixels[offset + 2] / (double)ushort.MaxValue)
        };
        var xyz = PrecisionColorCases.Transform(rgbToXyz, rgb);
        var fx = Pivot(xyz[0] / 0.9504559270516716);
        var fy = Pivot(xyz[1]);
        var fz = Pivot(xyz[2] / 1.0890577507598784);
        return new PrecisionLab(
            116 * fy - 16,
            500 * (fx - fy),
            200 * (fy - fz));
    }

    private static PrecisionLab ToLab(
        byte[] pixels,
        int offset,
        double[,] rgbToXyz)
    {
        var rgb = new[]
        {
            DecodeSrgb(pixels[offset] / (double)byte.MaxValue),
            DecodeSrgb(pixels[offset + 1] / (double)byte.MaxValue),
            DecodeSrgb(pixels[offset + 2] / (double)byte.MaxValue)
        };
        var xyz = PrecisionColorCases.Transform(rgbToXyz, rgb);
        var fx = Pivot(xyz[0] / 0.9504559270516716);
        var fy = Pivot(xyz[1]);
        var fz = Pivot(xyz[2] / 1.0890577507598784);
        return new PrecisionLab(
            116 * fy - 16,
            500 * (fx - fy),
            200 * (fy - fz));
    }

    private static double DecodeSrgb(double value) => value <= 0.04045
        ? value / 12.92
        : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static double Pivot(double value) => value > 216.0 / 24389
        ? Math.Cbrt(value)
        : 841.0 / 108 * value + 4.0 / 29;
}
