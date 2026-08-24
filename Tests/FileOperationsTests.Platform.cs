using System.Diagnostics;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

/// <summary>
/// Reveal and trash-policy cases, which are platform-shaped and split out to
/// keep the main file inside the repository's source-length limit.
/// </summary>
public sealed partial class FileOperationsTests
{
    [Fact]
    public async Task Reveal_UsesPlatformSpecificArgumentsAndSharedFolderLauncher()
    {
        var windows = FileOperationService.CreateRevealFileStartInfo(
            FileOperationPlatform.Windows, @"C:\Photos With Spaces\one.jpg");
        Assert.Equal("explorer.exe", windows.FileName);
        Assert.Equal(@"/select,""C:\Photos With Spaces\one.jpg""", windows.Arguments);
        Assert.Empty(windows.ArgumentList);

        var mac = FileOperationService.CreateRevealFileStartInfo(
            FileOperationPlatform.MacOS, "/photos/one.jpg");
        Assert.Equal("open", mac.FileName);
        Assert.Equal(["-R", "/photos/one.jpg"], mac.ArgumentList);

        var linux = FileOperationService.CreateRevealFileStartInfo(
            FileOperationPlatform.Linux, "/photos/one.jpg");
        Assert.Equal("xdg-open", linux.FileName);
        Assert.Equal(["/photos"], linux.ArgumentList);

        ProcessStartInfo? launched = null;
        var service = new FileOperationService(
            FileOperationPlatform.Windows,
            start =>
            {
                launched = start;
                return true;
            },
            _ => DriveType.Fixed);
        Assert.True(await service.OpenFolderAsync(_fx.Root));
        Assert.Equal("explorer.exe", launched!.FileName);
        Assert.Equal([_fx.Root], launched.ArgumentList);
    }

    [Fact]
    public async Task Reveal_DoesNotLaunchWhenFileNoLongerExists()
    {
        var launchCount = 0;
        var service = new FileOperationService(
            FileOperationPlatform.Windows,
            _ =>
            {
                launchCount++;
                return true;
            });

        Assert.False(await service.RevealFileAsync(_fx.Path("missing.jpg")));
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public void TrashGuard_RefusesUncNetworkAndRemovablePaths()
    {
        // AssessTrashPath rejects a path that is not fully qualified before it
        // reaches the Windows policy, and that check uses the host's path
        // semantics. On POSIX none of the drive-letter or UNC literals below are
        // fully qualified, so every case would return the not-local reason and
        // the policy would go unexercised. Production is unaffected: a real
        // Windows host evaluates them with Windows semantics.
        Assert.SkipWhen(
            !OperatingSystem.IsWindows(),
            "Windows trash policy needs Windows path semantics from the host.");

        var driveType = DriveType.Fixed;
        var service = new FileOperationService(
            FileOperationPlatform.Windows,
            _ => true,
            _ => driveType);

        var unc = service.AssessTrashPath(@"\\server\share\one.jpg");
        Assert.False(unc.IsSupported);
        Assert.Contains("Network", unc.Reason);

        driveType = DriveType.Network;
        var network = service.AssessTrashPath(@"N:\one.jpg");
        Assert.False(network.IsSupported);
        Assert.Contains("Network", network.Reason);

        driveType = DriveType.Removable;
        var removable = service.AssessTrashPath(@"E:\one.jpg");
        Assert.False(removable.IsSupported);
        Assert.Contains("removable", removable.Reason);

        foreach (var unsupported in new[]
                 { DriveType.Ram, DriveType.Unknown, DriveType.CDRom })
        {
            driveType = unsupported;
            var assessment = service.AssessTrashPath(@"X:\one.jpg");
            Assert.False(assessment.IsSupported);
            Assert.Contains("cannot be moved to Trash safely", assessment.Reason);
        }
    }

    [Theory]
    [InlineData((int)FileOperationPlatform.Linux)]
    [InlineData((int)FileOperationPlatform.MacOS)]
    public void TrashGuard_DoesNotApplyWindowsVolumePolicy(int platform)
    {
        var queried = false;
        var service = new FileOperationService((FileOperationPlatform)platform, _ => true, _ =>
        {
            queried = true;
            return DriveType.Removable;
        });
        Assert.True(service.AssessTrashPath(_fx.Path("one.jpg")).IsSupported);
        Assert.False(queried);
    }
}
