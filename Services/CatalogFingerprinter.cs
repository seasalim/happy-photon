using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

internal static class CatalogFingerprinter
{
    public static async Task<CatalogFingerprint> FingerprintAsync(string root)
    {
        var database = Path.Combine(root, "catalog.db");
        var presets = Path.Combine(root, "presets");
        return new CatalogFingerprint(
            await RecoverAndCountRowsAsync(database),
            HashFile(Path.Combine(root, ".catalog-identity")),
            Directory.Exists(presets)
                ? Directory.EnumerateFiles(presets, "*.json")
                    .ToDictionary(
                        path => Path.GetFileName(path),
                        HashFile,
                        StringComparer.Ordinal)
                : new Dictionary<string, string>());
    }

    public static async Task VerifyAsync(string root, CatalogFingerprint expected)
    {
        var actual = await FingerprintAsync(root);
        if (actual.RowCount != expected.RowCount ||
            actual.IdentityHash != expected.IdentityHash ||
            actual.Presets.Count != expected.Presets.Count ||
            expected.Presets.Any(pair =>
                !actual.Presets.TryGetValue(pair.Key, out var hash) || hash != pair.Value))
        {
            throw new IOException("The copied catalog did not verify.");
        }
    }

    private static async Task<long> RecoverAndCountRowsAsync(string database)
    {
        await using var connection = new SqliteConnection(
            $"Data Source={database};Mode=ReadWrite;Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM images;";
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
