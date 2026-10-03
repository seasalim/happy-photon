using System.Security.Cryptography;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

internal static class SyncPhotoGateSupport
{
    internal const string Raw = "canon-eos-6d-iso-6400.cr2";

    internal const string Heic = "iphone-14-pro-iso-1000.heic";

    internal static void RequirePerformance()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in sync photo baseline");
        PerfEnvironment.AssertFullCpu();
#if DEBUG
        Assert.Fail("Use Release");
#endif
    }

    internal static string LocalFixture(string name)
    {
        var path = GoldenTestPaths.Asset(name);
        GoldenTestPaths.RequireReadableFixture(path);
        var expected = name switch
        {
            Raw => "7727EE0280B44EA1D633962F49942F37F3C7EC6D704D22E108A5223666327C32",
            Heic => "E4BE19FBA9F585B74AC633AF0E545BCB5A331DB7EC3D0D8DEA81D8C538CB1E02",
            "reference.heic" => "297AFE8C8415871966591D671E7F181A6E73A31C1DCD65DCB657D997981FF166",
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        using var stream = File.OpenRead(path);
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(stream)));

        return path;
    }

    internal static EditSettings PhotoA()
    {
        var settings = HealWorkloads.LH8();
        settings.Crop = new CropRegion { Left = .13, Top = .17, Right = .89, Bottom = .93 };
        settings.HorizonRotation = 3.25;
        settings.Geometry = new GeometrySettings { Vertical = 17, Horizontal = -23, Aspect = 11, Distortion = -9 };

        return settings;
    }

    internal static EditSettings ResetPhotoGroups(EditSettings source)
    {
        var reset = source.Clone();
        reset.Crop = null;
        reset.HorizonRotation = 0;
        reset.Geometry = null;
        reset.Locals = null;
        Assert.Null(reset.Crop);
        Assert.Equal(0, reset.HorizonRotation);
        Assert.Null(reset.Geometry);
        Assert.Null(reset.Locals);
        Assert.NotNull(source.Crop);
        Assert.NotNull(source.Geometry);
        Assert.Equal(8, source.Locals!.Count);
        Assert.Equal(EditSettingsJson.Serialize(new EditSettings()), EditSettingsJson.Serialize(reset));

        return reset;
    }

    // Full Q16 RGB arrays in the same finalized sRGB target, before any encoder.
    internal static long DifferingCodes(MagickImage expected, MagickImage actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        var a = RenderPipelineTestSupport.ReadPixels(expected);
        var b = RenderPipelineTestSupport.ReadPixels(actual);
        Assert.Equal(a.Length, b.Length);
        long differing = 0;

        for (var index = 0; index < a.Length; index++)
        {
            if (a[index] != b[index]) differing++;
        }

        return differing;
    }
}
