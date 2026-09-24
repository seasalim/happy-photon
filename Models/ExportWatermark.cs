using CommunityToolkit.Mvvm.ComponentModel;

namespace HappyPhoton.Models;

public enum WatermarkColor { White, Black }
public enum WatermarkEdge { Top, Bottom, Left, Right, Center }
public enum WatermarkAlignment { Start, Middle, End }

public sealed record WatermarkSpec(
    string Text, string FontFamily = "", bool Bold = false, bool Italic = false,
    double Size = 3, WatermarkColor Color = WatermarkColor.White, double Opacity = 60,
    WatermarkEdge Edge = WatermarkEdge.Bottom, WatermarkAlignment Alignment = WatermarkAlignment.End,
    bool RotateAlongEdge = true, double Margin = 2);

public partial class ExportWatermark : ObservableObject
{
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _fontFamily = string.Empty;
    [ObservableProperty] private bool _bold;
    [ObservableProperty] private bool _italic;
    [ObservableProperty] private double _size = 3;
    [ObservableProperty] private WatermarkColor _color;
    [ObservableProperty] private double _opacity = 60;
    [ObservableProperty] private WatermarkEdge _edge = WatermarkEdge.Bottom;
    [ObservableProperty] private WatermarkAlignment _alignment = WatermarkAlignment.End;
    [ObservableProperty] private bool _rotateAlongEdge = true;
    [ObservableProperty] private double _margin = 2;
    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, (value ?? string.Empty).Replace("\r", "").Replace("\n", ""));
    }

    internal Func<string, string>? ResolveFontFamily { get; set; }
    public WatermarkSpec? Snapshot() => Enabled ? Capture() with
    {
        FontFamily = ResolveFontFamily?.Invoke(FontFamily) ?? FontFamily
    } : null;

    public WatermarkSpec Capture() => new(Text, FontFamily, Bold, Italic, Size,
        Color, Opacity, Edge, Alignment, RotateAlongEdge, Margin);

    public void Restore(WatermarkSpec spec, bool enabled)
    {
        Text = spec.Text;
        FontFamily = spec.FontFamily;
        Bold = spec.Bold;
        Italic = spec.Italic;
        Size = spec.Size;
        Color = spec.Color;
        Opacity = spec.Opacity;
        Edge = spec.Edge;
        Alignment = spec.Alignment;
        RotateAlongEdge = spec.RotateAlongEdge;
        Margin = spec.Margin;
        Enabled = enabled;
    }
}
