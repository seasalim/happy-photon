using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RepairOrientationTests
{
    public static TheoryData<int, int> Pairs => new(
        from source in Enumerable.Range(1, 8)
        from target in Enumerable.Range(1, 8)
        select (source, target));

    [Theory]
    [MemberData(nameof(Pairs))]
    public void AllOrientationPairsMatchTheBaseLoaderAndKeepFields(int source, int target)
    {
        using var sensor = RenderPipelineTestSupport.CreateBase(
            [1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 6], height: 2);
        using var from = sensor.Pixels.Clone();
        using var to = sensor.Pixels.Clone();
        ImageServiceHelpers.ApplyExifOrientation((MagickImage)from, source);
        ImageServiceHelpers.ApplyExifOrientation((MagickImage)to, target);
        using var composed = sensor.Pixels.Clone();
        using var sequential = from.Clone();
        ImageServiceHelpers.ApplyExifOrientation((MagickImage)composed, RepairOrientation.Compose(source, target));
        ImageServiceHelpers.ApplyExifOrientation((MagickImage)sequential, target);
        Assert.Equal(composed.Width, sequential.Width);
        Assert.Equal(composed.Height, sequential.Height);
        Assert.Equal(RenderPipelineTestSupport.ReadPixels((MagickImage)composed),
            RenderPipelineTestSupport.ReadPixels((MagickImage)sequential));
        var fromPixels = RenderPipelineTestSupport.ReadPixels((MagickImage)from);
        var toPixels = RenderPipelineTestSupport.ReadPixels((MagickImage)to);

        for (var index = 0; index < 6; index++)
        {
            var repair = new Repair
            {
                U = (index % from.Width + .5) / from.Width,
                V = (index / (int)from.Width + .5) / from.Height,
                Su = 0, Sv = 1, Radius = .01, Feather = .75, Opacity = .5, Type = "clone"
            };
            var mapped = RepairOrientation.Map(repair, source, target);
            var x = (int)Math.Round(mapped.U * to.Width - .5);
            var y = (int)Math.Round(mapped.V * to.Height - .5);
            Assert.Equal(fromPixels[index * 3], toPixels[(y * (int)to.Width + x) * 3]);
            Assert.Equal(repair with { U = mapped.U, V = mapped.V, Su = mapped.Su, Sv = mapped.Sv }, mapped);
            var roundTrip = RepairOrientation.Map(mapped, target, source);
            Assert.Equal(repair.U, roundTrip.U, 12);
            Assert.Equal(repair.V, roundTrip.V, 12);
            Assert.Equal(repair.Su, roundTrip.Su);
            Assert.Equal(repair.Sv, roundTrip.Sv);
        }

        var grid = new Repair { U = 123d / 16384, V = 7813d / 16384, Su = 15683d / 16384, Sv = 4567d / 16384 };
        var result = RepairOrientation.Map(grid, source, target);
        Assert.Equal(grid, RepairOrientation.Map(result, target, source));
        Assert.All(new[] { result.U, result.V, result.Su, result.Sv }, value => Assert.Equal(Math.Round(value * 16384), value * 16384));
    }
}
