using System.Text.Json;
using Xunit;

namespace HappyPhoton.Tests;

internal sealed record BackupKillPointMap(string[] Kept, Dictionary<string, string> Pruned)
{
    internal void Validate(string name, string[] discovered, bool requireOrder)
    {
        var points = Kept.Concat(Pruned.Keys).ToArray();
        Assert.NotEmpty(Kept);
        Assert.Equal(points.Length, points.Distinct(StringComparer.Ordinal).Count());

        foreach (var point in discovered)
        {
            Assert.True(points.Contains(point, StringComparer.Ordinal),
                $"Unclassified {name} point: {point}");
        }

        foreach (var point in points)
        {
            Assert.True(discovered.Contains(point, StringComparer.Ordinal),
                $"{name} map point not discovered: {point}");
        }

        foreach (var (point, equivalent) in Pruned)
        {
            Assert.True(Kept.Contains(equivalent, StringComparer.Ordinal),
                $"{name}: {point} must map to a kept point: {equivalent}");

            if (requireOrder)
            {
                Assert.True(Array.IndexOf(discovered, equivalent) < Array.IndexOf(discovered, point),
                    $"{name}: {equivalent} must precede {point}");
            }
        }
    }
}

internal sealed class BackupKillPointMatrix
{
    internal const int MovePartCount = 4;

    internal static readonly BackupKillPointMatrix Backup = new("BackupCrash", BackupCrashDriver.DiscoverAsync);

    internal static readonly BackupKillPointMatrix Move = new("MoveBackupCrash", MoveBackupCrashDriver.DiscoverAsync);

    internal static bool FullMatrix => Environment.GetEnvironmentVariable("HAPPY_PHOTON_BACKUP_FULL_MATRIX") == "1";

    // Equivalence is proven only for the ordinary fixture on Windows.
    internal static bool UseMap => OperatingSystem.IsWindows() &&
        Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1";

    private readonly object _sync = new();

    private readonly Func<BackupCatalogFixtures, Task<string[]>> _discover;

    private Lazy<Task<string[]>>? _discovered;

    internal string Name { get; }

    internal BackupKillPointMap Map { get; }

    internal string PathName => Name + "/" +
        (Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") == "1" ? "perf" : "ordinary");

    private BackupKillPointMatrix(string name, Func<BackupCatalogFixtures, Task<string[]>> discover)
    {
        Name = name;
        _discover = discover;
        var file = Path.Combine(AppContext.BaseDirectory, name + "Equivalence.json");
        Map = JsonSerializer.Deserialize<Dictionary<string, BackupKillPointMap>>(File.ReadAllText(file),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!["ordinary"];
    }

    internal IEnumerable<object[]> Rows(int part = 0, int partCount = 1) => Map.Kept
        .Where((_, index) => index % partCount == part)
        .Select(point => new object[] { point });

    internal Task<string[]> DiscoverAsync(BackupCatalogFixtures fixtures)
    {
        lock (_sync)
        {
            _discovered ??= new(() => _discover(fixtures));

            return _discovered.Value;
        }
    }

    internal IEnumerable<string> Targets(string kept, string[] discovered)
    {
        Map.Validate(Name, discovered, requireOrder: UseMap);
        Assert.Contains(kept, Map.Kept);
        yield return kept;

        if (!FullMatrix && UseMap) yield break;

        foreach (var point in discovered.Where(point => Map.Pruned.GetValueOrDefault(point) == kept))
        {
            yield return point;
        }
    }
}
