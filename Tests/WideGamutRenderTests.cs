using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WideGamutRenderTests
{
    [Fact]
    public void PreviewAlwaysUsesSrgbTarget()
    {
        ushort[] samples =
        [
            49402, 2998, 0,
            13015, 61719, 1154,
            52212, 16321, 170
        ];
        using var baseImage = RenderPipelineTestSupport.CreateBase(samples);
        var pipeline = new RenderPipeline();
        var options = new RenderOptions(false, false);
        using var defaultPreview = pipeline.Render(new RenderRequest(
            baseImage,
            new EditSettings(),
            RenderIntent.Preview,
            null,
            options));
        using var p3RequestedPreview = pipeline.Render(new RenderRequest(
            baseImage,
            new EditSettings(),
            RenderIntent.Preview,
            null,
            options,
            OutputColorSpace.DisplayP3));

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(defaultPreview.Image),
            RenderPipelineTestSupport.ReadPixels(p3RequestedPreview.Image));
    }
}
