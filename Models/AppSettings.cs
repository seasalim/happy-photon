namespace HappyPhoton.Models;

public class AppSettings
{
    public string? RootFolderPath { get; set; }
    public string? SelectedFolderPath { get; set; }
    public int? FirstRunExperienceVersion { get; set; }
    public ImageFileTypeFilter FileTypeFilter { get; set; } = ImageFileTypeFilter.All;
    public LibraryThumbnailSize LibraryThumbnailSize { get; set; } = LibraryThumbnailSize.Medium;
    public AppTheme AppTheme { get; set; } = AppTheme.Dark;
    public bool StripLocationData { get; set; }
    public OutputSharpeningMode OutputSharpening { get; set; } =
        OutputSharpeningMode.Screen;
}
