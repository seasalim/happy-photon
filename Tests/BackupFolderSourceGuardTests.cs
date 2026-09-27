using System.Text.RegularExpressions;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BackupFolderSourceGuardTests
{
    [Fact]
    public void ProductionSources_ObtainBackupFolderOnlyThroughCountedSeam()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "HappyPhoton.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var violations = Sources(root.FullName)
            .Where(path => Path.GetRelativePath(root.FullName, path).Replace('\\', '/') !=
                "Services/CatalogBackupService.cs")
            .Where(path => RefersToBackupFolder(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root.FullName, path));
        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("Path.Combine(root, \"Backups\")")]
    [InlineData("Directory.Exists(@\"C:\\catalog\\Backups\")")]
    [InlineData("File.OpenRead(\"/catalog/backups/one.zip\")")]
    public void GuardDetectsFolderReferences(string source) =>
        Assert.True(RefersToBackupFolder(source));

    private static bool RefersToBackupFolder(string source) =>
        Regex.IsMatch(source, @"\bBackups\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IEnumerable<string> Sources(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
            if (Path.GetExtension(file) is ".cs" or ".axaml" or ".xaml" or ".cpp" or ".h")
                yield return file;
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (name.StartsWith('.') || name is "bin" or "obj" or "Tests" or "HeadlessTests" or
                "spikes" or "node_modules" || name.EndsWith(".Tests", StringComparison.Ordinal))
                continue;
            foreach (var file in Sources(child)) yield return file;
        }
    }
}
