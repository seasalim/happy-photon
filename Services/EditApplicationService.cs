using ImageMagick;
using HappyPhoton.Models;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

/// <summary>
/// Service for applying edit settings to images including exposure, temperature,
/// brightness, contrast, saturation, and tone curves.
/// </summary>
public class EditApplicationService
{
    public void ApplyEdits(MagickImage image, EditSettings settings)
    {
        LogDebug(nameof(ApplyEdits), $"Entry - Rot={settings.Rotation}, Horizon={settings.HorizonRotation}, Exp={settings.Exposure}, Temp={settings.Temperature}, Bright={settings.Brightness}, Contrast={settings.Contrast}, Sat={settings.Saturation}, Vib={settings.Vibrance}, Shadows={settings.Shadows}, Highlights={settings.Highlights}, Curve={!settings.Curve.IsIdentity()}");

        // Apply rotation FIRST (before any color adjustments)
        if (settings.Rotation != 0)
        {
            LogDebug(nameof(ApplyEdits), $"Applying rotation: {settings.Rotation} degrees");
            image.Rotate(settings.Rotation);
        }

        CropRegion? safeCrop = null;
        if (settings.HorizonRotation != 0.0)
        {
            var sourceWidth = image.Width;
            var sourceHeight = image.Height;
            LogDebug(nameof(ApplyEdits), $"Applying horizon rotation: {settings.HorizonRotation:F2} degrees");
            image.Rotate(settings.HorizonRotation);
            // Rotate leaves a virtual page offset that would shift the crop coordinates.
            image.ResetPage();
            safeCrop = CropGeometry.SafeBoundsAfterRotation(
                sourceWidth, sourceHeight, settings.HorizonRotation, image.Width, image.Height);
        }

        var effectiveCrop = GetEffectiveCrop(settings.Crop, safeCrop);

        // Apply crop after rotation, before color adjustments.
        if (effectiveCrop != null && !effectiveCrop.IsFullImage)
        {
            var (x, y, w, h) = effectiveCrop.ToPixels((int)image.Width, (int)image.Height);
            LogDebug(nameof(ApplyEdits), $"Applying crop: x={x}, y={y}, w={w}, h={h}");
            image.Crop(new MagickGeometry(x, y, (uint)w, (uint)h));
            image.ResetPage();
        }

        // Exposure and temperature adjustments must be done in linear light space
        bool needsLinearProcessing = settings.Exposure != 0 || settings.Temperature != 0;
        var originalColorSpace = image.ColorSpace;

        if (needsLinearProcessing && originalColorSpace == ColorSpace.sRGB)
        {
            LogDebug(nameof(ApplyEdits), "Converting to linear RGB for exposure/temperature");
            image.ColorSpace = ColorSpace.RGB;
        }

        // Apply exposure (brightness in stops)
        if (settings.Exposure != 0)
        {
            var factor = Math.Pow(2, settings.Exposure);
            LogDebug(nameof(ApplyEdits), $"Applying exposure: {settings.Exposure} stops (factor={factor:F3})");
            image.Evaluate(Channels.All, EvaluateOperator.Multiply, factor);
        }

        // Apply color temperature (shift red/blue channels)
        if (settings.Temperature != 0)
        {
            var tempFactor = settings.Temperature / 100.0;
            LogDebug(nameof(ApplyEdits), $"Applying temperature: {settings.Temperature} ({(settings.Temperature > 0 ? "warmer" : "cooler")})");
            image.Evaluate(Channels.Red, EvaluateOperator.Multiply, 1 + tempFactor * 0.08);
            image.Evaluate(Channels.Blue, EvaluateOperator.Multiply, 1 - tempFactor * 0.08);
        }

        // Convert back to sRGB for perceptual adjustments
        if (needsLinearProcessing && originalColorSpace == ColorSpace.sRGB)
        {
            LogDebug(nameof(ApplyEdits), "Converting back to sRGB");
            image.ColorSpace = ColorSpace.sRGB;
        }

        // Apply brightness and contrast together
        if (settings.Brightness != 0 || settings.Contrast != 0)
        {
            LogDebug(nameof(ApplyEdits), $"Applying brightness/contrast: B={settings.Brightness}, C={settings.Contrast}");
            image.BrightnessContrast(new Percentage(settings.Brightness), new Percentage(settings.Contrast));
        }

        // Apply saturation
        if (settings.Saturation != 0)
        {
            LogDebug(nameof(ApplyEdits), $"Applying saturation: {settings.Saturation}");
            image.Modulate(new Percentage(100), new Percentage(100 + settings.Saturation), new Percentage(100));
        }

        // Apply vibrance (simplified fallback)
        if (settings.Vibrance != 0)
        {
            LogDebug(nameof(ApplyEdits), $"Applying vibrance: {settings.Vibrance}");
            image.Modulate(new Percentage(100), new Percentage(100 + settings.Vibrance * 0.5), new Percentage(100));
        }

        // Apply shadows and highlights
        if (settings.Shadows != 0)
        {
            LogDebug(nameof(ApplyEdits), $"Applying shadows: {settings.Shadows}");
            var gamma = 1.0 + (settings.Shadows / 100.0) * 0.2;
            image.GammaCorrect(gamma);
        }

        if (settings.Highlights != 0)
        {
            LogDebug(nameof(ApplyEdits), $"Applying highlights: {settings.Highlights}");
            image.Negate();
            var gamma = 1.0 - (settings.Highlights / 100.0) * 0.2;
            image.GammaCorrect(gamma);
            image.Negate();
        }

        // Apply tone curve
        if (!settings.Curve.IsIdentity())
        {
            LogDebug(nameof(ApplyEdits), "Applying tone curve");
            ApplyCurve(image, settings.Curve);
        }
    }

    private static CropRegion? GetEffectiveCrop(CropRegion? crop, CropRegion? safeCrop)
    {
        if (crop == null)
        {
            return safeCrop;
        }

        return safeCrop == null ? crop : CropGeometry.Intersect(crop, safeCrop);
    }

    public void ApplyCurve(MagickImage image, CurveData curve)
    {
        using var clutImage = new MagickImage(MagickColors.Black, 256, 1);
        using var clutPixels = clutImage.GetPixels();

        for (int i = 0; i < 256; i++)
        {
            var value = curve.LookupTable[i];
            var value16 = (ushort)(value * 257);
            clutPixels.SetPixel(i, 0, new ushort[] { value16, value16, value16 });
        }

        image.Clut(clutImage, PixelInterpolateMethod.Bilinear, Channels.RGB);
    }

    public void ApplyResize(MagickImage image, int maxDimension)
    {
        var maxDim = Math.Max(image.Width, image.Height);
        if (maxDim > (uint)maxDimension)
        {
            var geometry = new MagickGeometry((uint)maxDimension, (uint)maxDimension)
            {
                IgnoreAspectRatio = false
            };
            image.Resize(geometry);
        }
    }
}
