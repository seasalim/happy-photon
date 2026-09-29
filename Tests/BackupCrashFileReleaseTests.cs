using Xunit;

namespace HappyPhoton.Tests;

public sealed class BackupCrashFileReleaseTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("catalog.db")]
    [InlineData("Backups/held.partial.db")]
    [InlineData("presets/held.json")]
    public async Task WaitEndsWhenTheFileIsReleased(string relativePath)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows file-sharing behavior.");

        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "held");
        Task released;

        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            released = BackupCrashFileRelease.WaitAsync(directory.Path, 0, output);
            Assert.False(released.IsCompleted);
        }

        await released.WaitAsync(TestWaits.Condition);
        Assert.Equal("held", File.ReadAllText(path));
    }
}
