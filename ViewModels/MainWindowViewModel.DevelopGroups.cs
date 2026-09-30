using CommunityToolkit.Mvvm.Input;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private bool _settingDevelopGroups;

    public DevelopGroupViewModel ProfileGroup { get; } = new("Profile");

    public DevelopGroupViewModel WhiteBalanceGroup { get; } = new("White Balance");

    public DevelopGroupViewModel AdjustmentsGroup { get; } = new("Adjustments");

    public DevelopGroupViewModel PresenceGroup { get; } = new("Presence");

    public DevelopGroupViewModel ToneCurveGroup { get; } = new("Tone Curve");

    public DevelopGroupViewModel ColorMixerGroup { get; } = new("Color Mixer");

    public DevelopGroupViewModel DetailGroup { get; } = new("Detail");

    public DevelopGroupViewModel EffectsGroup { get; } = new("Effects");

    public DevelopGroupViewModel GeometryGroup { get; } = new("Geometry");

    public DevelopGroupViewModel OpticsGroup { get; } = new("Optics");

    public IReadOnlyList<DevelopGroupViewModel> DevelopGroupList { get; private set; } = [];

    private void InitializeDevelopGroups()
    {
        DevelopGroupList = [ProfileGroup, WhiteBalanceGroup, AdjustmentsGroup, PresenceGroup,
            ToneCurveGroup, ColorMixerGroup, DetailGroup, EffectsGroup, GeometryGroup, OpticsGroup];

        foreach (var group in DevelopGroupList)
        {
            group.PropertyChanged += (_, e) =>
            {
                if (!_settingDevelopGroups && e.PropertyName == nameof(DevelopGroupViewModel.IsExpanded))
                {
                    _ = PersistBrowsePreferenceAsync("Develop-group");
                }
            };
        }
    }

    public void RestoreDevelopGroups(IReadOnlyDictionary<string, bool> groups)
    {
        _settingDevelopGroups = true;

        try
        {
            foreach (var group in DevelopGroupList)
            {
                group.IsExpanded = groups.GetValueOrDefault(group.Name, true);
            }
        }
        finally
        {
            _settingDevelopGroups = false;
        }
    }

    public Dictionary<string, bool> CaptureDevelopGroups() =>
        DevelopGroupList.ToDictionary(group => group.Name, group => group.IsExpanded);

    [RelayCommand]
    public void SoloDevelopGroup(DevelopGroupViewModel group)
    {
        if (!DevelopGroupList.Contains(group)) return;

        RestoreDevelopGroups(DevelopGroupList.ToDictionary(item => item.Name, item => item == group));
        _ = PersistBrowsePreferenceAsync("Develop-group");
    }

    partial void OnIsWhiteBalancePickingChanged(bool value)
    {
        if (value) WhiteBalanceGroup.IsExpanded = true;
    }
}
