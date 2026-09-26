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
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0 && !IsVolumeMountPoint(current))
                throw new IOException($"Linked input is not permitted: {current.FullName}");
    }

    // A volume mounted into a folder (e.g. the ReFS dev volume at D:\Workspace) is a
    // reparse point too, but it is not a link to other content; symlinks and junctions are.
    // .NET reports no LinkTarget for volume mount points, so ask Windows directly: the call
    // succeeds only for a real volume mount point (not a junction or symlink).
    static bool IsVolumeMountPoint(FileSystemInfo entry)
    {
        if (!OperatingSystem.IsWindows() || entry is not DirectoryInfo) return false;
        var buffer = new char[64];
        return GetVolumeNameForVolumeMountPointW(
            Path.TrimEndingDirectorySeparator(entry.FullName) + "\\", buffer, (uint)buffer.Length);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, char[] volumeName, uint length);

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
