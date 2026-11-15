using System.Diagnostics;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class MagickSecurityPolicyTests
{
    [Fact]
    public void DefaultDirectory_IsUnderPerUserLocalApplicationData()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        Assert.True(Path.IsPathFullyQualified(local));
        var directory = MagickSecurityPolicy.GetConfigurationDirectory();

        Assert.Equal(Path.Combine("Happy Photon", $"magick-{MagickNET.Version}-profiles-32"),
            Path.GetRelativePath(local, directory));
        Assert.NotEqual(directory, MagickSecurityPolicy.GetConfigurationDirectory(OpenMpStartup.ConfigurationRoot));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative-data")]
    public void InvalidRoot_DoesNotFallBackToWorkingDirectory(string root) =>
        Assert.Throws<InvalidOperationException>(() => MagickSecurityPolicy.GetConfigurationDirectory(root));

    [Fact]
    public async Task SeparateUserDataRoots_KeepIndependentConfigurations()
    {
        using var first = new TemporaryDirectory();
        using var second = new TemporaryDirectory();
        await Task.WhenAll(RunProcessAsync(first.Path), RunProcessAsync(second.Path));
        var firstPolicy = Assert.Single(Directory.GetFiles(first.Path, "policy.xml", SearchOption.AllDirectories));
        var secondPolicy = Assert.Single(Directory.GetFiles(second.Path, "policy.xml", SearchOption.AllDirectories));

        Assert.NotEqual(firstPolicy, secondPolicy);
        Assert.Equal(File.ReadAllText(firstPolicy), File.ReadAllText(secondPolicy));
    }

    [Fact]
    public async Task UnixDirectory_IsOwnerOnlyOnCreationAndReuse()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix directory permissions are exercised on Linux/macOS.");
            return;
        }

        using var root = new TemporaryDirectory();
        await RunProcessAsync(root.Path);
        var directory = Path.GetDirectoryName(Assert.Single(
            Directory.GetFiles(root.Path, "policy.xml", SearchOption.AllDirectories)))!;
        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Assert.Equal(ownerOnly, File.GetUnixFileMode(directory));
        File.SetUnixFileMode(directory, ownerOnly | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite);
        await RunProcessAsync(root.Path);

        Assert.Equal(ownerOnly, File.GetUnixFileMode(directory));
    }

    [Fact]
    public async Task RepeatedInitialization_ReusesDirectoryWithoutRewritingFiles()
    {
        using var root = new TemporaryDirectory();
        await RunProcessAsync(root.Path);
        var policy = Assert.Single(Directory.GetFiles(root.Path, "policy.xml", SearchOption.AllDirectories));
        var directory = Path.GetDirectoryName(policy)!;
        var files = Directory.GetFiles(directory).Order().ToArray();
        Assert.Equal(11, files.Length);
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        foreach (var file in files)
        {
            File.SetLastWriteTimeUtc(file, stamp);
        }

        var folders = Directory.GetDirectories(root.Path, "*", SearchOption.AllDirectories).Order().ToArray();
        await RunProcessAsync(root.Path);

        Assert.Equal(folders, Directory.GetDirectories(root.Path, "*", SearchOption.AllDirectories).Order());
        Assert.Equal(files, Directory.GetFiles(directory).Order());
        Assert.All(files, file => Assert.Equal(stamp, File.GetLastWriteTimeUtc(file)));
        Assert.Contains("name=\"max-profile-count\" value=\"32\"", File.ReadAllText(policy));
    }

    [Fact]
    public async Task Initialization_RepairsInterruptedPolicyWrite()
    {
        using var root = new TemporaryDirectory();
        await RunProcessAsync(root.Path);
        var policy = Assert.Single(Directory.GetFiles(root.Path, "policy.xml", SearchOption.AllDirectories));
        var expected = File.ReadAllText(policy);
        File.WriteAllText(policy, "<policymap>");
        File.WriteAllText(policy + ".tmp", "interrupted");
        await RunProcessAsync(root.Path);

        Assert.Equal(expected, File.ReadAllText(policy));
        Assert.False(File.Exists(policy + ".tmp"));
    }

    [Fact]
    public async Task ConcurrentProcesses_ShareCompleteConfiguration()
    {
        using var root = new TemporaryDirectory();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RunProcessAsync(root.Path)));
        var policy = Assert.Single(Directory.GetFiles(root.Path, "policy.xml", SearchOption.AllDirectories));

        Assert.Equal(11, Directory.GetFiles(Path.GetDirectoryName(policy)!).Length);
        Assert.Contains("name=\"max-profile-count\" value=\"32\"", File.ReadAllText(policy));
    }

    internal static void RunChild()
    {
        MagickSecurityPolicy.Initialize(OpenMpStartup.ConfigurationRoot);
        using var image = new MagickImage(MagickColors.Gray, 1, 1);
        Console.WriteLine("MAGICK_CONFIGURATION_VERIFIED");
    }

    private static async Task RunProcessAsync(string temporaryRoot)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(MagickSecurityPolicyTests).Assembly.Location);
        start.ArgumentList.Add("--magick-policy-child");
        start.Environment[OpenMpStartup.ConfigurationRootEnvironmentVariable] = temporaryRoot;
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TestWaits.Condition);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        var output = await outputTask;
        var error = await errorTask;
        Assert.True(process.ExitCode == 0, $"{output}\n{error}");
        Assert.Contains("MAGICK_CONFIGURATION_VERIFIED", output);
    }
}
