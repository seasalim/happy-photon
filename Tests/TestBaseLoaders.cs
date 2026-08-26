using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal sealed class NullBaseLoader(
    BaseImageLoadFailure failure = BaseImageLoadFailure.DecodeFailed)
    : IBaseImageLoader
{
    public bool CanLoad(ImageFile file) => true;

    public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        BaseImageLoadOutcome.Failed(failure);

    public BaseImage? LoadFullBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) => null;
}

internal static class TestEditSettingsFactory
{
    public static EditSettings CreateTonal(
        double exposure = 0,
        int brightness = 0,
        int contrast = 0,
        int saturation = 0,
        int vibrance = 0,
        int shadows = 0,
        int highlights = 0,
        CurveData? curve = null)
    {
        var settings = new EditSettings
        {
            Exposure = exposure,
            Brightness = brightness,
            Contrast = contrast,
            Saturation = saturation,
            Vibrance = vibrance,
            Shadows = shadows,
            Highlights = highlights
        };
        if (curve != null) settings.Curve = curve;
        return settings;
    }
}
