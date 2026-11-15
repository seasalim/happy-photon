using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MixerEditGroup : UserControl
{
    private MainWindowViewModel? _owner;

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

        foreach (var button in MixerBandPicker.Children.OfType<Button>())
        {
            button.PointerEntered += (_, _) =>
            {
                if (button.IsEffectivelyEnabled && DataContext is MainWindowViewModel vm &&
                    button.CommandParameter is ColorMixerBand band &&
                    this.TryFindResource("SurfaceLow", ActualThemeVariant, out var resource) && resource is ISolidColorBrush brush)
                    vm.BeginMixerBandHover(band, (uint)(brush.Color.R << 16 | brush.Color.G << 8 | brush.Color.B));
            };
            button.PointerExited += (_, _) => (DataContext as MainWindowViewModel)?.EndMixerBandHover();
        }

        DataContextChanged += (_, _) => BindOwner();
        AttachedToVisualTree += (_, _) => BindOwner();
        DetachedFromVisualTree += (_, _) =>
        {
            (DataContext as MainWindowViewModel)?.EndMixerBandHover();
            UnbindOwner();
        };
    }

    private void BindOwner()
    {
        UnbindOwner();
        _owner = DataContext as MainWindowViewModel;
        if (_owner == null) return;

        _owner.MixerBandsChanged += OnMixerBandsChanged;
        RefreshBands();
    }

    private void UnbindOwner()
    {
        if (_owner != null) _owner.MixerBandsChanged -= OnMixerBandsChanged;

        _owner = null;
    }

    private void OnMixerBandsChanged(object? sender, EventArgs e) => RefreshBands();

    private void RefreshBands()
    {
        if (_owner == null) return;

        foreach (var button in MixerBandPicker.Children.OfType<Button>())
        {
            var band = (ColorMixerBand)button.CommandParameter!;
            button.Classes.Set("active", _owner.ActiveMixerBand == band);
            if (button.Content is Border { Child: Ellipse dot }) dot.IsVisible = _owner.IsMixerBandTouched(band);
        }
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
