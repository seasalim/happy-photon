using System.Buffers.Binary;
using System.Security.Cryptography;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderSequenceGoldenTests(ITestOutputHelper output)
{
    // Frozen at 73d631c. RGB Q16 samples hashed in explicit little-endian order.
    private static readonly Dictionary<string, string> Goldens = new()
    {
        ["raw-Preview-native"] = "01156FF64E64F29B667F2CFAEDBD60DF839331C47E72A599A8A8BB1DE29CF18A",
        ["monochrome-Preview-downsize"] = "2A654CBFF850D2ED543B921505BFF228C976C228799A9CFCB992B1E4EC1DF968",
        ["standard-Export-downsize"] = "EF023350DD0AAFA2AA6FF082F551DC3B4C6F5DAFEDEDC3D1B2D43A91D164AAE6",
        ["standard-Preview-downsize"] = "F2248EB2A47F778BD1002C4B2700AFA88D255E291F05FAD781AB99A353B0E832",
        ["monochrome-Export-native"] = "ECB4E401E02B3948CEB694F05318EBE6C289721A2E61983639590A2CE21A4963",
        ["monochrome-Export-downsize"] = "C38AB1FF5F5CA1909C4F78B21F42F9A6A0EA0C9B4F4D4F0568B2C92B8F202209",
        ["raw-Preview-downsize"] = "79A7F127DFE211FEBF061F5AA6F2A83F056035C3DBCB8C607E268C076CDB4311",
        ["raw-Export-native"] = "1CD938D171057DD7EDCAC26F039FD694CF1FF689206D1E1EDD2026DC2E5F5B34",
        ["monochrome-Preview-native"] = "E82C4A4CDF53F23A77F1AB06DE26FDB8C815ED96F0526E0AAE2D9C76EE65650D",
        ["standard-Export-native"] = "6CE7ED00D32136FD26314914F88AC94EDE6466B136DC74145F2DE7278814CDAE",
        ["standard-Preview-native"] = "790D233DD1017C02E9215B1E4316CDAF4EDC887FB250E9105F5ACF9239D69962",
        ["raw-Export-downsize"] = "B38CDE8DBD1BE3DEAA7BC41A039DDA565208A60F3262B890C0D326DF363251C0",
    };

    public static IEnumerable<object[]> Cases()
    {
        foreach (var kind in new[] { "raw", "standard", "monochrome" })
        foreach (var intent in new[] { RenderIntent.Preview, RenderIntent.Export })
        foreach (var resize in new[] { false, true })
            yield return [kind, intent, resize];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void PixelsAndStagesMatchFrozenBase(string kind, RenderIntent intent, bool resize)
    {
        var width = resize ? 512 : 400;
        var height = resize ? 384 : 300;
        using var source = CreateBase(kind, width, height);
        var request = new RenderRequest(source, Settings(), intent,
            resize ? 400 : null, new RenderOptions(false, false));
        var pipeline = new RenderPipeline();
        using var reference = pipeline.Render(request);
        var hash = Hash(RenderPipelineTestSupport.ReadPixels(reference.Image));
        var key = $"{kind}-{intent}-{(resize ? "downsize" : "native")}";
        output.WriteLine($"GOLDEN {key} {hash}");
        Assert.Equal(400u, reference.Image.Width);
        Assert.Equal(300u, reference.Image.Height);
        Assert.Equal(PlatformRenderGoldens.Expected("sequence", key, Goldens[key]), hash);
        foreach (var cap in new[] { 1, 2, Environment.ProcessorCount })
        {
            var stages = new List<string>();
            using var resting = pipeline.RenderResting(request,
                RenderExecutionOptions.Resting(CancellationToken.None, cap, stages.Add));
            Assert.Equal(reference.Image.Width, resting.Image.Width);
            Assert.Equal(reference.Image.Height, resting.Image.Height);
            Assert.Equal(hash, Hash(RenderPipelineTestSupport.ReadPixels(resting.Image)));
            Assert.Equal(ExpectedStages(kind), stages);
            // No crossing-worker seam exists: record the production partition formula.
            // Geometry is neutral, so crossing sees exactly width * height pixels.
            var workers = kind == "standard" ? 0 : Math.Min(cap,
                Math.Min(Environment.ProcessorCount, Math.Max(1, (width * height + 32767) / 32768)));
            output.WriteLine($"EXPECTED-WORKERS {key} cpu={Environment.ProcessorCount} cap={cap} crossing={workers} (formula, not observed; the cap is observed by RenderSequenceContentionTests)");
            output.WriteLine($"STAGES {key} {string.Join(",", stages)}");
        }
    }

    private static string[] ExpectedStages(string kind) => kind switch
    {
        "raw" => ["geometry", "raw-crossing", "color-encoding", "chroma",
            "noise-reduction", "capture-sharpen", "finalization", "effects"],
        "standard" => ["geometry", "standard-tone", "color-encoding", "chroma",
            "noise-reduction", "capture-sharpen", "finalization", "effects"],
        _ => ["geometry", "raw-crossing", "color-encoding",
            "noise-reduction", "capture-sharpen", "finalization", "effects"]
    };

    private static BaseImage CreateBase(string kind, int width, int height)
    {
        var samples = new ushort[width * height * 3];
        uint state = 0x9E3779B9;
        for (var pixel = 0; pixel < width * height; pixel++)
        for (var channel = 0; channel < 3; channel++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            samples[pixel * 3 + channel] = kind == "monochrome" && channel > 0
                ? samples[pixel * 3] : (ushort)(512 + state % 63488);
        }
        var map = kind == "raw" ? new DcpHueSatMap(6, 3, 2, true,
            DcpProfileReaderTests.CreateTable(6, 3, 2, 8, 1.1f, 0.92f), null, 0) : null;
        return RenderPipelineTestSupport.CreateBase(samples, kind != "standard", height,
            hueSatMap: map, isMonochrome: kind == "monochrome");
    }

    private static EditSettings Settings() => new()
    {
        Exposure = 0.45, Brightness = 12, Contrast = 28, Highlights = -31,
        Shadows = 24, Saturation = 19, Vibrance = 16,
        Detail = new DetailSettings { CaptureSharpen = 80, LuminanceNr = 55, ChromaNr = 65 },
        Effects = new EffectsSettings
        {
            Vignette = -37, Midpoint = 61, Grain = 42, GrainSize = GrainSize.Coarse
        }
    };

    private static string Hash(ushort[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2, 2), samples[i]);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}

