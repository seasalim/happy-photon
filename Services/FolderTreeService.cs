using HappyPhoton.Models;

namespace HappyPhoton.Services;

public enum BrowseLocationValidation
{
    Valid,
    MissingOrInaccessible,
    Catalog
}

/// <summary>
/// Builds the visible folder hierarchy and validates browsing locations.
/// </summary>
public class FolderTreeService
{
    private string[] _excludedRoots = [];
    private readonly Func<string?> _picturesPathProvider;

    public FolderTreeService(string? catalogPath = null)
        : this(catalogPath, GetPicturesCandidatePath)
    {
    }

    internal FolderTreeService(
        string? catalogPath,
        Func<string?> picturesPathProvider)
    {
        UseExcludedRoots(catalogPath == null ? [] : [catalogPath]);
        _picturesPathProvider = picturesPathProvider;
    }

    public void UseExcludedRoots(IEnumerable<string> roots)
    {
        _excludedRoots = roots
            .Select(NormalizePath)
            .Where(path => path != null)
            .Cast<string>()
            .Distinct(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
            .ToArray();
    }

    public string? GetAvailablePicturesPath()
    {
        var picturesPath = _picturesPathProvider();
        return Directory.Exists(picturesPath) ? NormalizePath(picturesPath) : null;
    }

    private static string? GetPicturesCandidatePath()
    {
        var picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(picturesPath))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
            {
                return null;
            }

            picturesPath = Path.Combine(userProfile, "Pictures");
        }

        return picturesPath;
    }

    public FolderNode CreateRootNode(string path)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var node = new FolderNode(normalizedPath);
        LoadChildren(node);
        return node;
    }

    public void LoadChildren(FolderNode parentNode)
    {
        parentNode.Children.Clear();
        foreach (var childNode in GetChildFolders(parentNode.Path))
        {
            parentNode.Children.Add(childNode);
        }
    }

    public IEnumerable<FolderNode> GetChildFolders(string parentPath)
    {
        foreach (var directory in GetVisibleDirectoryPaths(parentPath)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var node = new FolderNode(directory);
            if (HasSubfolders(directory))
            {
                node.Children.Add(FolderNode.CreateDummy());
            }

            yield return node;
        }
    }

    public bool HasSubfolders(string path) => GetVisibleDirectoryPaths(path).Count > 0;

    public BrowseLocationValidation ValidateBrowseLocation(string? path)
    {
        var normalizedPath = NormalizePath(path);
        if (normalizedPath == null || !Directory.Exists(normalizedPath))
        {
            return BrowseLocationValidation.MissingOrInaccessible;
        }

        if (_excludedRoots.Any(root => IsSameOrDescendant(root, normalizedPath)))
        {
            return BrowseLocationValidation.Catalog;
        }

        try
        {
            _ = Directory.EnumerateFileSystemEntries(normalizedPath).Take(1).ToArray();
            return BrowseLocationValidation.Valid;
        }
        catch (UnauthorizedAccessException)
        {
            return BrowseLocationValidation.MissingOrInaccessible;
        }
        catch (IOException)
        {
            return BrowseLocationValidation.MissingOrInaccessible;
        }
    }

    public bool IsWithinRoot(string rootPath, string? candidatePath)
    {
        var normalizedRoot = NormalizePath(rootPath);
        var normalizedCandidate = NormalizePath(candidatePath);
        return normalizedRoot != null &&
               normalizedCandidate != null &&
               Directory.Exists(normalizedCandidate) &&
               IsSameOrDescendant(normalizedRoot, normalizedCandidate);
    }

    internal static bool IsSameOrDescendant(string rootPath, string candidatePath)
    {
        var relativePath = Path.GetRelativePath(rootPath, candidatePath);
        if (relativePath == ".")
        {
            return true;
        }

        return !Path.IsPathRooted(relativePath) &&
               relativePath != ".." &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private IReadOnlyList<string> GetVisibleDirectoryPaths(string parentPath)
    {
        if (!Directory.Exists(parentPath))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.EnumerateDirectories(parentPath)
                .Where(path => !IsHiddenOrSystem(path))
                .Select(Path.GetFullPath)
                .Where(path => !_excludedRoots.Any(root =>
                    IsSameOrDescendant(root, path)))
                .ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsHiddenOrSystem(string path)
    {
        try
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith('.'))
            {
                return true;
            }

            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Hidden) != 0 ||
                   (attributes & FileAttributes.System) != 0;
        }
        catch
        {
            return true;
        }
    }
}
