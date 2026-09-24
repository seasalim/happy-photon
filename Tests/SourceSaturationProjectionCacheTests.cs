using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SourceSaturationProjectionCacheTests
{
    [Fact]
    public void Render_ContrastEditReusesProjectionAcrossFreshGeometryMaps()
    {
        using var source = RenderPipelineTestSupport.CreateBase(new ushort[32 * 24 * 3], height: 24);
        var mask = new SourceSaturationMask(32, 24);
        mask.SetFlags(16, 12, 7);
        var settings = Settings();
        var first = RenderAndProject(source, mask, settings);
        var edited = settings.Clone();
        edited.Contrast = 20;

        var second = RenderAndProject(source, mask, edited);

        Assert.Same(first, second);
        Assert.Same(first.Mask, second.Mask);
    }

    [Theory]
    [InlineData("rotation")]
    [InlineData("horizon")]
    [InlineData("crop")]
    [InlineData("vertical")]
    [InlineData("horizontal")]
    [InlineData("aspect")]
    [InlineData("distortion")]
    [InlineData("width")]
    [InlineData("height")]
    public void Render_GeometryOrTargetSizeEditInvalidatesProjection(string change)
    {
        using var source = RenderPipelineTestSupport.CreateBase(new ushort[32 * 24 * 3], height: 24);
        var mask = new SourceSaturationMask(32, 24);
        mask.SetFlags(16, 12, 7);
        var settings = Settings();
        var first = RenderAndProject(source, mask, settings);
        var edited = settings.Clone();
        switch (change)
        {
            case "rotation": edited.Rotation = 180; break;
            case "horizon": edited.HorizonRotation = -3; break;
            case "crop": edited.Crop!.Left = 0.2; break;
            case "vertical": edited.Geometry!.Vertical = -15; break;
            case "horizontal": edited.Geometry!.Horizontal = 20; break;
            case "aspect": edited.Geometry!.Aspect = -10; break;
            case "distortion": edited.Geometry!.Distortion = 25; break;
        }

        var second = RenderAndProject(source, mask, edited,
            change == "width" ? 7 : null, change == "height" ? 5 : null);

        Assert.NotSame(first, second);
    }

    private static EditSettings Settings() => new()
    {
        HorizonRotation = 3,
        Geometry = new GeometrySettings { Vertical = 15, Horizontal = -20, Aspect = 10, Distortion = -25 },
        Crop = new CropRegion { Left = 0.1, Top = 0.1, Right = 0.9, Bottom = 0.9 },
        Detail = new DetailSettings { CaptureSharpen = 0 }
    };

    private static SourceSaturationProjection RenderAndProject(
        BaseImage source, SourceSaturationMask mask, EditSettings settings, int? width = null, int? height = null)
    {
        using var rendered = new RenderPipeline().Render(new RenderRequest(
            source, settings, RenderIntent.Preview, null, new RenderOptions(ComputeStats: true))
        {
            SourceSaturation = mask
        });
        using var geometry = RenderGeometry.Apply(source.Pixels, settings, out var trace);
        return SourceSaturationMaskProjector.Project(mask, settings, trace,
            width ?? (int)rendered.Image.Width, height ?? (int)rendered.Image.Height)!;
    }
}
