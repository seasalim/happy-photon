using Xunit;

namespace HappyPhoton.Tests;

public sealed class NativeBinaryInspectionTests
{
    [Fact]
    public void RestoredWindowsRuntime_ReportsImportsAndRecursiveCompanions()
    {
        var path = RuntimePackageFile(
            "happyphoton.libraw.native", "win-x64", "raw_r.dll");
        var directory = Path.GetDirectoryName(path)!;

        var binary = NativeBinaryInspection.Inspect(path);
        var inventory = NativeBinaryInspection.Inventory(path, directory);

        Assert.Equal("PE", binary.Format);
        Assert.Equal("x86-64", binary.Architecture);
        Assert.Contains(binary.Imports, name =>
            name.Equals("jpeg8.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(binary.Imports, name =>
            name.Equals("lcms2-2.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(binary.Imports, name =>
            name.Equals("z.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(inventory, item => item.Name.Equals(
            "jpeg8.dll", StringComparison.OrdinalIgnoreCase) &&
            item.ResolvedPath != null);
        Assert.Contains(inventory, item => item.Classification == "OS-provided");
    }

    [Fact]
    public void RestoredLinuxRuntime_ReportsSonameImportsAndSymbolRequirements()
    {
        var path = RuntimePackageFile(
            "happyphoton.libraw.native", "linux-x64", "libraw_r.so.25");
        var directory = Path.GetDirectoryName(path)!;

        var binary = NativeBinaryInspection.Inspect(path);
        var inventory = NativeBinaryInspection.Inventory(path, directory);

        Assert.Equal("ELF64", binary.Format);
        Assert.Equal("x86-64", binary.Architecture);
        Assert.Equal("libraw_r.so.25", binary.Identity);
        Assert.Contains("libjpeg.so.8", binary.Imports);
        Assert.Contains("liblcms2.so.2", binary.Imports);
        Assert.Contains("libgomp.so.1", binary.Imports);
        Assert.Contains(binary.EncodedRequirements, value => value.Contains("GLIBC_"));
        Assert.Contains(inventory, item => item.Name == "libgomp.so.1" &&
            item.Classification == "prerequisite");
    }

    [Fact]
    public void CheckedInMacRuntime_ReportsInstallNameImportsAndMinimumOs()
    {
        var path = RuntimePackageFile(
            "happyphoton.libraw.native", "osx-arm64", "libraw.25.dylib");

        var binary = NativeBinaryInspection.Inspect(path);
        var inventory = NativeBinaryInspection.Inventory(path);

        Assert.Equal("Mach-O 64", binary.Format);
        Assert.Equal("arm64", binary.Architecture);
        Assert.Equal("@loader_path/libraw.25.dylib", binary.Identity);
        Assert.Contains(binary.Imports, value => value.Contains("libSystem"));
        Assert.Contains(binary.EncodedRequirements, value =>
            value.Contains("macOS minimum 13.0.0", StringComparison.Ordinal));
        Assert.All(inventory.Skip(1), item => Assert.Equal("OS-provided", item.Classification));
    }

    [Theory]
    [InlineData("PE")]
    [InlineData("ELF")]
    [InlineData("MachO")]
    public void MalformedBinary_IsRejected(string format)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bad-native-{Guid.NewGuid():N}.bin");
        try
        {
            var header = format switch
            {
                "PE" => new byte[] { (byte)'M', (byte)'Z', 0, 0 },
                "ELF" => [0x7f, (byte)'E', (byte)'L', (byte)'F'],
                _ => [0xcf, 0xfa, 0xed, 0xfe]
            };
            File.WriteAllBytes(path, header);
            Assert.Throws<InvalidDataException>(() => NativeBinaryInspection.Inspect(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    internal static string RuntimePackageFile(string package, string rid, string name)
    {
        var root = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages");
        }

        var path = Path.Combine(root, package, "0.22.2.11", "runtimes", rid, "native", name);
        Assert.True(File.Exists(path), $"Restored runtime fixture is missing: {path}");
        return path;
    }
}
