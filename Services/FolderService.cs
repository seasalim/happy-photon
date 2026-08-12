using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Service for scanning folders and finding image files.
/// </summary>
public class FolderService
{
    public FolderScanResult ScanFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return new FolderScanResult([], []);

        var images = new List<ImageFile>();
        var sidecars = new List<string>();
        foreach (var file in new DirectoryInfo(folderPath).EnumerateFiles())
        {
            if (ImageFile.SupportedExtensions.Contains(file.Extension))
            {
                images.Add(new ImageFile(
                    file.FullName,
                    SourceAvailabilityService.GetEnumerationHint(file)));
            }
            else if (string.Equals(file.Extension, ".xmp",
                         StringComparison.OrdinalIgnoreCase))
            {
                sidecars.Add(file.FullName);
            }
        }

        images.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(
            left.FilePath, right.FilePath));
        sidecars.Sort(StringComparer.OrdinalIgnoreCase);
        return new FolderScanResult(images, sidecars);
    }

    public IEnumerable<ImageFile> GetImagesInFolder(string folderPath)
    {
        return ScanFolder(folderPath).Images;
    }

    public bool IsSupportedImage(string filePath)
    {
        return ImageFile.SupportedExtensions.Contains(
            Path.GetExtension(filePath));
    }
}
