using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class RenderDedupHashControlTests
{
    private const int TransferSamples = 4097;

    [Fact]
    public void ToneLutApplicator_MatchesFrozenHash()
    {
        var lut = new double[ToneLut.Length];

        for (var index = 0; index < lut.Length; index++)
        {
            var value = index / (double)ushort.MaxValue;
            lut[index] = Math.Pow(value, 0.82) * 0.97 + 0.012 * Math.Sin(value * 9.1);
        }

        using var image = CreateImage(Width, Height, 0x9B05688C);
        ToneLutApplicator.Apply(image, lut);
        var hash = ImageHash(image);
        AssertFrozen("tonelut-single", hash);

        foreach (var cap in Caps())
        {
            using var resting = CreateImage(Width, Height, 0x9B05688C);
            ToneLutApplicator.Apply(resting, lut, Resting(cap));
            Assert.Equal(hash, ImageHash(resting));
        }

        using var matrixImage = CreateImage(Width, Height, 0x9B05688C);
        ToneLutApplicator.Apply(matrixImage, ProfileMatrix, AgxLuts());
        AssertFrozen("tonelut-matrix-agx", ImageHash(matrixImage));
    }

    [Fact]
    public void WorkingSpaceConversion_MatchesFrozenHash()
    {
        // 1024 x 768 spans three 262,144-pixel workers; the samples reach exact 0 and 65535.
        using var image = CreateImage(1024, 768, 0x1F83D9AB);

        WorkingSpaceColorConversion.ConvertSrgbToLinearRec2020(image);

        AssertFrozen("working-space-srgb-to-rec2020", ImageHash(image));
    }

    [Fact]
    public void AgxComposeCached_MatchesFrozenHash()
    {
        var luts = AgxLuts();

        using var hash = new HashBuilder();
        hash.AddDoubles(luts.Red);
        hash.AddDoubles(luts.Green);
        hash.AddDoubles(luts.Blue);
        AssertFrozen("agx-compose-cached", hash.Finish());
    }

    [Fact]
    public void OklabTransformQ16_MatchesFrozenHash()
    {
        var mixer = ColorMixerParameters.From(new ColorMixerSettings
        {
            Red = new ColorMixerBandSettings { Hue = 12, Saturation = -20, Luminance = 8 },
            Aqua = new ColorMixerBandSettings { Hue = -25, Saturation = 35, Luminance = -10 },
            Purple = new ColorMixerBandSettings { Hue = 30, Saturation = 15, Luminance = 20 }
        });
        using var plain = new HashBuilder();
        using var mixed = new HashBuilder();
        using var encoded = new HashBuilder();
        var state = 0x5BE0CD19u;

        for (var index = 0; index < 60_000; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            var red = (ushort)state;
            var green = (ushort)(state >> 16);
            var blue = (ushort)(state * 2654435761u >> 16);
            AddQ16(plain, OklabColor.TransformQ16(red, green, blue, 30, 20));
            AddQ16(mixed, OklabColor.TransformQ16(red, green, blue, -15, 40, in mixer));
            var rgb = OklabColor.TransformEncodedRec2020(
                new OklabRgb(red / 65535.0, green / 65535.0, blue / 65535.0), 30, 20, in mixer);
            encoded.AddDouble(rgb.Red);
            encoded.AddDouble(rgb.Green);
            encoded.AddDouble(rgb.Blue);
        }

        AssertFrozen("oklab-q16", plain.Finish());
        AssertFrozen("oklab-q16-mixer", mixed.Finish());
        AssertFrozen("oklab-encoded-mixer", encoded.Finish());
    }

    [Fact]
    public void IccParsedProfiles_MatchFrozenHash()
    {
        foreach (var (name, bytes) in SoftProofIccFixtureWriter.CreateProfiles().OrderBy(pair => pair.Key))
        {
            var profile = IccDisplayProfileParser.Parse(bytes, name);
            using var hash = new HashBuilder();
            hash.AddInt32((int)profile.Kind);
            var m = profile.Colorants;
            hash.AddDoubles([m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33]);
            AddCurve(hash, profile.RedCurve);
            AddCurve(hash, profile.GreenCurve);
            AddCurve(hash, profile.BlueCurve);
            AssertFrozen($"icc-{Path.GetFileNameWithoutExtension(name)}", hash.Finish());
        }
    }

    [Fact]
    public void SrgbTransferCopies_MatchFrozenHash()
    {
        using var icc = new HashBuilder();
        AddCurve(icc, ToneCurve.Srgb);
        AddCurve(icc, ToneCurve.Parametric(3, [2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045]));
        AddCurve(icc, ToneCurve.Parametric(4, [2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045, 0.001, 0.002]));
        AddCurve(icc, ToneCurve.Gamma(2.2));
        AddCurve(icc, ToneCurve.Sampled(Enumerable.Range(0, 1024)
            .Select(index => Math.Pow(index / 1023.0, 2.2)).ToArray()));
        AssertFrozen("icc-tonecurves", icc.Finish());

        using var toneLut = new HashBuilder();

        for (var index = 0; index <= ushort.MaxValue; index++)
        {
            var value = index / (double)ushort.MaxValue;
            toneLut.AddDouble(ToneLut.SrgbEncode(value));
            toneLut.AddDouble(ToneLut.SrgbDecode(value));
        }

        AssertFrozen("tonelut-srgb-transfer", toneLut.Finish());
    }

    private static ToneLuts AgxLuts() => AgxToneLut.ComposeCached(
        AgxToneEnginePropertyTests.Parameters(
            contrast: 35,
            highlights: -40,
            shadows: 25,
            exposureEv: 0.6,
            sourceExposureEv: -0.25),
        1.2);

    private static void AddQ16(HashBuilder hash, OklabQ16 value) =>
        hash.AddUInt16s([value.Red, value.Green, value.Blue]);

    private static void AddCurve(HashBuilder hash, ToneCurve curve)
    {
        for (var index = 0; index < TransferSamples; index++)
            hash.AddDouble(curve.Evaluate(index / (double)(TransferSamples - 1)));
    }
}
