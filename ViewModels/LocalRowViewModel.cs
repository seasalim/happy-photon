using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public sealed class LocalRowViewModel(LocalAdjustment local) : ObservableObject
{
    public LocalAdjustment Local { get; private set; } = local;
    public string Name => Local.Name;
    public bool Enabled => Local.Enabled;
    internal void Refresh(LocalAdjustment value)
    {
        Local = value;
        OnPropertyChanged(nameof(Local));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Enabled));
    }
}
