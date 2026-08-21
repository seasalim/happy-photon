using System.Runtime.InteropServices;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal static class RenderPipelineTestSupport
{
    public static BaseImage CreateBase(
        ushort[] samples,
        bool isRaw = false,
        int height = 1,
        double sourceBiasEv = 0,
        DcpHueSatMap? hueSatMap = null,
        SourceSaturationMask? sourceSaturation = null)
    {
        var width = samples.Length / 3 / height;
        var pixels = RawBaseLoader.ImportRgb16(
            MemoryMarshal.AsBytes(samples.AsSpan()),
            width,
            height);
        return new BaseImage(
            pixels,
            new BaseImageInfo(
                isRaw ? BaseSourceKind.RawLibRaw : BaseSourceKind.Standard,
                isRaw,
                BaseDecodeSettings.Default,
                null,
                null,
                6504,
                0,
                false,
                null,
                1,
                width,
                height,
                SourceExposureBiasEv: sourceBiasEv)
            {
                DcpProfile = hueSatMap == null
                    ? null
                    : new DcpProfilePayload("synthetic", "Synthetic", hueSatMap),
                ProfileToken = hueSatMap == null ? string.Empty : "synthetic"
            },
            sourceSaturation);
    }

    public static ushort[] ReadPixels(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read RGB pixels.");
}
