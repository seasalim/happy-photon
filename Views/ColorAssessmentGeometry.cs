using Avalonia;

namespace HappyPhoton.Views;

public readonly record struct ColorAssessmentGeometry(
    double BandWidth,
    Size FitBox,
    bool IsFieldVisible)
{
    public const double MinimumBandWidth = 24;
    public const double MaximumBandWidth = 48;
    private const double BandRatio = 0.04;

    public static ColorAssessmentGeometry Calculate(
        Size viewport,
        bool isColorAssessment)
    {
        if (!IsPositiveFinite(viewport.Width) ||
            !IsPositiveFinite(viewport.Height))
        {
            return new(0, default, false);
        }

        if (!isColorAssessment)
        {
            return new(0, viewport, false);
        }

        var bandWidth = Math.Clamp(
            Math.Round(
                BandRatio * Math.Min(viewport.Width, viewport.Height),
                MidpointRounding.AwayFromZero),
            MinimumBandWidth,
            MaximumBandWidth);
        var fitBox = new Size(
            viewport.Width - 4 * bandWidth,
            viewport.Height - 4 * bandWidth);
        if (!IsPositiveFinite(fitBox.Width) ||
            !IsPositiveFinite(fitBox.Height))
        {
            return new(0, viewport, false);
        }

        return new(bandWidth, fitBox, true);
    }

    private static bool IsPositiveFinite(double value) =>
        value > 0 && double.IsFinite(value);
}
