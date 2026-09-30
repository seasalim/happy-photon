using CommunityToolkit.Mvvm.ComponentModel;

namespace HappyPhoton.ViewModels;

public partial class DevelopGroupViewModel(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isExpanded = true;
}
