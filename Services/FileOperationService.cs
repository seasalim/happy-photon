using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;
using System.Runtime.InteropServices;

namespace HappyPhoton.Services;

/// <summary>
/// Service for platform-specific file operations like moving to trash.
/// </summary>
public class FileOperationService
{
    public async Task<bool> MoveToTrashAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return await MoveToTrashLinuxAsync(filePath);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return await MoveToTrashWindowsAsync(filePath);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return await MoveToTrashMacAsync(filePath);
        }

        return false;
    }

    private async Task<bool> MoveToTrashLinuxAsync(string filePath)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "gio",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("trash");
            process.StartInfo.ArgumentList.Add(filePath);

            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> MoveToTrashWindowsAsync(string filePath)
    {
        try
        {
            await Task.Run(() => FileSystem.DeleteFile(filePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> MoveToTrashMacAsync(string filePath)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "osascript",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("-e");
            process.StartInfo.ArgumentList.Add("on run argv");
            process.StartInfo.ArgumentList.Add("-e");
            process.StartInfo.ArgumentList.Add("tell application \"Finder\" to delete POSIX file (item 1 of argv)");
            process.StartInfo.ArgumentList.Add("-e");
            process.StartInfo.ArgumentList.Add("end run");
            process.StartInfo.ArgumentList.Add(filePath);

            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
