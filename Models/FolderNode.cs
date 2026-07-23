using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HappyPhoton.Models;

/// <summary>
/// Represents a folder node in the folder tree hierarchy.
/// </summary>
public partial class FolderNode : ObservableObject
{
    public string Path { get; }
    public string Name { get; }
    public bool IsDummy { get; init; }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    public ObservableCollection<FolderNode> Children { get; } = new();

    public bool HasDummyChild => Children.Count == 1 && Children[0].IsDummy;

    public FolderNode(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        if (string.IsNullOrEmpty(Name))
        {
            // For root paths like "/" or "C:\"
            Name = path;
        }
    }

    /// <summary>
    /// Creates a dummy placeholder node for lazy loading indication.
    /// </summary>
    public static FolderNode CreateDummy() => new("") { IsDummy = true };
}
