using System.Collections.Concurrent;
using System.Globalization;
using HappyPhoton.LibRaw.Interop;

namespace HappyPhoton.Services;

internal sealed class LensIdentityResolver
{
    private static readonly ConcurrentDictionary<string,
        Lazy<IReadOnlyDictionary<ulong, string>>> Tables = new(
            StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string,
        Lazy<IReadOnlyDictionary<string, string>>> Aliases = new();
    private readonly string _tableDirectory;

    internal string? ResolveAlias(string? name, string? make = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = LensfunDatabase.Normalize(name);
        var maker = LensfunDatabase.Normalize(make ?? string.Empty);
        if (maker.Length > 0 && name.StartsWith(maker, StringComparison.Ordinal))
            name = name[maker.Length..];
        try
        {
            var path = Path.Combine(_tableDirectory, "same-optics.tsv");
            return Aliases.GetOrAdd(path, value => new(() => LoadAliases(value)))
                .Value.GetValueOrDefault(name);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, string> LoadAliases(string path)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var fields = line.Split('\t');
            if (fields.Length != 2 || fields.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Same-optics alias table is invalid.");
            var source = LensfunDatabase.Normalize(fields[0]);
            var target = fields[1].Trim();
            if (source == LensfunDatabase.Normalize(target)) continue;
            aliases.Add(source, target);
        }
        return aliases;
    }

    internal LensIdentityResolver() : this(
        Path.Combine(PackagedDataRoot.Resolve(), "data", "lens-ids"))
    {
    }

    internal LensIdentityResolver(string tableDirectory)
    {
        _tableDirectory = tableDirectory;
    }

    internal string? Resolve(string? make, LibRawLensIdentity? identity)
        => ResolveCandidates(make, identity).FirstOrDefault();

    internal IEnumerable<string> ResolveCandidates(
        string? make,
        LibRawLensIdentity? identity)
    {
        if (identity == null) yield break;
        var transmitted = string.IsNullOrWhiteSpace(identity.Lens)
            ? null
            : identity.Lens.Trim();
        if (transmitted != null) yield return transmitted;
        var derived = ResolveId(make, identity);
        if (derived != null && !string.Equals(
            transmitted, derived, StringComparison.Ordinal))
            yield return derived;
    }

    private string? ResolveId(string? make, LibRawLensIdentity identity)
    {
        if (identity.LensMount != LibRawLensMounts.NikonF ||
            identity.LensId == 0 || string.IsNullOrWhiteSpace(make))
            return null;
        var tableName = LensfunDatabase.Normalize(make).ToLowerInvariant();
        if (tableName.Length == 0) return null;
        try
        {
            var path = Path.Combine(_tableDirectory, $"{tableName}.tsv");
            var table = Tables.GetOrAdd(path, value => new(
                () => LoadTable(value),
                LazyThreadSafetyMode.ExecutionAndPublication));
            return table.Value.GetValueOrDefault(identity.LensId);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<ulong, string> LoadTable(string path)
    {
        var candidates = new Dictionary<ulong, HashSet<string>>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var fields = line.Split('\t');
            if (fields.Length != 2 || fields[0].Length != 16 ||
                !ulong.TryParse(fields[0], NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out var id) ||
                string.IsNullOrWhiteSpace(fields[1]))
                throw new InvalidDataException("Lens identity table is invalid.");
            if (!candidates.TryGetValue(id, out var names))
                candidates[id] = names = new HashSet<string>(StringComparer.Ordinal);
            names.Add(fields[1].Trim());
        }
        return candidates
            .Where(item => item.Value.Count == 1 &&
                !item.Value.Single().Contains(" or ", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(item => item.Key, item => item.Value.Single());
    }
}
