using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;
using static HappyPhoton.Tests.RawOrientationMeasureSupport;

namespace HappyPhoton.Tests;

public sealed class RawOrientationBaselineTests(ITestOutputHelper output)
{
    [Fact]
    public void O1Surfaces()
    {
        Require();
        using var directory = new TemporaryDirectory();
        var original = SyncSpotGateSupport.Fixture();
        var one = SyncSpotRotatedFixture.Create(original, directory.Path, 1);
        var three = SyncSpotRotatedFixture.Create(original, directory.Path, 3);
        var rows = new List<object>();

        foreach (var preview in new[] { true, false })
        {
            using var a = Load(one, preview);
            using var b = Load(three, preview);
            using var expected = Transform(a.Pixels, 3);
            var same = Difference(a.Pixels, b.Pixels);
            var turned = Difference(expected, b.Pixels);

            foreach (var intent in preview ? new[] { RenderIntent.Preview }
                : new[] { RenderIntent.Preview, RenderIntent.Export })
            {
                using var ar = Render(a, intent);
                using var br = Render(b, intent);
                using var er = Transform(ar.Image, 3);
                rows.Add(new { surface = preview ? "Develop" : intent == RenderIntent.Preview ? "1:1" : "export",
                    width = b.Pixels.Width, height = b.Pixels.Height, baseSameQ16 = same.Max,
                    baseRotatedQ16 = turned.Max, renderSameQ16 = Difference(ar.Image, br.Image).Max,
                    renderRotatedQ16 = Difference(er, br.Image).Max });
            }
        }

        Record("O1", rows, output);
    }

    [Fact]
    public void O2AllExif()
    {
        Require();
        using var directory = new TemporaryDirectory();
        var original = SyncSpotGateSupport.Fixture();
        var one = SyncSpotRotatedFixture.Create(original, directory.Path, 1);
        using var source = Load(one, false);
        var rows = new List<object>();

        for (ushort exif = 1; exif <= 8; exif++)
        {
            var path = exif == 1 ? one : SyncSpotRotatedFixture.Create(original, directory.Path, exif);
            using var actual = Load(path, false);
            using var expected = Transform(source.Pixels, exif);
            var dimensionsMatch = actual.Pixels.Width == expected.Width && actual.Pixels.Height == expected.Height;
            rows.Add(new { exif, width = actual.Pixels.Width, height = actual.Pixels.Height,
                expectedWidth = expected.Width, expectedHeight = expected.Height,
                dimensionsMatch, maxQ16 = dimensionsMatch ? Difference(expected, actual.Pixels).Max : (int?)null,
                frameOrientation = actual.Info.SensorFrame?.Orientation,
                recordedOrientation = actual.Info.ExifOrientationApplied });
        }

        Record("O2", rows, output);
    }
}
