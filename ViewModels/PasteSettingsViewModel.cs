using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public sealed partial class PasteSettingsViewModel : ObservableObject
{
    public PasteSettingsViewModel(string sourceName, int targetCount,
        IReadOnlyDictionary<string, bool> remembered, bool currentPhoto = false)
    {
        TargetCount = targetCount;
        Summary = currentPhoto
            ? $"From {sourceName} to this photo"
            : $"From {sourceName} to {targetCount} photos";
        Groups = EditSettingsTransfer.LookGroups.Select(group =>
            new PasteSettingsGroupViewModel(group,
                remembered.GetValueOrDefault(group.Name, group.IsDefault))).ToArray();

        foreach (var group in Groups)
        {
            group.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanPaste));
        }
    }

    public string Summary { get; }

    public int TargetCount { get; }

    public IReadOnlyList<PasteSettingsGroupViewModel> Groups { get; }

    public bool CanPaste => Groups.Any(group => group.IsSelected);

    public Dictionary<string, bool> CaptureChoice() =>
        Groups.ToDictionary(group => group.Group.Name, group => group.IsSelected);

    [RelayCommand]
    private void All() => SetSelection(_ => true);

    [RelayCommand]
    private void None() => SetSelection(_ => false);

    [RelayCommand]
    private void Defaults() => SetSelection(group => group.IsDefault);

    private void SetSelection(Func<EditSettingsGroup, bool> selected)
    {
        foreach (var group in Groups)
        {
            group.IsSelected = selected(group.Group);
        }
    }
}

public sealed partial class PasteSettingsGroupViewModel(
    EditSettingsGroup group, bool selected) : ObservableObject
{
    public EditSettingsGroup Group { get; } = group;

    [ObservableProperty]
    private bool _isSelected = selected;
}
