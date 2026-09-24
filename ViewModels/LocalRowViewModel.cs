using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public sealed class LocalRowViewModel(LocalAdjustment local) : ObservableObject
{
    public LocalAdjustment Local { get; private set; } = local;
    public string Name => Local.Name;
    public string Glyph => Local.IsBrush ? "✎" : Local.IsRadial ? "○" : "▱";
    public bool Enabled => Local.Enabled;
    public bool HasLuminance => Local.Luminance?.IsEffective == true;
    public string RangeLabel => HasLuminance ? Local.Hue?.Enabled == true ? "Luminance · Hue" : "Luminance"
        : Local.Hue?.Enabled == true ? "Hue" : "";
    internal void Refresh(LocalAdjustment value)
    {
        Local = value;
        OnPropertyChanged(nameof(Local));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(HasLuminance));
        OnPropertyChanged(nameof(RangeLabel));
    }
}
