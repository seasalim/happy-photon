namespace HappyPhoton.Services;

public partial class CatalogService
{
    /// <summary>Gets an application setting value.</summary>
    public async Task<string?> GetAppSettingAsync(string key)
    {
        EnsureInitialized();
        return await GatedAsync(async connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM app_settings WHERE key = @key;";
            cmd.Parameters.AddWithValue("@key", key);
            var result = await cmd.ExecuteScalarAsync();
            return result as string;
        });
    }

    /// <summary>Gets every application setting in one query.</summary>
    public async Task<IReadOnlyDictionary<string, string?>> GetAppSettingsAsync()
    {
        EnsureInitialized();
        return await GatedAsync(async connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM app_settings;";
            using var reader = await cmd.ExecuteReaderAsync();
            var settings = new Dictionary<string, string?>(StringComparer.Ordinal);

            while (await reader.ReadAsync())
            {
                settings[reader.GetString(0)] = reader.GetValue(1) as string;
            }

            return settings;
        });
    }

    /// <summary>Sets an application setting value.</summary>
    public async Task SetAppSettingAsync(string key, string? value)
    {
        await SetAppSettingsAsync(new Dictionary<string, string?>
        {
            [key] = value
        });
    }

    /// <summary>Sets application setting values in one transaction.</summary>
    public async Task SetAppSettingsAsync(IReadOnlyDictionary<string, string?> settings)
    {
        EnsureInitialized();
        await GatedAsync(async connection =>
        {
            using var transaction = connection.BeginTransaction();

            foreach (var (key, value) in settings)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;

                if (value == null)
                {
                    cmd.CommandText = "DELETE FROM app_settings WHERE key = @key;";
                    cmd.Parameters.AddWithValue("@key", key);
                }
                else
                {
                    cmd.CommandText = @"
                    INSERT INTO app_settings (key, value) VALUES (@key, @value)
                    ON CONFLICT(key) DO UPDATE SET value = @value;
                ";
                    cmd.Parameters.AddWithValue("@key", key);
                    cmd.Parameters.AddWithValue("@value", value);
                }

                await cmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        });
    }
}
