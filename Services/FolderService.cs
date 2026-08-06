using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Service for scanning folders and finding image files.
/// </summary>
public class FolderService
{
    public IEnumerable<ImageFile> GetImagesInFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            yield break;

        var files = Directory.EnumerateFiles(folderPath)
            .Where(f => ImageFile.SupportedExtensions.Contains(
                Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            yield return new ImageFile(file);
        }
    }

    public bool IsSupportedImage(string filePath)
    {
        return ImageFile.SupportedExtensions.Contains(
            Path.GetExtension(filePath));
    }
}
