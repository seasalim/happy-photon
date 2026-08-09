namespace HappyPhoton.Models;

public class AppSettings
{
    public string? RootFolderPath { get; set; }
    public string? SelectedFolderPath { get; set; }
    public int? FirstRunExperienceVersion { get; set; }
    public ImageFileTypeFilter FileTypeFilter { get; set; } = ImageFileTypeFilter.All;
    public LibraryThumbnailSize LibraryThumbnailSize { get; set; } = LibraryThumbnailSize.Medium;
    public bool StripLocationData { get; set; }
    public bool OutputSharpening { get; set; } = true;
    public bool McpServerEnabled { get; set; }
    public string? McpToken { get; set; }
}
