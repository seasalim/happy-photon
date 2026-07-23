namespace HappyPhoton.Models;

public class AppSettings
{
    public string? RootFolderPath { get; set; }
    public string? SelectedFolderPath { get; set; }
    public ImageFileTypeFilter FileTypeFilter { get; set; } = ImageFileTypeFilter.All;
    public bool McpServerEnabled { get; set; }
    public string? McpToken { get; set; }
}
