using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Service for scanning folders and finding image files.
/// </summary>
public class FolderService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Standard formats
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp",
        // HEIC/HEIF (Apple)
        ".heic", ".heif",
        // RAW formats
        ".cr2", ".cr3",   // Canon
        ".nef", ".nrw",   // Nikon
        ".arw", ".srf", ".sr2",  // Sony
        ".dng",           // Adobe DNG
        ".raf",           // Fujifilm
        ".orf",           // Olympus
        ".rw2",           // Panasonic
        ".pef"            // Pentax
    };

    public IEnumerable<ImageFile> GetImagesInFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            yield break;

        var files = Directory.EnumerateFiles(folderPath)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            yield return new ImageFile(file);
        }
    }

    public bool IsSupportedImage(string filePath)
    {
        return SupportedExtensions.Contains(Path.GetExtension(filePath));
    }
}
