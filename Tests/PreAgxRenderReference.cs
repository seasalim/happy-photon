using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal static class PreAgxRenderReference
{
    private const int LutLength = 4096;

    internal static MagickImage Render(
        BaseImage baseImage,
        EditSettings settings,
        int? maxDimension)
    {
        var output = RenderGeometry.Apply(baseImage.Pixels, settings, out _);
        try
        {
            var whiteBalance = CreateWhiteBalance(baseImage.Info, settings);
            var matrix = ChromaticAdaptation.Multiply(
                RgbColorSpaceMatrices.LinearRec2020ToLinearSrgb,
                whiteBalance);
            var normalized = ChromaticAdaptation.NormalizeForRender(matrix);
            var parameters = new ToneParams(
                settings.Exposure + baseImage.Info.SourceExposureBiasEv,
                normalized.Fold,
                settings.Brightness,
                settings.Contrast,
                settings.Shadows,
                settings.Highlights,
                settings.BaseLook ?? baseImage.Info.IsRawSource,
                settings.Curve);
            Apply(output, normalized.Matrix, ComposeLut(parameters));
            RenderColorEncoding.RetagAsSrgb(output);
            RenderChromaStage.Apply(output, settings);
            RenderNoiseReduction.Apply(
                output,
                baseImage.Info,
                settings.Detail);
            RenderSharpening.ApplyCapture(
                output,
                baseImage.Info,
                settings.Detail);
            if (maxDimension is { } limit)
            {
                RenderColorEncoding.ResizeInLinearLight(output, limit);
            }
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private static double[,] CreateWhiteBalance(
        BaseImageInfo info,
        EditSettings settings)
    {
        var whiteBalance = settings.Wb ?? throw new ArgumentException(
            "White-balance settings are required.", nameof(settings));
        return whiteBalance.Mode switch
        {
            WbMode.AsShot => ChromaticAdaptation.Identity(),
            WbMode.Custom or WbMode.Preset => WhiteBalanceModel.CreateMatrix(
                Require(whiteBalance.Kelvin, nameof(whiteBalance.Kelvin)),
                Require(whiteBalance.Tint, nameof(whiteBalance.Tint)),
                info.AsShotKelvin,
                info.AsShotTint),
            WbMode.Picked => WhiteBalanceModel.CreateGainMatrix(
                whiteBalance.Gains ?? throw new ArgumentException(
                    "Picked white balance requires gains.",
                    nameof(settings))),
            _ => throw new InvalidOperationException(
                $"Unsupported white-balance mode: {whiteBalance.Mode}.")
        };
    }

    private static double Require(double? value, string name) =>
        value ?? throw new ArgumentException(
            $"White balance requires {name}.", name);

    private static ushort[] ComposeLut(ToneParams parameters)
    {
        var lut = new ushort[LutLength];
        for (var index = 0; index < lut.Length; index++)
        {
            lut[index] = (ushort)Math.Round(
                ToneLut.Evaluate(
                    parameters,
                    index / (double)(LutLength - 1)) * ushort.MaxValue,
                MidpointRounding.AwayFromZero);
        }
        return lut;
    }

    private static void Apply(
        MagickImage image,
        double[,] matrix,
        ushort[] lut)
    {
        using var pixels = image.GetPixels();
        var values = pixels.GetArea(0, 0, image.Width, image.Height) ??
            throw new InvalidOperationException("Unable to access Q16 pixels.");
        var layout = RenderKernelSupport.GetLayout(pixels);
        var pixelCount = checked((int)(image.Width * image.Height));
        Parallel.For(0, pixelCount, pixel =>
        {
            var offset = pixel * layout.Channels;
            var red = values[offset + layout.Red];
            var green = values[offset + layout.Green];
            var blue = values[offset + layout.Blue];
            values[offset + layout.Red] = Transform(0);
            values[offset + layout.Green] = Transform(1);
            values[offset + layout.Blue] = Transform(2);

            ushort Transform(int row)
            {
                var value = matrix[row, 0] * red +
                    matrix[row, 1] * green +
                    matrix[row, 2] * blue;
                var transformed = (ushort)Math.Clamp(
                    Math.Floor(value + 0.5),
                    0,
                    ushort.MaxValue);
                return Interpolate(lut, transformed);
            }
        });
        pixels.SetArea(0, 0, image.Width, image.Height, values);
    }

    private static ushort Interpolate(ushort[] lut, ushort sample)
    {
        var scaled = (uint)sample * (lut.Length - 1);
        var lower = (int)(scaled / ushort.MaxValue);
        if (lower >= lut.Length - 1)
        {
            return lut[^1];
        }
        var remainder = scaled % ushort.MaxValue;
        var numerator =
            (ulong)lut[lower] * (ulong)(ushort.MaxValue - remainder) +
            (ulong)lut[lower + 1] * (ulong)remainder;
        return (ushort)((numerator + ushort.MaxValue / 2) / ushort.MaxValue);
    }
}
