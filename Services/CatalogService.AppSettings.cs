namespace HappyPhoton.Services;

public partial class CatalogService
{
    public async Task<string?> GetAppSettingAsync(string key)
    {
        EnsureInitialized();
        await _connectionGate.WaitAsync();
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT value FROM app_settings WHERE key = @key;";
            cmd.Parameters.AddWithValue("@key", key);
            var result = await cmd.ExecuteScalarAsync();
            return result as string;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task SetAppSettingAsync(string key, string? value)
    {
        EnsureInitialized();
        await _connectionGate.WaitAsync();
        try
        {
            using var cmd = _connection!.CreateCommand();
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
        finally
        {
            _connectionGate.Release();
        }
    }
}
