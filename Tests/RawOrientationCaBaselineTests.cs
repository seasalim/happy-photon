using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;
using static HappyPhoton.Tests.RawOrientationMeasureSupport;

namespace HappyPhoton.Tests;

public sealed partial class RawOrientationOpticsBaselineTests
{
    [Fact]
    public void O4StrongCaFrozenOracle()
    {
        Require();
        using var directory = new TemporaryDirectory();
        var oracle = Path.Combine(Root, "oracle");
        const string correction = "ca-strong";
        var fixture = Path.Combine(oracle, correction + ".dng");
        var savedPixels = Path.Combine(oracle, correction + ".png");
        var settingsPath = Path.Combine(oracle, correction + "-settings.json");
        var decode = Off with { ChromaticAberration = true };
        var options = new SyntheticRawDngOptions
        {
            UseInsetDefaultCrop = false,
            IncludeChromaticAberration = true,
            ChromaticAberrationKr1Delta = 0.08,
            WarpKr1 = 0,
            VignetteK0 = 0,
            WarpCenterX = 0.31,
            WarpCenterY = 0.67
        };

        if (Freeze)
        {
            Assert.False(File.Exists(fixture), "An existing frozen oracle must never be overwritten.");
            Assert.False(File.Exists(savedPixels));
            Assert.False(File.Exists(settingsPath));
            File.Copy(SyntheticRawDngFactory.Write(directory.Path, options), fixture);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(new
            {
                decode,
                renderSettings = EditSettingsJson.Serialize(new EditSettings()),
                output = "full preview base, no render resize, lossless 8-bit sRGB PNG",
                sensorWidth = options.Width, sensorHeight = options.Height,
                options.Make, options.Model, options.Scale, options.Orientation,
                options.IncludeOpcodes, options.IncludeChromaticAberration,
                options.ChromaticAberrationKr1Delta, options.WarpKr1, options.VignetteK0,
                options.WarpKt0, options.WarpKt1, options.WarpCenterX, options.WarpCenterY,
                activeArea = options.ActiveArea,
                defaultCropOrigin = new[] { 0, 0 }, defaultCropSize = new[] { 608, 448 },
                mosaic = "RGGB; every site round(512 + ((x + 0.5) / 640) * 2600); white=4095; black=0",
                warpPlanes = new[]
                {
                    new[] { 1d, -0.08, 0, 0, 0, 0 },
                    new[] { 1d, 0, 0, 0, 0, 0 },
                    new[] { 1d, 0.08, 0, 0, 0, 0 }
                }
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        using var basis = Load(fixture, true, decode);
        Assert.NotNull(basis.Info.LensPrescriptionSummary);
        var prescription = new DngLensPrescriptionReader().Read(fixture).Prescription;
        Assert.NotNull(prescription);
        using var corrected = Render(basis, RenderIntent.Export);
        corrected.Image.Depth = 8;
        using var plainBasis = Load(fixture, true, Off);
        using var plain = Render(plainBasis, RenderIntent.Export);
        plain.Image.Resize(new MagickGeometry(corrected.Image.Width, corrected.Image.Height)
            { IgnoreAspectRatio = true });
        var effect = Difference(corrected.Image, plain.Image, true);
        Assert.True(effect.Max >= 8, $"CA max effect {effect.Max} is below 8 codes.");
        Assert.True(effect.Mean >= 0.5, $"CA mean effect {effect.Mean} is below 0.5 codes.");
        var asymmetry = EffectAsymmetry(corrected.Image, plain.Image);
        Assert.True(asymmetry.Max >= 8, "CA effect must discriminate a 180-degree rotation.");

        if (Freeze)
        {
            corrected.Image.Write(savedPixels, MagickFormat.Png);
        }

        using var reference = new MagickImage(savedPixels);
        var identityError = Difference(reference, corrected.Image, true).Max;
        Assert.True(identityError <= 1);
        var rows = new List<object>
        {
            new { correction, exif = 1, width = reference.Width, height = reference.Height,
                oracleMax8 = identityError, effectMax8 = effect.Max, effectMean8 = effect.Mean,
                effectRotation180Max8 = asymmetry.Max, effectRotation180Mean8 = asymmetry.Mean }
        };

        foreach (var exif in new ushort[] { 3, 6, 8 })
        {
            var path = RawOrientationOpticsBaselineTests.CopyOrientation(fixture, directory.Path, exif);
            using var targetBasis = Load(path, true, decode);
            using var target = Render(targetBasis, RenderIntent.Export);
            using var expected = Transform(reference, exif);
            var dimensionsMatch = (expected.Width, expected.Height) == (target.Image.Width, target.Image.Height);
            rows.Add(new { correction, exif, width = target.Image.Width, height = target.Image.Height,
                expectedWidth = expected.Width, expectedHeight = expected.Height, dimensionsMatch,
                oracleMax8 = dimensionsMatch ? Difference(expected, target.Image, true).Max : (int?)null });
        }

        Record("O4-ca-strong", rows, output);
    }

    private static (int Max, double Mean) EffectAsymmetry(MagickImage corrected, MagickImage plain)
    {
        using var correctedPixels = corrected.GetPixelsUnsafe();
        using var plainPixels = plain.GetPixelsUnsafe();
        var a = correctedPixels.ToShortArray(PixelMapping.RGB)!;
        var b = plainPixels.ToShortArray(PixelMapping.RGB)!;
        var maximum = 0;
        long total = 0;

        for (var i = 0; i < a.Length; i++)
        {
            var opposite = (a.Length / 3 - 1 - i / 3) * 3 + i % 3;
            var effect = Math.Abs((int)Math.Round(a[i] / 257d) - (int)Math.Round(b[i] / 257d));
            var rotatedEffect = Math.Abs((int)Math.Round(a[opposite] / 257d)
                - (int)Math.Round(b[opposite] / 257d));
            var delta = Math.Abs(effect - rotatedEffect);
            maximum = Math.Max(maximum, delta);
            total += delta;
        }

        return (maximum, total / (double)a.Length);
    }
}
