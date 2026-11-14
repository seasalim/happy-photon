using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

public partial class CatalogService
{
    private async Task<T> GatedAsync<T>(
        Func<SqliteConnection, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        await _connectionGate.WaitAsync(cancellationToken);

        try
        {
            return await work(_connection!);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async Task GatedAsync(
        Func<SqliteConnection, Task> work,
        CancellationToken cancellationToken = default)
    {
        await _connectionGate.WaitAsync(cancellationToken);

        try
        {
            await work(_connection!);
        }
        finally
        {
            _connectionGate.Release();
        }
    }
}
