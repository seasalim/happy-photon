using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HappyPhoton.Tests;

internal sealed record KillPointEntry(string Path, string Type, long? Length, string? Sha256);

internal sealed record KillPointFingerprint(KillPointEntry[] Entries)
{
    internal string Digest => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Entries))));

    internal static KillPointFingerprint Read(string root)
    {
        var entries = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                var attributes = File.GetAttributes(path);
                Assert.False(attributes.HasFlag(FileAttributes.ReparsePoint), $"Linked fingerprint entry: {relative}");

                if (attributes.HasFlag(FileAttributes.Directory))
                    return new KillPointEntry(relative, "directory", null, null);

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                return new KillPointEntry(relative, "file", stream.Length,
                    Convert.ToHexString(SHA256.HashData(stream)));
            })
            .OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();

        return new(entries);
    }

    internal bool SameAs(KillPointFingerprint other) => Entries.SequenceEqual(other.Entries);
}

