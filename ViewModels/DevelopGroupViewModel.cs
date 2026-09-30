using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class DevelopGroupViewModel(string name) : ObservableObject
{
    private readonly EditSettingsGroup[] _transferGroups = EditSettingsTransfer.Groups
        .Where(group => name switch
        {
            "Profile" => group.Name == "Camera Profile",
            "Optics" => group.Name is "Optics" or "Lens Profile",
            _ => group.Name == name
        }).ToArray();

    public string Name { get; } = name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEditDot), nameof(EditDotHelpText))]
    private bool _isExpanded = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEditDot), nameof(EditDotHelpText))]
    private bool _hasEdits;

    public bool ShowsEditDot => HasEdits && !IsExpanded;

    public string? EditDotHelpText => ShowsEditDot ? "has edits" : null;

    internal void RefreshEdits(EditSettings? settings)
    {
        var hasEdits = false;

        foreach (var group in _transferGroups)
        {
            hasEdits |= settings != null && group.DiffersFromDefault(settings);
        }

        HasEdits = hasEdits;
    }
}
