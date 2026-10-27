using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HappyPhoton.Tests;

internal sealed record RestoreCrashEntry(string Path, string Type, long? Length, string? Sha256);

internal sealed record RestoreCrashFingerprint(RestoreCrashEntry[] Entries)
{
    internal string Digest => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Entries))));

    internal static RestoreCrashFingerprint Read(string root)
    {
        var entries = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                var attributes = File.GetAttributes(path);
                Assert.False(attributes.HasFlag(FileAttributes.ReparsePoint), $"Linked fingerprint entry: {relative}");

                if (attributes.HasFlag(FileAttributes.Directory))
                    return new RestoreCrashEntry(relative, "directory", null, null);

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                return new RestoreCrashEntry(relative, "file", stream.Length,
                    Convert.ToHexString(SHA256.HashData(stream)));
            })
            .OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();

        return new(entries);
    }

    internal bool SameAs(RestoreCrashFingerprint other) => Entries.SequenceEqual(other.Entries);
}

public sealed class RestoreCrashFingerprintTests
{
    [Theory]
    [InlineData("thumbs")]
    [InlineData("previews")]
    [InlineData("rendered-thumbs")]
    [InlineData("tmp")]
    public void CacheTierBytes_AffectFingerprint(string tier)
    {
        using var first = new TemporaryDirectory();
        using var second = new TemporaryDirectory();

        foreach (var root in new[] { first.Path, second.Path })
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, "cache", "assets", tier));
            File.WriteAllText(Path.Combine(directory.FullName, "old"), "same");
        }

        Assert.True(RestoreCrashFingerprint.Read(first.Path).SameAs(RestoreCrashFingerprint.Read(second.Path)));
        File.WriteAllText(Path.Combine(second.Path, "cache", "assets", tier, "old"), "diff");
        Assert.False(RestoreCrashFingerprint.Read(first.Path).SameAs(RestoreCrashFingerprint.Read(second.Path)));
    }

    [Fact]
    public void EmptyDirectory_AffectsFingerprint()
    {
        using var first = new TemporaryDirectory();
        using var second = new TemporaryDirectory();
        Assert.True(RestoreCrashFingerprint.Read(first.Path).SameAs(RestoreCrashFingerprint.Read(second.Path)));
        Directory.CreateDirectory(Path.Combine(second.Path, "empty"));
        Assert.False(RestoreCrashFingerprint.Read(first.Path).SameAs(RestoreCrashFingerprint.Read(second.Path)));
    }
}
