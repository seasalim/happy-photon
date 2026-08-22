using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public partial class MixerEditGroup : UserControl
{
    public static FuncValueConverter<ColorMixerBand, IBrush>
        HueTrackBrushConverter { get; } = new(CreateHueTrackBrush);

    public static FuncValueConverter<ColorMixerBand, IBrush>
        SaturationTrackBrushConverter { get; } =
        new(CreateSaturationTrackBrush);

    public static FuncValueConverter<ColorMixerBand, IBrush>
        LuminanceTrackBrushConverter { get; } =
        new(CreateLuminanceTrackBrush);

    public MixerEditGroup()
    {
        InitializeComponent();
    }

    private static IBrush CreateHueTrackBrush(ColorMixerBand band) =>
        Gradient(HappyPhotonColors.GetMixerHueTrackColors(band));

    private static IBrush CreateSaturationTrackBrush(ColorMixerBand band) =>
        Gradient(HappyPhotonColors.GetMixerSaturationTrackColors(band));

    private static IBrush CreateLuminanceTrackBrush(ColorMixerBand band) =>
        Gradient(HappyPhotonColors.GetMixerLuminanceTrackColors(band));

    private static LinearGradientBrush Gradient(params Color[] colors)
    {
        var stops = new GradientStops();
        for (var index = 0; index < colors.Length; index++)
        {
            stops.Add(new GradientStop(
                colors[index],
                index / (double)(colors.Length - 1)));
        }
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = stops
        };
    }

}
