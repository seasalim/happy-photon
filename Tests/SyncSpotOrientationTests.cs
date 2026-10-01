using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncSpotOrientationTests(ITestOutputHelper output)
{
    // Independent of the future repair mapper: observe the native output frame,
    // then compare the shared orientation contract with independently rotated labels.
    [Fact]
    public void G2_RealHeaderPixels()
    {
        SyncPhotoGateSupport.RequirePerformance();
        using var directory = new TemporaryDirectory();
        var original = SyncSpotGateSupport.Fixture();
        var decode = BaseDecodeSettings.From(SyncSpotGateSupport.Source());
        var loader = new GatedBaseImageLoader(new RawBaseLoader(), new SourceAvailabilityService());
        using var source = loader.LoadFullBase(new ImageFile(original), decode, CancellationToken.None);
        Assert.NotNull(source);
        Assert.Equal(1, source.Info.ExifOrientationApplied);
        var sourcePixels = RenderPipelineTestSupport.ReadPixels(source.Pixels);
        var width = (int)source.Pixels.Width;
        var height = (int)source.Pixels.Height;

        foreach (var orientation in new ushort[] { 3, 6, 8 })
        {
            var path = SyncSpotRotatedFixture.Create(original, directory.Path, orientation);
            var map = LoaderMap(path, decode, out var flip, out var preRotated);
            using var target = loader.LoadFullBase(new ImageFile(path), decode, CancellationToken.None);
            Assert.NotNull(target);
            Assert.Equal(LibRawOrientation.FromNativeFlip(flip), target.Info.ExifOrientationApplied);
            var reader = new PhotoFrameFactsReader(new SourceAvailabilityService());
            Assert.Equal(target.Info.SensorFrame, reader.ReadSensorFrame(new ImageFile(path)));
            var targetPixels = RenderPipelineTestSupport.ReadPixels(target.Pixels);
            var targetWidth = (int)target.Pixels.Width;
            var targetHeight = (int)target.Pixels.Height;
            var points = new List<(double U, double V)>();

            for (var y = 0; y <= 12; y++)
            {
                for (var x = 0; x <= 16; x++)
                {
                    points.Add(((x + .5) / 17, (y + .5) / 13));
                }
            }

            foreach (var repair in RepairTestWorkload.S64())
            {
                points.Add((repair.U, repair.V));
                points.Add((repair.Su, repair.Sv));
            }

            var maximum = 0;

            foreach (var point in points)
            {
                var x = Math.Clamp((int)(point.U * width), 0, width - 1);
                var y = Math.Clamp((int)(point.V * height), 0, height - 1);
                var mapped = map.Apply((x + .5) / width, (y + .5) / height);
                var repair = new Repair { U = (x + .5) / width, V = (y + .5) / height };
                var actual = RepairOrientation.Map(repair, 1, target.Info.SensorFrame!.Orientation);
                Assert.Equal(mapped.U, actual.U);
                Assert.Equal(mapped.V, actual.V);
                var tx = Math.Clamp((int)Math.Round(mapped.U * targetWidth - .5), 0, targetWidth - 1);
                var ty = Math.Clamp((int)Math.Round(mapped.V * targetHeight - .5), 0, targetHeight - 1);

                for (var channel = 0; channel < 3; channel++)
                {
                    var a = sourcePixels[(y * width + x) * 3 + channel];
                    var b = targetPixels[(ty * targetWidth + tx) * 3 + channel];
                    maximum = Math.Max(maximum, Math.Abs(a - b));
                }
            }

            output.WriteLine($"SYNC_SPOT gate=G2 exif={orientation} nativeFlip={flip} preRotated={preRotated} map={map} pixelMaxError={maximum} toleranceQ16=1 sampledPoints={points.Count} size={targetWidth}x{targetHeight}");
            Assert.InRange(maximum, 0, 1);
        }

        SyncPhotoGateSupport.LocalFixture(SyncPhotoGateSupport.Raw);
    }

    private static FrameMap LoaderMap(string path, BaseDecodeSettings decode, out int flip, out bool preRotated)
    {
        SyncProfileGateSupport.RequireLocal(path);
        using var raw = LibRawContext.Open(path);
        var dimensions = raw.GetDimensions();
        flip = LibRawOrientation.ToNativeFlip(dimensions.Orientation);
        Assert.Contains(flip, new[] { 3, 5, 6 });
        raw.Unpack();
        raw.ConfigureOutput(RawBaseLoader.ConfigureOutput(decode, preview: false));
        raw.Process();
        using var processed = raw.MakeProcessedImage();
        Assert.Equal(flip == 3 ? dimensions.VisibleWidth : dimensions.VisibleHeight, processed.Description.Width);
        Assert.Equal(flip == 3 ? dimensions.VisibleHeight : dimensions.VisibleWidth, processed.Description.Height);
        using var marker = RenderPipelineTestSupport.CreateBase(
            [1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 6], height: 2);
        // Model the native rotation independently; real decoded pixels below verify it.
        marker.Pixels.Rotate(flip == 3 ? 180 : flip == 5 ? 270 : 90);
        var transform = RawBaseLoader.ResolveOrientation(dimensions.Orientation);
        preRotated = transform.AlreadyApplied;
        Assert.True(preRotated);
        Assert.Equal(1, transform.LoaderOrientation);
        Assert.Equal(dimensions.Orientation, transform.FrameOrientation);
        var pixels = RenderPipelineTestSupport.ReadPixels(marker.Pixels);
        var width = (int)marker.Pixels.Width;
        var height = (int)marker.Pixels.Height;
        var origin = Find(1);
        var right = Find(3);
        var down = Find(4);

        (int X, int Y) Find(ushort label)
        {
            var pixel = Array.IndexOf(pixels, label) / 3;

            return (pixel % width / (width - 1), pixel / width / (height - 1));
        }

        return new(origin.X, right.X - origin.X, down.X - origin.X,
            origin.Y, right.Y - origin.Y, down.Y - origin.Y);
    }

    private readonly record struct FrameMap(int X0, int Xu, int Xv, int Y0, int Yu, int Yv)
    {
        internal (double U, double V) Apply(double u, double v) => (X0 + Xu * u + Xv * v, Y0 + Yu * u + Yv * v);

        public override string ToString() => $"({X0}+{Xu}u+{Xv}v,{Y0}+{Yu}u+{Yv}v)";
    }
}
