using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public partial class CropGeometryTests
{
    [Theory]
    [InlineData(.049)]
    [InlineData(20.01)]
    public void InfeasibleFitLeavesDraftUnchanged(double ratio)
    {
        var draft = new CropRegion();

        Assert.False(CropGeometry.CanFit(ratio));
        Assert.Same(draft, CropGeometry.Fit(draft, ratio));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(1d / 6)]
    public void PanoramaOriginalSwapLeavesDraftUnchanged(double frameRatio)
    {
        var draft = new CropRegion();

        Assert.False(CropGeometry.CanFit(CropGeometry.SwappedRatio(draft, frameRatio)));
        Assert.Same(draft, CropGeometry.Swap(draft, frameRatio));
    }

    [Theory]
    [InlineData(.05)]
    [InlineData(20)]
    public void ExtremeFeasibleRatioMeetsMinimumAndBounds(double ratio)
    {
        var crop = CropGeometry.Fit(new CropRegion(), ratio);

        Assert.True(CropGeometry.CanFit(ratio));
        Assert.InRange(crop.Right - crop.Left, CropGeometry.MinCropSize - 1e-12, 1);
        Assert.InRange(crop.Bottom - crop.Top, CropGeometry.MinCropSize - 1e-12, 1);
        Assert.Equal(ratio, (crop.Right - crop.Left) / (crop.Bottom - crop.Top), 12);
    }

    [Theory]
    [InlineData(6000, 4000, 0)]
    [InlineData(6000, 4000, 90)]
    [InlineData(4000, 3000, 0)]
    [InlineData(4000, 3000, 270)]
    public void PresetConversionUsesCorrectedFrame(int width, int height, int rotation)
    {
        var frame = RenderGeometry.CalculateOriginalViewSize(width, height, new EditSettings { Rotation = rotation });
        var frameRatio = frame.Width / (double)frame.Height;

        foreach (var ratio in new[] { 1d, 1.25, 4d / 3, 1.5, 16d / 9 })
        {
            var normalized = CropGeometry.NormalizedRatio(ratio, frameRatio);
            var crop = CropGeometry.Fit(new CropRegion(), normalized);
            Assert.Equal(ratio, CropGeometry.DraftPixelRatio(crop, frameRatio), 12);
        }
    }

    [Theory]
    [InlineData(4d / 3, 4d / 3, "Original")]
    [InlineData(3d / 4, 3d / 4, "Original")]
    [InlineData(1.5, 1.5, "Original")]
    [InlineData(1.5, 2d / 3, "Original")]
    [InlineData(4d / 3, 1, "1:1")]
    [InlineData(4d / 3, 1.506, "3:2")]
    [InlineData(4d / 3, 1.494, "3:2")]
    [InlineData(4d / 3, 1.51, "Custom")]
    [InlineData(4d / 3, 2d / 3, "3:2")]
    public void LabelIsDerivedWithoutOrientation(double frame, double ratio, string expected)
    {
        var crop = CropGeometry.Fit(new CropRegion(), CropGeometry.NormalizedRatio(ratio, frame));

        Assert.Equal(expected, CropGeometry.DeriveRatio(crop, frame));
    }

    [Fact]
    public void FitPreservesCenterInsideDraft()
    {
        var draft = new CropRegion { Left = .2, Top = .1, Right = .8, Bottom = .9 };
        var fit = CropGeometry.Fit(draft, 1.5);

        Assert.Equal(1.5, (fit.Right - fit.Left) / (fit.Bottom - fit.Top), 12);
        Assert.Equal(draft.Left + draft.Right, fit.Left + fit.Right, 12);
        Assert.Equal(draft.Top + draft.Bottom, fit.Top + fit.Bottom, 12);
        Assert.InRange(fit.Left, draft.Left - 1e-12, draft.Right + 1e-12);
        Assert.InRange(fit.Right, draft.Left - 1e-12, draft.Right + 1e-12);
        Assert.InRange(fit.Top, draft.Top - 1e-12, draft.Bottom + 1e-12);
        Assert.InRange(fit.Bottom, draft.Top - 1e-12, draft.Bottom + 1e-12);
    }

    [Theory]
    [InlineData(.4, .4)]
    [InlineData(0, 0)]
    [InlineData(.95, .95)]
    public void SmallFitGrowsOnlyToMinimumAndStaysInBounds(double left, double top)
    {
        var draft = new CropRegion { Left = left, Top = top, Right = left + .05, Bottom = top + .05 };
        var fit = CropGeometry.Fit(draft, 4d / 3);

        Assert.Equal(.05, fit.Bottom - fit.Top, 12);
        Assert.Equal(.05 * 4 / 3, fit.Right - fit.Left, 12);
        Assert.InRange(fit.Left, 0, 1);
        Assert.InRange(fit.Right, 0, 1);
        Assert.InRange(fit.Top, 0, 1);
        Assert.InRange(fit.Bottom, 0, 1);

        if (left == .4)
        {
            Assert.Equal(draft.Left + draft.Right, fit.Left + fit.Right, 12);
            Assert.Equal(draft.Top + draft.Bottom, fit.Top + fit.Bottom, 12);
        }
    }

    [Fact]
    public void RepeatedSwapsKeepPixelRatioMinimumAndCenter()
    {
        var crop = CropGeometry.Fit(new CropRegion(), 1.5 / (4d / 3));

        for (var i = 0; i < 40; i++)
        {
            crop = CropGeometry.Swap(crop, 4d / 3);
            Assert.Equal(i % 2 == 0 ? 2d / 3 : 1.5, CropGeometry.DraftPixelRatio(crop, 4d / 3), 12);
            Assert.True(crop.Right - crop.Left >= .05 - 1e-12);
            Assert.True(crop.Bottom - crop.Top >= .05 - 1e-12);
            Assert.Equal(1, crop.Left + crop.Right, 12);
            Assert.Equal(1, crop.Top + crop.Bottom, 12);
        }
    }

    [Fact]
    public void TargetUsesExactPresetOrCustomStartRatio()
    {
        var crop = new CropRegion { Left = .1, Right = .9, Top = .2, Bottom = .7 };

        Assert.Equal(1.6, CropGeometry.TargetRatio(crop, "Custom", 4d / 3), 12);
        Assert.Equal(1.125, CropGeometry.TargetRatio(crop, "3:2", 4d / 3), 12);
        Assert.Equal(1, CropGeometry.TargetRatio(new CropRegion(), "Original", 3d / 4), 12);
    }
}
