using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Service for building and navigating the folder tree hierarchy.
/// </summary>
public class FolderTreeService
{
    /// <summary>
    /// Gets the user's Pictures folder as the root node with immediate children loaded.
    /// </summary>
    public FolderNode GetPicturesFolderNode()
    {
        var picturesPath = GetPicturesPath();
        var node = new FolderNode(picturesPath);

        // Load immediate children
        LoadChildren(node);

        return node;
    }

    /// <summary>
    /// Gets the path to the user's Pictures folder, with cross-platform fallback.
    /// </summary>
    public string GetPicturesPath()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

        // Fallback if MyPictures is empty (can happen on some Linux systems)
        if (string.IsNullOrEmpty(pictures))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            pictures = Path.Combine(home, "Pictures");

            // If Pictures doesn't exist, just use home
            if (!Directory.Exists(pictures))
            {
                pictures = home;
            }
        }

        return pictures;
    }

    /// <summary>
    /// Loads immediate child folders into the given folder node.
    /// </summary>
    public void LoadChildren(FolderNode parentNode)
    {
        parentNode.Children.Clear();

        foreach (var childNode in GetChildFolders(parentNode.Path))
        {
            parentNode.Children.Add(childNode);
        }
    }

    /// <summary>
    /// Gets child folders for the given parent path.
    /// Each child has a dummy node if it has subfolders (for lazy loading).
    /// </summary>
    public IEnumerable<FolderNode> GetChildFolders(string parentPath)
    {
        if (!Directory.Exists(parentPath))
            yield break;

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(parentPath)
                .Where(d => !IsHiddenOrSystem(d))
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var dir in directories)
        {
            var node = new FolderNode(dir);

            // Add dummy child if this folder has subfolders (for lazy loading indicator)
            if (HasSubfolders(dir))
            {
                node.Children.Add(FolderNode.CreateDummy());
            }

            yield return node;
        }
    }

    /// <summary>
    /// Checks if the given folder has any subfolders.
    /// </summary>
    public bool HasSubfolders(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path)
                .Where(d => !IsHiddenOrSystem(d))
                .Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a directory is hidden or a system folder.
    /// </summary>
    private static bool IsHiddenOrSystem(string path)
    {
        try
        {
            var name = Path.GetFileName(path);

            // Skip hidden folders (starting with .)
            if (name.StartsWith('.'))
                return true;

            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.Hidden) != 0 ||
                   (attrs & FileAttributes.System) != 0;
        }
        catch
        {
            return true; // Skip if we can't read attributes
        }
    }
}
