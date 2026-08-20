using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using HappyPhoton.Models;

using APath = Avalonia.Controls.Shapes.Path;

namespace HappyPhoton.Views;

public partial class CurveView
{
    public static readonly StyledProperty<ToneCurveChannel> ActiveChannelProperty =
        AvaloniaProperty.Register<CurveView, ToneCurveChannel>(nameof(ActiveChannel));

    public static readonly StyledProperty<CurveData?> CompositeCurveProperty =
        AvaloniaProperty.Register<CurveView, CurveData?>(nameof(CompositeCurve));

    public static readonly StyledProperty<bool> HasRedCurveProperty =
        AvaloniaProperty.Register<CurveView, bool>(nameof(HasRedCurve));

    public static readonly StyledProperty<bool> HasGreenCurveProperty =
        AvaloniaProperty.Register<CurveView, bool>(nameof(HasGreenCurve));

    public static readonly StyledProperty<bool> HasBlueCurveProperty =
        AvaloniaProperty.Register<CurveView, bool>(nameof(HasBlueCurve));

    public ToneCurveChannel ActiveChannel
    {
        get => GetValue(ActiveChannelProperty);
        set => SetValue(ActiveChannelProperty, value);
    }

    public CurveData? CompositeCurve
    {
        get => GetValue(CompositeCurveProperty);
        set => SetValue(CompositeCurveProperty, value);
    }

    public bool HasRedCurve
    {
        get => GetValue(HasRedCurveProperty);
        set => SetValue(HasRedCurveProperty, value);
    }

    public bool HasGreenCurve
    {
        get => GetValue(HasGreenCurveProperty);
        set => SetValue(HasGreenCurveProperty, value);
    }

    public bool HasBlueCurve
    {
        get => GetValue(HasBlueCurveProperty);
        set => SetValue(HasBlueCurveProperty, value);
    }

    private IBrush ActiveCurveBrush => ActiveChannel switch
    {
        ToneCurveChannel.Red => HappyPhotonColors.ColorLabelRed,
        ToneCurveChannel.Green => HappyPhotonColors.ColorLabelGreen,
        ToneCurveChannel.Blue => HappyPhotonColors.ColorLabelBlue,
        _ => HappyPhotonColors.PrimaryContainer
    };

    private void OnCompositeChannelClick(object? sender, RoutedEventArgs e) =>
        SelectChannel(ToneCurveChannel.Composite);

    private void OnRedChannelClick(object? sender, RoutedEventArgs e) =>
        SelectChannel(ToneCurveChannel.Red);

    private void OnGreenChannelClick(object? sender, RoutedEventArgs e) =>
        SelectChannel(ToneCurveChannel.Green);

    private void OnBlueChannelClick(object? sender, RoutedEventArgs e) =>
        SelectChannel(ToneCurveChannel.Blue);

    private void SelectChannel(ToneCurveChannel channel)
    {
        SetCurrentValue(ActiveChannelProperty, channel);
        UpdateChannelSelectors();
    }

    private void UpdateChannelSelectors()
    {
        if (CompositeChannelButton == null)
        {
            return;
        }

        CompositeChannelButton.Classes.Set(
            "active", ActiveChannel == ToneCurveChannel.Composite);
        RedChannelButton.Classes.Set("active", ActiveChannel == ToneCurveChannel.Red);
        GreenChannelButton.Classes.Set(
            "active", ActiveChannel == ToneCurveChannel.Green);
        BlueChannelButton.Classes.Set("active", ActiveChannel == ToneCurveChannel.Blue);
        RedChannelButton.Classes.Set("touched", HasRedCurve);
        GreenChannelButton.Classes.Set("touched", HasGreenCurve);
        BlueChannelButton.Classes.Set("touched", HasBlueCurve);
    }

    private void DrawCurvePath(
        CurveData curve,
        double width,
        double height,
        IBrush stroke,
        double thickness,
        double opacity)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(
                new Point(0, height - curve.LookupTable[0] / 255.0 * height),
                false);
            for (var index = 1; index < curve.LookupTable.Length; index++)
            {
                context.LineTo(new Point(
                    index / 255.0 * width,
                    height - curve.LookupTable[index] / 255.0 * height));
            }
            context.EndFigure(false);
        }

        _canvas!.Children.Add(new APath
        {
            Data = geometry,
            Stroke = stroke,
            StrokeThickness = thickness,
            Opacity = opacity
        });
    }
}
