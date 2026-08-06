using HappyPhoton.Models;

namespace HappyPhoton.Services;

public partial class CatalogService
{
    /// <summary>Creates missing image records in batches and loads their state.</summary>
    public async Task<IReadOnlyDictionary<string, CatalogImageState>> LoadOrCreateImageStatesAsync(
        IReadOnlyCollection<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var paths = filePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existingStates = await LoadImageStatesAsync(paths, cancellationToken);
        var missingPaths = paths.Where(path => !existingStates.ContainsKey(path)).ToArray();
        const int batchSize = 300;

        for (var offset = 0; offset < missingPaths.Length; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _connectionGate.WaitAsync(cancellationToken);
            try
            {
                var count = Math.Min(batchSize, missingPaths.Length - offset);
                using var cmd = _connection!.CreateCommand();
                var values = new string[count];

                for (var index = 0; index < count; index++)
                {
                    var pathParameter = $"@path{index}";
                    var nameParameter = $"@name{index}";
                    values[index] =
                        $"({pathParameter}, {nameParameter}, " +
                        "@editSettings, @editVersion, @updated)";
                    cmd.Parameters.AddWithValue(pathParameter, missingPaths[offset + index]);
                    cmd.Parameters.AddWithValue(nameParameter, Path.GetFileName(missingPaths[offset + index]));
                }

                cmd.Parameters.AddWithValue(
                    "@editSettings",
                    DefaultEditSettingsJson);
                cmd.Parameters.AddWithValue(
                    "@editVersion",
                    EditSettings.CurrentVersion);
                cmd.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
                cmd.CommandText = $@"
                    INSERT INTO images (
                        file_path, file_name, edit_settings, edit_version, updated_utc)
                    VALUES {string.Join(", ", values)}
                    ON CONFLICT(file_path) DO NOTHING;
                ";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _connectionGate.Release();
            }
        }

        return missingPaths.Length == 0
            ? existingStates
            : await LoadImageStatesAsync(paths, cancellationToken);
    }
}
