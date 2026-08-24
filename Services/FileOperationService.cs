using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;

namespace HappyPhoton.Services;

public readonly record struct TrashPathAssessment(bool IsSupported, string? Reason);

internal interface IFileOperationService
{
    TrashPathAssessment AssessTrashPath(string path);
    Task<bool> MoveToTrashAsync(string filePath);
    Task<bool> RevealFileAsync(string filePath);
    Task<bool> OpenFolderAsync(string folderPath);
}

/// <summary>
/// Service for platform-specific file operations that preserve recoverability.
/// </summary>
public sealed class FileOperationService : IFileOperationService
{
    private readonly Func<ProcessStartInfo, bool> _launch;
    private readonly Func<string, DriveType> _getDriveType;
    private readonly FileOperationPlatform _platform;

    public FileOperationService()
        : this(
            CurrentPlatform,
            start => Process.Start(start) != null,
            root => new DriveInfo(root).DriveType)
    {
    }

    internal FileOperationService(
        FileOperationPlatform platform,
        Func<ProcessStartInfo, bool> launch,
        Func<string, DriveType>? getDriveType = null)
    {
        _platform = platform;
        _launch = launch;
        _getDriveType = getDriveType ?? (root => new DriveInfo(root).DriveType);
    }

    public TrashPathAssessment AssessTrashPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return new TrashPathAssessment(false, "The file path is not local.");
        }

        if (_platform != FileOperationPlatform.Windows)
            return new TrashPathAssessment(true, null);

        if (path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return new TrashPathAssessment(
                false, "Network files cannot be moved to Trash safely.");
        }

        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root))
            {
                return new TrashPathAssessment(
                    false, "The file's storage volume could not be identified.");
            }

            var driveType = _getDriveType(root);
            if (driveType != DriveType.Fixed)
            {
                return new TrashPathAssessment(
                    false, driveType switch
                    {
                        DriveType.Network =>
                            "Network files cannot be moved to Trash safely.",
                        DriveType.Removable =>
                            "Files on removable media cannot be moved to Trash safely.",
                        _ => "Files on this storage volume cannot be moved to Trash safely."
                    });
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new TrashPathAssessment(
                false, "The file's storage volume could not be identified.");
        }

        return new TrashPathAssessment(true, null);
    }

    public async Task<bool> MoveToTrashAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;

        return _platform switch
        {
            FileOperationPlatform.Linux => await RunAsync("gio", "trash", filePath),
            FileOperationPlatform.Windows => await MoveToTrashWindowsAsync(filePath),
            FileOperationPlatform.MacOS => await RunAsync(
                "osascript",
                "-e", "on run argv",
                "-e", "tell application \"Finder\" to delete POSIX file (item 1 of argv)",
                "-e", "end run",
                filePath),
            _ => false
        };
    }

    public Task<bool> RevealFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return Task.FromResult(false);

        return Task.FromResult(TryLaunch(CreateRevealFileStartInfo(
            _platform, filePath)));
    }

    public Task<bool> OpenFolderAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return Task.FromResult(false);

        return Task.FromResult(TryLaunch(CreateOpenFolderStartInfo(
            _platform, folderPath)));
    }

    internal static ProcessStartInfo CreateRevealFileStartInfo(
        FileOperationPlatform platform,
        string filePath)
    {
        var start = CreateFileManagerStartInfo(platform);
        if (platform == FileOperationPlatform.Windows)
            start.Arguments = $"/select,\"{filePath}\"";
        else if (platform == FileOperationPlatform.MacOS)
        {
            start.ArgumentList.Add("-R");
            start.ArgumentList.Add(filePath);
        }
        else
        {
            var separator = filePath.LastIndexOf('/');
            start.ArgumentList.Add(separator > 0
                ? filePath[..separator]
                : Path.GetDirectoryName(filePath) ?? filePath);
        }
        return start;
    }

    internal static ProcessStartInfo CreateOpenFolderStartInfo(
        FileOperationPlatform platform,
        string folderPath)
    {
        var start = CreateFileManagerStartInfo(platform);
        start.ArgumentList.Add(folderPath);
        return start;
    }

    private static ProcessStartInfo CreateFileManagerStartInfo(
        FileOperationPlatform platform) => new(platform switch
        {
            FileOperationPlatform.Windows => "explorer.exe",
            FileOperationPlatform.MacOS => "open",
            _ => "xdg-open"
        }) { UseShellExecute = false };

    private bool TryLaunch(ProcessStartInfo start)
    {
        try
        {
            return _launch(start);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> RunAsync(
        string executable,
        params string[] arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> MoveToTrashWindowsAsync(string filePath)
    {
        try
        {
            await Task.Run(() => FileSystem.DeleteFile(
                filePath,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static FileOperationPlatform CurrentPlatform =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? FileOperationPlatform.Windows
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? FileOperationPlatform.MacOS
                : FileOperationPlatform.Linux;
}

internal enum FileOperationPlatform
{
    Windows,
    MacOS,
    Linux
}
