namespace HappyPhoton.Services;

public partial class CatalogService
{
    // Test catalogs are disposable, and a per-commit fsync dominates catalog-heavy tests.
    internal static bool SkipDurableSyncByDefaultForTests { get; set; }

    internal bool SkipDurableSyncForTests { get; set; } = SkipDurableSyncByDefaultForTests;

    internal async Task<string?> ReadPragmaForTestsAsync(string name)
    {
        EnsureInitialized();
        await _connectionGate.WaitAsync();
        try
        {
            using var command = _connection!.CreateCommand();
            command.CommandText = $"PRAGMA {name};";

            return (await command.ExecuteScalarAsync())?.ToString();
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async Task ApplyTestDurabilityAsync()
    {
        if (!SkipDurableSyncForTests) return;

        using var command = _connection!.CreateCommand();
        command.CommandText = "PRAGMA synchronous=OFF;";
        await command.ExecuteNonQueryAsync();
    }
}
