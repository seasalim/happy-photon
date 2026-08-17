using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawWhiteBalanceRenderTests
{
    private readonly RenderPipeline _pipeline = new();

    [Theory]
    [InlineData(WbMode.Custom)]
    [InlineData(WbMode.Preset)]
    public void Render_TargetAtRawFallbackAnchor_IsIdentity(WbMode mode)
    {
        using var baseImage = CreateRawFallbackBase();
        var settings = new EditSettings
        {
            Wb = new WhiteBalanceSettings
            {
                Mode = mode,
                Kelvin = 5500,
                Tint = 0
            }
        };

        using var asShot = Render(new EditSettings(), baseImage);
        using var anchored = Render(settings, baseImage);

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(asShot.Image),
            RenderPipelineTestSupport.ReadPixels(anchored.Image));
    }

    private RenderResult Render(EditSettings settings, BaseImage baseImage) =>
        _pipeline.Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            null,
            new RenderOptions()));

    private static BaseImage CreateRawFallbackBase()
    {
        ushort[] samples =
        [
            8000, 12000, 16000,
            24000, 20000, 12000
        ];
        var pixels = RawBaseLoader.ImportRgb16(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan()),
            width: 2,
            height: 1);
        return new BaseImage(
            pixels,
            new BaseImageInfo(
                BaseSourceKind.RawLibRaw,
                IsRawSource: true,
                BaseDecodeSettings.Default,
                CamMul: null,
                CamToSrgb: null,
                AsShotKelvin: 5500,
                AsShotTint: 0,
                HadIccProfile: false,
                IccDescription: null,
                ExifOrientationApplied: 1,
                FullWidth: 2,
                FullHeight: 1));
    }
}
