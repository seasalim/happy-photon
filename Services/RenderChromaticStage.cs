using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal static class RenderChromaticStage
{
    internal static (double[,] Matrix, double Fold) CreateNormalizedMatrix(
        BaseImageInfo info,
        EditSettings settings)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(settings);
        return ChromaticAdaptation.NormalizeForRender(
            CreateWhiteBalanceMatrix(info, settings));
    }

    internal static double[,] CreateWhiteBalanceMatrix(
        BaseImageInfo info,
        EditSettings settings)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(settings);
        if (info.IsMonochrome)
        {
            return ChromaticAdaptation.Identity();
        }
        var whiteBalance = settings.Wb ??
            throw new ArgumentException(
                "White-balance settings are required.",
                nameof(settings));
        return whiteBalance.Mode == WbMode.AsShot
            ? ChromaticAdaptation.Identity()
            : CreateMatrix(whiteBalance, info);
    }

    private static double[,] CreateMatrix(
        WhiteBalanceSettings settings,
        BaseImageInfo info) =>
        settings.Mode switch
        {
            WbMode.Custom or WbMode.Preset => WhiteBalanceModel.CreateMatrix(
                RequireValue(settings.Kelvin, nameof(settings.Kelvin)),
                RequireValue(settings.Tint, nameof(settings.Tint)),
                info.AsShotKelvin,
                info.AsShotTint),
            WbMode.Picked => WhiteBalanceModel.CreateGainMatrix(
                settings.Gains ??
                throw new ArgumentException(
                    $"{settings.Mode} white balance requires gains.",
                    nameof(settings))),
            _ => throw new InvalidOperationException(
                $"Unsupported white-balance mode: {settings.Mode}.")
        };

    private static double RequireValue(double? value, string name) =>
        value ?? throw new ArgumentException(
            $"White balance requires {name}.",
            name);
}
