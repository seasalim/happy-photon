using System.Buffers.Binary;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;
using static HappyPhoton.Tests.RawOrientationMeasureSupport;

namespace HappyPhoton.Tests;

public sealed partial class RawOrientationOpticsBaselineTests(ITestOutputHelper output)
{
    [Fact]
    public void O4FrozenOracle()
    {
        Require();
        using var directory = new TemporaryDirectory();
        var oracle = Path.Combine(Root, "oracle");
        Directory.CreateDirectory(oracle);
        var rows = new List<object>();
        var manifest = new List<object>();

        foreach (var correction in new[] { "distortion", "ca", "vignetting", "asymmetric" })
        {
            var options = new SyntheticRawDngOptions
            {
                UseInsetDefaultCrop = correction == "asymmetric",
                AsymmetricCrop = correction == "asymmetric",
                IncludeChromaticAberration = correction == "ca",
                WarpKr1 = correction == "vignetting" ? 0 : -0.08,
                VignetteK0 = correction == "vignetting" ? 0.2 : 0,
                WarpKt0 = correction == "asymmetric" ? 0.012 : 0,
                WarpKt1 = correction == "asymmetric" ? -0.009 : 0,
                WarpCenterX = correction == "asymmetric" ? 0.37 : 0.5,
                WarpCenterY = correction == "asymmetric" ? 0.61 : 0.5
            };
            var decode = Off with
            {
                Distortion = correction is "distortion" or "asymmetric",
                ChromaticAberration = correction == "ca",
                Vignetting = correction == "vignetting"
            };
            var fixture = Path.Combine(oracle, correction + ".dng");
            var savedPixels = Path.Combine(oracle, correction + ".png");
            var settingsPath = Path.Combine(oracle, correction + "-settings.json");

            if (Freeze)
            {
                Assert.False(File.Exists(fixture), "An existing frozen oracle must never be overwritten.");
                File.Copy(SyntheticRawDngFactory.Write(directory.Path, options), fixture);
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(new
                {
                    decode, output = "full preview base, no render resize, lossless 8-bit sRGB PNG",
                    sensorWidth = options.Width, sensorHeight = options.Height,
                    options.WarpKr1, options.VignetteK0, options.WarpKt0, options.WarpKt1,
                    options.WarpCenterX, options.WarpCenterY, options.IncludeChromaticAberration,
                    defaultCropOrigin = options.UseInsetDefaultCrop ? options.DefaultCropOrigin : [0u, 0u],
                    defaultCropSize = options.UseInsetDefaultCrop ? options.DefaultCropSize : [608u, 448u]
                }, new JsonSerializerOptions { WriteIndented = true }));
            }

            using var basis = Load(fixture, true, decode);
            Assert.NotNull(basis.Info.LensPrescriptionSummary);
            var prescription = new DngLensPrescriptionReader().Read(fixture).Prescription;
            Assert.NotNull(prescription);

            if (correction == "asymmetric")
            {
                Assert.NotEqual(0, prescription.Warps[0].Planes[0].Kt0);
                Assert.NotEqual(0, prescription.Warps[0].Planes[0].Kt1);
                Assert.NotEqual(1, prescription.OutputWindow.Left + prescription.OutputWindow.Right);
                Assert.NotEqual(1, prescription.OutputWindow.Top + prescription.OutputWindow.Bottom);
            }

            using var corrected = Render(basis, RenderIntent.Export);
            corrected.Image.Depth = 8;

            if (Freeze)
            {
                corrected.Image.Write(savedPixels, MagickFormat.Png);
            }

            using var reference = new MagickImage(savedPixels);
            using var plainBasis = Load(fixture, true, Off);
            using var plain = Render(plainBasis, RenderIntent.Export);
            // The asymmetric prescription also applies its authored DNG crop.
            // Pin the comparison grid to the corrected output dimensions.
            plain.Image.Resize(new MagickGeometry(reference.Width, reference.Height) { IgnoreAspectRatio = true });
            var effect = Difference(corrected.Image, plain.Image, true);
            Assert.True(effect.Max > 0);
            rows.Add(new { correction, exif = 1, width = reference.Width, height = reference.Height,
                oracleMax8 = Difference(reference, corrected.Image, true).Max,
                effectMax8 = effect.Max, effectMean8 = effect.Mean,
                renderSettings = EditSettingsJson.Serialize(new EditSettings()),
                sourceWindow = prescription.SourceWindow, outputWindow = prescription.OutputWindow });

            foreach (var exif in new ushort[] { 3, 6, 8 })
            {
                var path = CopyOrientation(fixture, directory.Path, exif);
                using var targetBasis = Load(path, true, decode);
                using var target = Render(targetBasis, RenderIntent.Export);
                using var expected = Transform(reference, exif);
                var dimensionsMatch = (expected.Width, expected.Height) == (target.Image.Width, target.Image.Height);
                rows.Add(new { correction, exif, width = target.Image.Width, height = target.Image.Height,
                    expectedWidth = expected.Width, expectedHeight = expected.Height, dimensionsMatch,
                    oracleMax8 = dimensionsMatch ? Difference(expected, target.Image, true).Max : (int?)null });
            }

            foreach (var path in new[] { fixture, savedPixels, settingsPath })
            {
                manifest.Add(new { file = Path.GetFileName(path), sha256 = Hash(path),
                    width = path.EndsWith(".dng") ? options.Width : (int)reference.Width,
                    height = path.EndsWith(".dng") ? options.Height : (int)reference.Height,
                    settings = Path.GetFileName(settingsPath),
                    effectMax8 = effect.Max, effectMean8 = effect.Mean,
                renderSettings = EditSettingsJson.Serialize(new EditSettings()),
                sourceWindow = prescription.SourceWindow, outputWindow = prescription.OutputWindow });
            }
        }

        if (Freeze)
        {
            File.WriteAllText(Path.Combine(oracle, "manifest.json"),
                JsonSerializer.Serialize(new { revision = "8f83f5fd57d97d7aad9987cef3492a5885370b88",
                    files = manifest }, new JsonSerializerOptions { WriteIndented = true }));
        }

        Record("O4", rows, output);
    }

    [Fact]
    public void O4bSaturation()
    {
        Require();
        using var directory = new TemporaryDirectory();
        var rows = new List<object>();

        foreach (var exif in new ushort[] { 1, 3, 6, 8 })
        {
            var path = SyntheticRawDngFactory.Write(directory.Path, new SyntheticRawDngOptions
            {
                Orientation = exif, WarpKr1 = -0.08, VignetteK0 = 0, SaturatedRedSite = (500, 240)
            });
            SyncProfileGateSupport.RequireLocal(path);
            var outcome = new RawBaseLoader().LoadPreviewBaseWithOutcome(new ImageFile(path),
                Off with { Distortion = true }, CancellationToken.None);
            using var pair = outcome.Pair;
            Assert.NotNull(pair);
            var mask = Assert.IsType<SourceSaturationMask>(outcome.Analysis.SourceSaturation);
            var points = Enumerable.Range(0, mask.Height)
                .SelectMany(y => Enumerable.Range(0, mask.Width).Where(x => (mask.GetFlags(x, y) & 1) != 0)
                    .Select(x => (X: x, Y: y))).ToArray();
            Assert.NotEmpty(points);
            var mx = points.Average(p => p.X);
            var my = points.Average(p => p.Y);
            var image = pair.Interactive.Pixels;
            using var pixels = image.GetPixelsUnsafe();
            var values = pixels.ToShortArray(PixelMapping.RGB)!;
            var best = double.NegativeInfinity;
            var bx = 0;
            var by = 0;

            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var index = (y * (int)image.Width + x) * 3;
                    var score = values[index] - 0.5 * (values[index + 1] + values[index + 2]);
                    if (score <= best) continue;

                    best = score;
                    bx = x;
                    by = y;
                }
            }

            rows.Add(new { exif, imageWidth = image.Width, imageHeight = image.Height,
                maskWidth = mask.Width, maskHeight = mask.Height, maskX = mx, maskY = my,
                featureX = bx, featureY = by, dx = mx - bx, dy = my - by,
                distance = Math.Sqrt(Math.Pow(mx - bx, 2) + Math.Pow(my - by, 2)), maskSites = points.Length });
        }

        Record("O4b", rows, output);
    }

    internal static string CopyOrientation(string source, string directory, ushort orientation)
    {
        SyncProfileGateSupport.RequireLocal(source);
        var bytes = File.ReadAllBytes(source);
        var ifd = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(ifd));

        for (var i = 0; i < count; i++)
        {
            var offset = ifd + 2 + i * 12;
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset)) != 274) continue;

            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 8), orientation);
        }

        var path = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(source)}-{orientation}.dng");
        File.WriteAllBytes(path, bytes);

        return path;
    }
}
