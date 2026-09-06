using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LocalsLinearParitySentinelTests(ITestOutputHelper output)
{
    // G6 frozen at 1191d2d1e2186c33823596bc3eb02cb39cf7ad40, render v14.
    // Do not regenerate for the additive radial slice. Q16 RGB is hashed little-endian.
    private const string CanonicalJson = """{"version":4,"exposure":0.45,"wb":{"mode":"asShot","kelvin":null,"tint":null,"gains":null,"preset":null},"highlights":-31,"shadows":24,"brightness":12,"contrast":28,"saturation":19,"vibrance":16,"baseLook":null,"hlReconstruction":"clip","detail":{"captureSharpen":80,"luminanceNr":55,"chromaNr":65},"effects":{"vignette":-37,"midpoint":61,"grain":42,"grainSize":"coarse"},"lens":{"distortion":true,"chromaticAberration":true,"vignetting":false},"rotation":0,"horizon_rotation":0,"crop":{"left":0.1,"top":0.1,"right":0.9,"bottom":0.9},"curve":{"points":[{"x":0,"y":0},{"x":1,"y":1}]},"applied_preset_id":null,"locals":[{"id":"11111111111111111111111111111111","type":"linear","ordinal":1,"enabled":true,"cu":0.42,"cv":0.46,"angle":32,"feather":0.35,"exposure":1.5},{"id":"22222222222222222222222222222222","type":"linear","ordinal":2,"enabled":true,"cu":0.58,"cv":0.54,"angle":137,"feather":0.55,"exposure":-1}]}""";
    private const string SettingsHash = "9925ec66622900962369abbaff1fc33f9d0dfad3d87604cf5dc6ba0b60b2f7b6";
    private const string RawInteractive = "E263BABC98787437F4404E34FD11A67A781B5072D233425FD1F2717231773B27";
    private const string RawResting = "1B9D7022375137C0E965803461FD5D560E8E6869372231466EE3C650E0E1B072";
    private const string StandardInteractive = "E650EE39F5CF1134BB2D13EC974AF08974C2E8210461CD263807728784741774";
    private const string StandardResting = "B507E029503E88BE8006C08C2AD89503C591F85B78A3AE7FF8C20548568960CE";

    [Fact]
    public void LinearDocumentStaysByteIdentical()
    {
        var settings = Settings();
        Assert.Equal(14, RenderPipeline.Version);
        Check("json", CanonicalJson, EditSettingsJson.Serialize(settings));
        Check("settings", SettingsHash, RenderSettingsHash.Compute(settings));
    }

    [Theory]
    [InlineData("raw", false)]
    [InlineData("raw", true)]
    [InlineData("standard", false)]
    [InlineData("standard", true)]
    public void ActiveLinearPixelsStayByteIdentical(string kind, bool resting)
    {
        // Reuse the exact golden generator without changing the existing golden file.
        var factory = typeof(RenderSequenceGoldenTests).GetMethod("CreateBase",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        using var source = (BaseImage)factory.Invoke(null, [kind, 2200, 1650])!;
        var settings = Settings();
        var pipeline = new RenderPipeline();
        string actual;
        if (resting)
        {
            // Match PreviewService.Resting: snapshot frame, apply geometry, resize,
            // clear prepared geometry, then carry the original frame into RenderResting.
            var frame = RenderGeometry.CalculateLocalsFrame(2200, 1650, settings);
            var pixels = RenderGeometry.Apply(source.Pixels, settings, out _);
            using var prepared = new BaseImage(pixels, source.Info);
            BitmapConversionService.ResizeToMaxDimension(pixels, 1600);
            var preparedSettings = settings.Clone();
            preparedSettings.Rotation = 0;
            preparedSettings.HorizonRotation = 0;
            preparedSettings.Crop = null;
            preparedSettings.Geometry = null;
            using var result = pipeline.RenderResting(new RenderRequest(prepared,
                preparedSettings, RenderIntent.Preview, 1600, new RenderOptions(false, false))
                { LocalsFrameOverride = frame },
                RenderExecutionOptions.Resting(CancellationToken.None));
            actual = Hash(RenderPipelineTestSupport.ReadPixels(result.Image));
            Assert.Equal(1600u, result.Image.Width);
            Assert.Equal(1200u, result.Image.Height);
        }
        else
        {
            using var result = pipeline.Render(new RenderRequest(source, settings,
                RenderIntent.Preview, 1600, new RenderOptions(false, false)));
            actual = Hash(RenderPipelineTestSupport.ReadPixels(result.Image));
            Assert.Equal(1600u, result.Image.Width);
            Assert.Equal(1200u, result.Image.Height);
        }
        var expected = (kind, resting) switch
        {
            ("raw", false) => RawInteractive,
            ("raw", true) => RawResting,
            ("standard", false) => StandardInteractive,
            _ => StandardResting
        };
        Check($"{kind}-{(resting ? "resting" : "interactive")}", expected, actual);
    }

    private void Check(string name, string expected, string actual)
    {
        output.WriteLine($"SENTINEL {name} {actual}");
        Assert.Equal(expected, actual);
    }

    private static EditSettings Settings() => new()
    {
        Exposure = 0.45, Brightness = 12, Contrast = 28, Highlights = -31,
        Shadows = 24, Saturation = 19, Vibrance = 16,
        Detail = new DetailSettings { CaptureSharpen = 80, LuminanceNr = 55, ChromaNr = 65 },
        Effects = new EffectsSettings
        {
            Vignette = -37, Midpoint = 61, Grain = 42, GrainSize = GrainSize.Coarse
        },
        Crop = new() { Left = .1, Top = .1, Right = .9, Bottom = .9 },
        Locals =
        [
            new() { Id = "11111111111111111111111111111111", Type = "linear", Ordinal = 1,
                Enabled = true, Cu = .42, Cv = .46, Angle = 32, Feather = .35, Exposure = 1.5 },
            new() { Id = "22222222222222222222222222222222", Type = "linear", Ordinal = 2,
                Enabled = true, Cu = .58, Cv = .54, Angle = 137, Feather = .55, Exposure = -1 }
        ]
    };

    private static string Hash(ushort[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2, 2), samples[i]);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}

