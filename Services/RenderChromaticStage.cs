using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class RenderChromaticStage
{
    public static double Apply(
        MagickImage image,
        BaseImageInfo info,
        EditSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(settings);

        var whiteBalance = settings.Wb ??
            throw new ArgumentException(
                "White-balance settings are required.",
                nameof(settings));
        if (whiteBalance.Mode == WbMode.AsShot)
        {
            return 1.0;
        }

        var matrix = CreateMatrix(whiteBalance, info);
        var normalized = ChromaticAdaptation.NormalizeForRender(matrix);
        if (!IsIdentity(normalized.Matrix))
        {
            image.ColorMatrix(ToMagickMatrix(normalized.Matrix));
        }

        return normalized.Fold;
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

    private static bool IsIdentity(double[,] matrix)
    {
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var expected = row == column ? 1.0 : 0.0;
                if (matrix[row, column] != expected)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static MagickColorMatrix ToMagickMatrix(double[,] matrix) =>
        new(3,
        [
            matrix[0, 0], matrix[0, 1], matrix[0, 2],
            matrix[1, 0], matrix[1, 1], matrix[1, 2],
            matrix[2, 0], matrix[2, 1], matrix[2, 2]
        ]);
}
