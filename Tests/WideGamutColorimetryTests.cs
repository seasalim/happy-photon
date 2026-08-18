using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

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

    private static double DecodeSrgb(double value) => value <= 0.04045
        ? value / 12.92
        : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static double Pivot(double value) => value > 216.0 / 24389
        ? Math.Cbrt(value)
        : 841.0 / 108 * value + 4.0 / 29;
}
