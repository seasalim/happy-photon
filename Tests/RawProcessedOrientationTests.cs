using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawProcessedOrientationTests
{
    [Fact]
    public void DocumentedTable_MatchesRealHeadersAndSampledBasePixels()
    {
        var document = File.ReadAllText(Path.Combine(GoldenTestPaths.RepositoryRoot,
            "docs", "pipeline", "DECODE.md"));
        var rows = document.Split('\n').Where(line => line.StartsWith("| ") &&
            char.IsDigit(line[2])).Select(line => line.Split('|').Select(cell => cell.Trim()).ToArray()).ToArray();
        Assert.Equal(8, rows.Length);
        int[] flips = [0, 1, 3, 2, 4, 6, 7, 5];
        using var directory = new TemporaryDirectory();
        var original = SyncSpotGateSupport.Fixture();
        using var source = RawOrientationMeasureSupport.Load(original, false);
        var pixels = RenderPipelineTestSupport.ReadPixels(source.Pixels);
        var width = (int)source.Pixels.Width;
        var height = (int)source.Pixels.Height;

        for (ushort exif = 1; exif <= 8; exif++)
        {
            var row = rows[exif - 1];
            Assert.Equal(exif.ToString(), row[1]);
            Assert.Equal(flips[exif - 1].ToString(), row[2]);
            Assert.Equal(exif <= 4 ? "W × H" : "H × W", row[4]);
            Assert.Equal("None", row[5]);
            Assert.Equal(row[3], row[6]);
            Assert.Equal(flips[exif - 1], LibRawOrientation.ToNativeFlip(exif));
            Assert.Equal(exif, LibRawOrientation.FromNativeFlip(flips[exif - 1]));
            var path = SyncSpotRotatedFixture.Create(original, directory.Path, exif);

            using (var raw = LibRawContext.Open(path))
            {
                Assert.Equal(exif, raw.GetDimensions().Orientation);
                Assert.Equal(exif, raw.GetMetadata().Orientation);
            }

            using var target = RawOrientationMeasureSupport.Load(path, false);
            var expectedSize = exif <= 4 ? (width, height) : (height, width);
            Assert.Equal(expectedSize, ((int)target.Pixels.Width, (int)target.Pixels.Height));
            Assert.Equal(expectedSize, (target.Info.FullWidth, target.Info.FullHeight));
            Assert.Equal(exif, target.Info.ExifOrientationApplied);
            Assert.Equal(exif, target.Info.SensorFrame!.Orientation);
            var reader = new PhotoFrameFactsReader(new SourceAvailabilityService());
            Assert.Equal(expectedSize, reader.Read(new ImageFile(path), new EditSettings
            {
                Lens = new() { Distortion = false, ChromaticAberration = false }
            }));
            Assert.Equal(target.Info.SensorFrame, reader.ReadSensorFrame(new ImageFile(path)));
            var actual = RenderPipelineTestSupport.ReadPixels(target.Pixels);

            for (var y = 0; y < height; y += 137)
            {
                for (var x = 0; x < width; x += 173)
                {
                    var (tx, ty) = exif switch
                    {
                        2 => (width - 1 - x, y),
                        3 => (width - 1 - x, height - 1 - y),
                        4 => (x, height - 1 - y),
                        5 => (y, x),
                        6 => (height - 1 - y, x),
                        7 => (height - 1 - y, width - 1 - x),
                        8 => (y, width - 1 - x),
                        _ => (x, y)
                    };

                    for (var channel = 0; channel < 3; channel++)
                    {
                        Assert.Equal(pixels[(y * width + x) * 3 + channel],
                            actual[(ty * expectedSize.Item1 + tx) * 3 + channel]);
                    }
                }
            }
        }
    }
}
