using System.Security.Cryptography;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

internal static class RawOrientationMeasureSupport
{
    internal static readonly BaseDecodeSettings Off = new(
        HlReconstructionMode.Clip, Distortion: false, ChromaticAberration: false, Vignetting: false);

    internal static string Root => Path.Combine(GoldenTestPaths.RepositoryRoot,
        "artifacts", "318-raw-orientation");

    internal static bool Freeze => Environment.GetEnvironmentVariable("HAPPY_PHOTON_ORIENTATION_FREEZE") == "1";

    internal static void Require()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_ORIENTATION_MEASURE") != "1",
            "Opt-in orientation baseline");
        Directory.CreateDirectory(Root);
    }

    internal static void Record(string gate, object value, ITestOutputHelper output)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        var run = Environment.GetEnvironmentVariable("HAPPY_PHOTON_ORIENTATION_RUN") ?? "manual";
        File.WriteAllText(Path.Combine(Root, run + "-" + gate + ".json"), json);
        output.WriteLine(gate + " " + json);
    }

    internal static BaseImage Load(string path, bool preview, BaseDecodeSettings? decode = null)
    {
        SyncProfileGateSupport.RequireLocal(path);
        var loader = new GatedBaseImageLoader(new RawBaseLoader(), new SourceAvailabilityService());
        var file = new ImageFile(path);
        var result = preview ? loader.LoadPreviewBase(file, decode ?? Off, CancellationToken.None)
            : loader.LoadFullBase(file, decode ?? Off, CancellationToken.None);
        Assert.NotNull(result);

        return result;
    }

    // Independent EXIF permutation; deliberately does not call production orientation helpers.
    internal static MagickImage Transform(MagickImage source, int orientation)
    {
        var result = (MagickImage)source.Clone();

        switch (orientation)
        {
            case 2: result.Flop(); break;

            case 3: result.Rotate(180); break;

            case 4: result.Flip(); break;

            case 5: result.Transpose(); break;

            case 6: result.Rotate(90); break;

            case 7: result.Transverse(); break;

            case 8: result.Rotate(270); break;
        }

        return result;
    }

    internal static (int Max, double Mean) Difference(MagickImage a, MagickImage b, bool eightBit = false)
    {
        Assert.Equal((a.Width, a.Height), (b.Width, b.Height));
        using var pa = a.GetPixelsUnsafe();
        using var pb = b.GetPixelsUnsafe();
        var av = pa.ToShortArray(PixelMapping.RGB)!;
        var bv = pb.ToShortArray(PixelMapping.RGB)!;
        var maximum = 0;
        long total = 0;

        for (var i = 0; i < av.Length; i++)
        {
            var delta = eightBit
                ? Math.Abs((int)Math.Round(av[i] / 257d) - (int)Math.Round(bv[i] / 257d))
                : Math.Abs(av[i] - bv[i]);
            maximum = Math.Max(maximum, delta);
            total += delta;
        }

        return (maximum, total / (double)av.Length);
    }

    internal static RenderResult Render(BaseImage basis, RenderIntent intent, EditSettings? settings = null) =>
        new RenderPipeline().Render(new RenderRequest(basis, settings ?? new EditSettings(),
            intent, null, new RenderOptions(false)));

    internal static string Hash(string path)
    {
        using var stream = File.OpenRead(path);

        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
