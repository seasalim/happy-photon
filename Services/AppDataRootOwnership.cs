using System.Text;

namespace HappyPhoton.Services;

public sealed class AppDataOwnershipException(string message) : IOException(message);

public static class AppDataRootOwnership
{
    public const string MarkerFileName = ".happy-photon-root";
    private const string MarkerContents = "Happy Photon application data root\nv1\n";

    public static void Claim(string root)
    {
        var path = Normalize(root);
        Directory.CreateDirectory(path);
        var marker = Path.Combine(path, MarkerFileName);
        if (File.Exists(marker))
        {
            AssertAppOwned(path);
            return;
        }

        WriteAtomic(marker, MarkerContents);
    }

    public static void ClaimFresh(string root)
    {
        var path = Normalize(root);
        if (Directory.Exists(path))
        {
            var marker = Path.Combine(path, MarkerFileName);
            if (File.Exists(marker))
            {
                AssertAppOwned(path);
                return;
            }
            if (Directory.EnumerateFileSystemEntries(path).Any())
            {
                throw new AppDataOwnershipException(
                    $"The dedicated Happy Photon folder '{path}' is not empty.");
            }
        }
        Claim(path);
    }

    public static void AssertAppOwned(string root)
    {
        var marker = Path.Combine(Normalize(root), MarkerFileName);
        string contents;
        try
        {
            contents = File.ReadAllText(marker, Encoding.UTF8);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new AppDataOwnershipException(
                $"Happy Photon cannot prove ownership of '{root}'.");
        }

        if (!string.Equals(contents, MarkerContents, StringComparison.Ordinal))
        {
            throw new AppDataOwnershipException(
                $"Happy Photon cannot prove ownership of '{root}'.");
        }
    }

    public static string CreateDedicatedChild(
        string selectedParent,
        string childName,
        IEnumerable<string>? nonOverlappingWith = null)
    {
        if (string.IsNullOrWhiteSpace(childName) ||
            childName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("A safe child folder name is required.", nameof(childName));
        }

        var child = Path.Combine(Normalize(selectedParent), childName.Trim());
        ValidateObviousTarget(child);
        // Overlaps must refuse before ClaimFresh, or a doomed pick leaves a
        // claimed folder inside an existing root.
        var resolvedChild = ResolveRealPath(child);
        foreach (var root in nonOverlappingWith ?? [])
        {
            var resolvedRoot = ResolveRealPath(root);
            if (IsSameOrDescendant(resolvedRoot, resolvedChild) ||
                IsSameOrDescendant(resolvedChild, resolvedRoot))
            {
                throw new ArgumentException(
                    "Choose a location outside the current catalog and cache folders.");
            }
        }
        ClaimFresh(child);
        return child;
    }

    public static void ValidateProposedRoots(
        string catalogRoot,
        string cacheRoot,
        IEnumerable<string>? otherRoots = null)
    {
        var roots = new[] { catalogRoot, cacheRoot }
            .Concat(otherRoots ?? [])
            .Select(ResolveRealPath)
            .ToArray();
        for (var left = 0; left < roots.Length; left++)
        {
            ValidateObviousTarget(roots[left]);
            for (var right = left + 1; right < roots.Length; right++)
            {
                if (IsSameOrDescendant(roots[left], roots[right]) ||
                    IsSameOrDescendant(roots[right], roots[left]))
                {
                    throw new ArgumentException(
                        "Catalog and cache locations must be separate, non-overlapping folders.");
                }
            }
        }
    }

    public static void ValidateObviousTarget(string path)
    {
        var candidate = ResolveRealPath(path);
        var root = Path.GetPathRoot(candidate);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (Same(candidate, root) || Same(candidate, home) || Same(candidate, pictures))
        {
            throw new ArgumentException(
                "Choose a dedicated child folder, not a home, Pictures, or volume root.");
        }
    }

    internal static bool IsSameOrDescendant(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative == "." ||
               (!Path.IsPathRooted(relative) && relative != ".." &&
                !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    internal static string ResolveRealPath(string path)
    {
        var full = Normalize(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (!Directory.Exists(current)) continue;
            try
            {
                var target = new DirectoryInfo(current)
                    .ResolveLinkTarget(returnFinalTarget: true);
                if (target != null) current = target.FullName;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // A path we cannot inspect cannot be used for I/O later. Preserve its
                // normalized spelling here so validation remains non-mutating.
            }
        }
        return Normalize(current);
    }

    internal static void WriteAtomic(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, contents, new UTF8Encoding(false));
        File.Move(temporary, path);
    }

    internal static void WriteAtomicOwned(
        string ownedRoot,
        string path,
        string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, contents, new UTF8Encoding(false));
        AssertAppOwned(ownedRoot);
        File.Move(temporary, path, overwrite: true);
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool Same(string left, string? right) =>
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(
            Normalize(left),
            Normalize(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
