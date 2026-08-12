using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed record FolderScanResult(
    IReadOnlyList<ImageFile> Images,
    IReadOnlyList<string> SidecarPaths);

public sealed record XmpSidecarCandidate(
    string Path,
    DateTime LastWriteUtc,
    long Length,
    bool IsFullName);

public sealed record XmpSidecarResolution(
    XmpSidecarCandidate? Winner,
    XmpSidecarCandidate? Shadowed,
    string CreationPath,
    bool BaseNameAmbiguous);

public static class XmpSidecarPaths
{
    public static XmpSidecarResolution Resolve(
        string imagePath,
        IReadOnlyCollection<string> folderImagePaths,
        XmpSidecarNaming naming,
        IReadOnlyCollection<string>? indexedSidecarPaths = null)
    {
        var fullName = imagePath + ".xmp";
        var baseName = Path.ChangeExtension(imagePath, ".xmp");
        var stem = Path.GetFileNameWithoutExtension(imagePath);
        var matchingStemCount = folderImagePaths.Count(path =>
            string.Equals(Path.GetFileNameWithoutExtension(path), stem,
                StringComparison.OrdinalIgnoreCase));
        var ambiguous = matchingStemCount != 1;

        var candidates = new List<XmpSidecarCandidate>(2);
        AddExisting(candidates, fullName, isFullName: true, indexedSidecarPaths);
        if (!ambiguous && !string.Equals(
                fullName, baseName, StringComparison.OrdinalIgnoreCase))
        {
            AddExisting(candidates, baseName, isFullName: false, indexedSidecarPaths);
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.LastWriteUtc)
            .ThenByDescending(candidate => candidate.IsFullName)
            .ToArray();
        var creationPath = naming == XmpSidecarNaming.BaseName && !ambiguous
            ? baseName
            : fullName;
        return new XmpSidecarResolution(
            ordered.FirstOrDefault(),
            ordered.Skip(1).FirstOrDefault(),
            creationPath,
            ambiguous);
    }

    public static bool IsCandidateName(string sidecarPath, string imagePath)
    {
        var full = imagePath + ".xmp";
        var baseName = Path.ChangeExtension(imagePath, ".xmp");
        return string.Equals(sidecarPath, full, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sidecarPath, baseName, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddExisting(
        ICollection<XmpSidecarCandidate> candidates,
        string path,
        bool isFullName,
        IReadOnlyCollection<string>? indexedSidecarPaths)
    {
        if (indexedSidecarPaths != null && !indexedSidecarPaths.Contains(
                path, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }
        try
        {
            var info = new FileInfo(path);
            if (info.Exists)
            {
                candidates.Add(new XmpSidecarCandidate(
                    info.FullName, info.LastWriteTimeUtc, info.Length, isFullName));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
