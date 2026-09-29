using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public sealed partial class PasteSettingsViewModel : ObservableObject
{
    internal static IReadOnlyList<EditSettingsGroup> AvailableGroups { get; } =
        EditSettingsTransfer.Groups.Where(group => group.Kind == EditSettingsGroupKind.Look ||
            group.Name is "Crop & Straighten" or "Geometry" or "Camera Profile" or "Lens Profile" or "Locals").ToArray();

    internal static bool HasOwn(EditSettings settings, string group) => group switch
    {
        "Crop & Straighten" => settings.Crop is { IsFullImage: false } || settings.HorizonRotation != 0,
        "Geometry" => settings.Geometry is { IsIdentity: false },
        "Locals" => settings.Locals is { Count: > 0 },
        "Camera Profile" => settings.RawProfile != null,
        "Lens Profile" => settings.Lens.ProfileOverride != null,
        _ => false
    };

    public PasteSettingsViewModel(string sourceName, int targetCount,
        IReadOnlyDictionary<string, bool> remembered, bool currentPhoto = false,
        IReadOnlyList<EditSettings>? targets = null, Func<bool, int?>? reframeCount = null)
    {
        TargetCount = targetCount;
        Summary = currentPhoto
            ? $"From {sourceName} to this photo"
            : $"From {sourceName} to {targetCount} photos";
        Groups = AvailableGroups.Select(group =>
        {
            var count = targets?.Count(target => HasOwn(target, group.Name)) ?? 0;
            var note = count == 0 ? "none have their own" : $"replaces own on {count} of {targetCount}";

            note += group.Name switch
            {
                "Camera Profile" => "\nother camera models keep their own",
                "Lens Profile" => "\nphotos outside the lens mount keep their own",
                _ => ""
            };

            return new PasteSettingsGroupViewModel(group,
                remembered.GetValueOrDefault(group.Name, group.IsDefault), note);
        }).ToArray();

        foreach (var group in Groups)
        {
            group.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(PasteSettingsGroupViewModel.IsSelected)) return;

                OnPropertyChanged(nameof(CanPaste));
                UpdateReframeNote();
            };
        }

        UpdateReframeNote();

        void UpdateReframeNote()
        {
            if (reframeCount == null) return;

            var crop = Groups.Single(group => group.Group.Name == "Crop & Straighten");
            var count = reframeCount(Groups.Single(group => group.Group.Name == "Optics").IsSelected);
            var own = targets?.Count(target => HasOwn(target, crop.Group.Name)) ?? 0;
            crop.Note = own == 0 ? "none have their own" : $"replaces own on {own} of {targetCount}";
            if (count > 0) crop.Note += $" · reframed to fit on {count}";
        }
    }

    public string Summary { get; }

    public int TargetCount { get; }

    public IReadOnlyList<PasteSettingsGroupViewModel> Groups { get; }

    public IEnumerable<PasteSettingsGroupViewModel> LookGroups =>
        Groups.Where(group => group.Group.Kind == EditSettingsGroupKind.Look);

    public IEnumerable<PasteSettingsGroupViewModel> PhotoGroups =>
        Groups.Where(group => group.Group.Kind == EditSettingsGroupKind.PhotoSpecific);

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
    EditSettingsGroup group, bool selected, string note = "") : ObservableObject
{
    public EditSettingsGroup Group { get; } = group;

    [ObservableProperty]
    private string _note = note;

    [ObservableProperty]
    private bool _isSelected = selected;
}
