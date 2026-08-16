using System.Security.Cryptography;

namespace HappyPhoton.Tests;

internal sealed record NativeBinaryInfo(
    string Format,
    string Architecture,
    string? Identity,
    IReadOnlyList<string> Imports,
    IReadOnlyList<string> EncodedRequirements);

internal sealed record NativeDependencyInfo(
    string Name,
    string? ResolvedPath,
    string Classification,
    string? Sha256,
    NativeBinaryInfo? Binary);

internal static class NativeBinaryInspection
{
    public static NativeBinaryInfo Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        Span<byte> magic = stackalloc byte[4];
        if (stream.Read(magic) != magic.Length)
        {
            throw new InvalidDataException("Native binary header is truncated.");
        }

        stream.Position = 0;
        if (magic[0] == (byte)'M' && magic[1] == (byte)'Z')
        {
            return PeBinaryInspection.Inspect(stream);
        }

        if (magic.SequenceEqual(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' }))
        {
            return ElfBinaryInspection.Inspect(stream);
        }

        if (magic.SequenceEqual(new byte[] { 0xcf, 0xfa, 0xed, 0xfe }) ||
            magic.SequenceEqual(new byte[] { 0xfe, 0xed, 0xfa, 0xcf }))
        {
            return MachOBinaryInspection.Inspect(stream);
        }

        throw new InvalidDataException("Unsupported native binary format.");
    }

    public static IReadOnlyList<NativeDependencyInfo> Inventory(
        string rootPath,
        params string[] companionDirectories)
    {
        var directories = companionDirectories
            .Prepend(Path.GetDirectoryName(Path.GetFullPath(rootPath))!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var pending = new Queue<(string Name, string? Path, string Classification)>();
        pending.Enqueue((Path.GetFileName(rootPath), Path.GetFullPath(rootPath), "bundled"));
        var results = new List<NativeDependencyInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.TryDequeue(out var item))
        {
            var key = item.Path ?? item.Name;
            if (!seen.Add(key))
            {
                continue;
            }

            if (item.Path == null)
            {
                results.Add(new NativeDependencyInfo(
                    item.Name, null, item.Classification, null, null));
                continue;
            }

            var binary = Inspect(item.Path);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.Path)));
            results.Add(new NativeDependencyInfo(
                item.Name, item.Path, item.Classification, hash, binary));
            foreach (var import in binary.Imports)
            {
                var resolved = Resolve(import, directories);
                var classification = resolved != null
                    ? "bundled"
                    : IsOsProvided(import) ? "OS-provided" : "prerequisite";
                pending.Enqueue((import, resolved, classification));
            }
        }

        return results;
    }

    private static string? Resolve(string import, IReadOnlyList<string> directories)
    {
        var name = Path.GetFileName(import.Replace('@', Path.DirectorySeparatorChar));
        foreach (var directory in directories)
        {
            var exact = Path.Combine(directory, name);
            if (File.Exists(exact))
            {
                return Path.GetFullPath(exact);
            }

            var match = Directory.EnumerateFiles(directory)
                .FirstOrDefault(path => Path.GetFileName(path).Equals(
                    name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return Path.GetFullPath(match);
            }
        }

        return null;
    }

    private static bool IsOsProvided(string name)
    {
        var fileName = Path.GetFileName(name).ToLowerInvariant();
        return fileName.StartsWith("api-ms-win-") ||
            fileName.StartsWith("ext-ms-win-") ||
            fileName is "kernel32.dll" or "user32.dll" or "advapi32.dll" or
                "ws2_32.dll" or
                "ole32.dll" or "shell32.dll" or "vcruntime140.dll" or
                "vcruntime140_1.dll" or "msvcp140.dll" or "ucrtbase.dll" ||
            fileName is "libc.so.6" or "libm.so.6" or "libdl.so.2" or
                "libpthread.so.0" or "librt.so.1" or "ld-linux-x86-64.so.2" ||
            name.StartsWith("/usr/lib/", StringComparison.Ordinal) ||
            name.StartsWith("/System/Library/", StringComparison.Ordinal) ||
            name.StartsWith("@rpath/libSystem", StringComparison.Ordinal) ||
            name.StartsWith("@rpath/libc++", StringComparison.Ordinal);
    }
}
