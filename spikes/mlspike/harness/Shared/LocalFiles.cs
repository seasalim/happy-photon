using System.Security.Cryptography;
using HappyPhoton.Services;

namespace HappyPhoton.MlSpike;

public static class LocalFiles
{
    public static void Check(string path)
    {
        if (!SourceAccessPolicy.CanRead(new SourceAvailabilityService().GetAvailability(path),
                SourceReadIntent.Background))
            throw new IOException($"Input is not locally readable: {path}");
        for (FileSystemInfo? current = new FileInfo(Path.GetFullPath(path));
             current != null; current = Directory.GetParent(current.FullName))
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Linked input is not permitted: {current.FullName}");
    }

    public static byte[] Read(string path)
    {
        Check(path);
        return File.ReadAllBytes(path);
    }

    public static string Hash(string path)
    {
        Check(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static string Resolve(string root, string relative)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(fullRoot + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Manifest path escapes sample root.");
        Check(path);
        return path;
    }

    public static FileStream Create(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        Check(Path.GetDirectoryName(full)!);
        // CreateNew refuses collisions with originals, existing results, and hard links.
        return new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }
}
