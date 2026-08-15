using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed record CaptureFileGroup(IReadOnlyList<string> ImageIds);

/// <summary>
/// Derives session-scoped logical captures from image file paths.
/// </summary>
public static class CapturePairingService
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public static bool IsRawJpegPair(string firstPath, string secondPath)
    {
        ArgumentNullException.ThrowIfNull(firstPath);
        ArgumentNullException.ThrowIfNull(secondPath);

        var firstRole = GetRole(firstPath);
        var secondRole = GetRole(secondPath);
        if (firstRole == FileRole.Other || secondRole == FileRole.Other ||
            firstRole == secondRole)
        {
            return false;
        }

        return PathComparer.Equals(
                   Path.GetDirectoryName(firstPath),
                   Path.GetDirectoryName(secondPath)) &&
               string.Equals(
                   Path.GetFileNameWithoutExtension(firstPath),
                   Path.GetFileNameWithoutExtension(secondPath),
                   StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<CaptureFileGroup> GroupCaptures(
        IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var paths = filePaths
            .OrderBy(path => path, StringComparer.Ordinal)
            .Distinct(PathComparer)
            .ToList();
        var candidates = paths
            .Where(path => GetRole(path) != FileRole.Other)
            .GroupBy(
                path => new PairingKey(
                    Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path)),
                PairingKeyComparer.Instance);
        var pairedPaths = new HashSet<string>(PathComparer);
        var captures = new List<CaptureFileGroup>();

        foreach (var candidatesForStem in candidates)
        {
            var rawPaths = candidatesForStem
                .Where(path => GetRole(path) == FileRole.Raw)
                .ToList();
            var jpegPaths = candidatesForStem
                .Where(path => GetRole(path) == FileRole.Jpeg)
                .ToList();
            if (rawPaths.Count != 1 || jpegPaths.Count != 1 ||
                !IsRawJpegPair(rawPaths[0], jpegPaths[0]))
            {
                continue;
            }

            var members = new[] { rawPaths[0], jpegPaths[0] }
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            captures.Add(new CaptureFileGroup(members));
            pairedPaths.Add(rawPaths[0]);
            pairedPaths.Add(jpegPaths[0]);
        }

        captures.AddRange(paths
            .Where(path => !pairedPaths.Contains(path))
            .Select(path => new CaptureFileGroup([path])));
        captures.Sort((left, right) => string.CompareOrdinal(
            left.ImageIds[0],
            right.ImageIds[0]));
        return captures;
    }

    private static FileRole GetRole(string path)
    {
        var extension = Path.GetExtension(path);
        if (ImageFile.RawExtensions.Contains(extension))
        {
            return FileRole.Raw;
        }

        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            ? FileRole.Jpeg
            : FileRole.Other;
    }

    private readonly record struct PairingKey(string? Directory, string Basename);

    private sealed class PairingKeyComparer : IEqualityComparer<PairingKey>
    {
        public static PairingKeyComparer Instance { get; } = new();

        public bool Equals(PairingKey left, PairingKey right) =>
            PathComparer.Equals(left.Directory, right.Directory) &&
            StringComparer.OrdinalIgnoreCase.Equals(left.Basename, right.Basename);

        public int GetHashCode(PairingKey key) => HashCode.Combine(
            key.Directory == null ? 0 : PathComparer.GetHashCode(key.Directory),
            StringComparer.OrdinalIgnoreCase.GetHashCode(key.Basename));
    }

    private enum FileRole
    {
        Other,
        Raw,
        Jpeg
    }
}
