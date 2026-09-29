using System.Collections;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Tests;

// Read-only reflection is diagnostic only; a provider layout change reports unavailable.
internal static class BackupCrashSqliteDiagnostics
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static string Snapshot(string database)
    {
        const string policy = "clear_all_pools=not-called (process-wide clearing forbidden; baseline is observational)";

        try
        {
            var type = typeof(SqliteConnection).Assembly.GetType("Microsoft.Data.Sqlite.SqliteConnectionFactory", true)!;
            var factory = type.GetField("Instance", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
            var gate = (ReaderWriterLockSlim)Field(factory, "_lock")!;
            if (!gate.TryEnterReadLock(100)) return $"{policy}\nsqlite_pool_state=unavailable (factory busy)";

            var pools = new HashSet<object>(ReferenceEqualityComparer.Instance);

            try
            {
                foreach (DictionaryEntry entry in (IDictionary)Field(factory, "_poolGroups")!)
                {
                    AddPool(entry.Value!, pools);
                }

                foreach (var group in (IEnumerable)Field(factory, "_idlePoolGroups")!)
                {
                    AddPool(group, pools);
                }
            }
            finally
            {
                gate.ExitReadLock();
            }

            var released = (IEnumerable)Field(factory, "_poolsToRelease")!;

            lock (released)
            {
                foreach (var pool in released) pools.Add(pool);
            }

            var matching = 0;
            var total = 0;
            var idle = 0;

            foreach (var pool in pools)
            {
                var options = (SqliteConnectionStringBuilder)Field(pool, "_connectionOptions")!;
                if (!string.Equals(Path.GetFullPath(options.DataSource), Path.GetFullPath(database),
                    StringComparison.OrdinalIgnoreCase)) continue;

                var connections = (ICollection)Field(pool, "_connections")!;

                lock (connections)
                {
                    matching++;
                    total += connections.Count;
                    idle += (int)Field(pool, "_warmPool")!.GetType().GetProperty("Count")!
                        .GetValue(Field(pool, "_warmPool"))!;
                    idle += (int)Field(pool, "_coldPool")!.GetType().GetProperty("Count")!
                        .GetValue(Field(pool, "_coldPool"))!;
                }
            }

            return $"{policy}\nsqlite_pool_state=observed file={database} matching_pools={matching} " +
                $"connections={total} idle={idle} checked_out={total - idle} " +
                "nonpooled_handles=not-enumerated";
        }
        catch (Exception ex)
        {
            return $"{policy}\nsqlite_pool_state=unavailable error={ex.GetType().Name}: {ex.Message}";
        }
    }

    private static void AddPool(object group, HashSet<object> pools)
    {
        if (Field(group, "_pool") is { } pool) pools.Add(pool);
    }

    private static object? Field(object instance, string name) =>
        (instance.GetType().GetField(name, Fields) ?? throw new MissingFieldException(name)).GetValue(instance);
}
