using HappyPhoton.Models;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

public sealed class LightroomCatalogReader
{
    internal const string SnapshotDirectoryPrefix = "happy-photon-import-";
    private static readonly string[] CoreTables =
    [
        "Adobe_images", "AgLibraryFile", "AgLibraryFolder",
        "AgLibraryRootFolder", "Adobe_variablesTable"
    ];

    public async Task<LightroomCatalogContents> ReadAsync(
        string catalogPath,
        IReadOnlyDictionary<ColorLabel, string>? colorLabelNames = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(catalogPath);
        if (!File.Exists(fullPath) ||
            !string.Equals(Path.GetExtension(fullPath), ".lrcat",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Choose a Lightroom Classic .lrcat catalog.");
        }

        if (File.Exists(fullPath + ".lock"))
        {
            throw new IOException("Close Lightroom before importing this catalog.");
        }
        if (File.Exists(fullPath + "-wal") ||
            File.Exists(fullPath + "-shm") ||
            File.Exists(fullPath + "-journal"))
        {
            throw new IOException(
                "Close Lightroom completely before importing. This catalog still has active SQLite sidecars.");
        }

        var snapshotDirectory = Path.Combine(
            Path.GetTempPath(), SnapshotDirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(snapshotDirectory);
        var snapshotPath = Path.Combine(snapshotDirectory, "catalog.snapshot");
        try
        {
            await Task.Run(() => CreateClosedCatalogSnapshot(fullPath, snapshotPath));
            cancellationToken.ThrowIfCancellationRequested();
            return await ReadSnapshotAsync(
                fullPath, snapshotPath,
                colorLabelNames ?? ColorLabelNames.Defaults,
                cancellationToken);
        }
        finally
        {
            TryDeleteSnapshotDirectory(snapshotDirectory);
        }
    }

    internal static void SweepOrphanedSnapshots()
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(
                         Path.GetTempPath(), SnapshotDirectoryPrefix + "*"))
            {
                var name = Path.GetFileName(directory);
                var suffix = name[SnapshotDirectoryPrefix.Length..];
                if (suffix.Length == 32 && suffix.All(Uri.IsHexDigit) &&
                    Directory.GetCreationTimeUtc(directory) <
                    DateTime.UtcNow.Subtract(TimeSpan.FromHours(1)))
                    TryDeleteSnapshotDirectory(directory);
            }
        }
        catch
        {
        }
    }

    private static void CreateClosedCatalogSnapshot(
        string sourcePath,
        string destinationPath)
    {
        using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var destination = new FileStream(
            destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(destination);
        destination.Flush(flushToDisk: true);
    }

    private static async Task<LightroomCatalogContents> ReadSnapshotAsync(
        string sourcePath,
        string snapshotPath,
        IReadOnlyDictionary<ColorLabel, string> colorLabelNames,
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);

        var tables = await ReadTableNamesAsync(connection, cancellationToken);
        var missingTables = CoreTables.Where(table => !tables.Contains(table)).ToArray();
        if (missingTables.Length > 0)
        {
            throw new InvalidDataException(
                $"This Lightroom catalog is not compatible (missing: {string.Join(", ", missingTables)}).");
        }

        var columns = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in CoreTables)
        {
            columns[table] = await ReadColumnsAsync(connection, table, cancellationToken);
        }
        ValidateCoreColumns(columns);

        var version = await ReadVersionAsync(connection, cancellationToken);
        var major = checked((int)(version / 100000));
        var carriedAxes = GetCarriedAxes(columns["Adobe_images"]);
        var warnings = GetSchemaWarnings(carriedAxes);
        var roots = await ReadRootsAsync(connection, cancellationToken);
        var records = await ReadRecordsAsync(
            connection, carriedAxes, colorLabelNames, cancellationToken);
        var rootCounts = records
            .GroupBy(record => record.SourceRoot, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return new LightroomCatalogContents(
            sourcePath, version, major, major is 12 or 13, carriedAxes,
            roots.Select(root => new CatalogSourceRoot(
                root, rootCounts.GetValueOrDefault(root))).ToArray(),
            records, warnings);
    }

    private static async Task<HashSet<string>> ReadTableNamesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table';";
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(1));
        return result;
    }

    private static void ValidateCoreColumns(
        IReadOnlyDictionary<string, HashSet<string>> columns)
    {
        var required = new Dictionary<string, string[]>
        {
            ["Adobe_images"] = ["rootFile", "masterImage"],
            ["AgLibraryFile"] = ["id_local", "folder", "idx_filename"],
            ["AgLibraryFolder"] = ["id_local", "rootFolder", "pathFromRoot"],
            ["AgLibraryRootFolder"] = ["id_local", "absolutePath"],
            ["Adobe_variablesTable"] = ["name", "value"]
        };
        var missing = required.SelectMany(pair => pair.Value
                .Where(column => !columns[pair.Key].Contains(column))
                .Select(column => $"{pair.Key}.{column}"))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"This Lightroom catalog is not compatible (missing: {string.Join(", ", missing)}).");
        }
    }

    private static AssessmentAxes GetCarriedAxes(HashSet<string> imageColumns)
    {
        var axes = AssessmentAxes.None;
        if (imageColumns.Contains("rating")) axes |= AssessmentAxes.Rating;
        if (imageColumns.Contains("pick")) axes |= AssessmentAxes.Flag;
        if (imageColumns.Contains("colorLabels")) axes |= AssessmentAxes.Label;
        return axes;
    }

    private static IReadOnlyList<string> GetSchemaWarnings(AssessmentAxes axes)
    {
        var warnings = new List<string>();
        if (!axes.HasFlag(AssessmentAxes.Rating)) warnings.Add("Ratings are unavailable in this catalog schema.");
        if (!axes.HasFlag(AssessmentAxes.Flag)) warnings.Add("Pick and reject flags are unavailable in this catalog schema.");
        if (!axes.HasFlag(AssessmentAxes.Label)) warnings.Add("Color labels are unavailable in this catalog schema.");
        return warnings;
    }

    private static async Task<long> ReadVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM Adobe_variablesTable WHERE name = 'Adobe_DBVersion';";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value == null || !long.TryParse(Convert.ToString(value), out var version) || version <= 0)
        {
            throw new InvalidDataException("The Lightroom catalog version could not be read.");
        }
        return version;
    }

    private static async Task<IReadOnlyList<string>> ReadRootsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT absolutePath FROM AgLibraryRootFolder ORDER BY id_local;";
        var result = new List<string>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0)) result.Add(reader.GetString(0));
        }
        return result.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static async Task<IReadOnlyList<CatalogImportRecord>> ReadRecordsAsync(
        SqliteConnection connection,
        AssessmentAxes carriedAxes,
        IReadOnlyDictionary<ColorLabel, string> colorLabelNames,
        CancellationToken cancellationToken)
    {
        var rating = carriedAxes.HasFlag(AssessmentAxes.Rating) ? "i.rating" : "NULL";
        var flag = carriedAxes.HasFlag(AssessmentAxes.Flag) ? "i.pick" : "NULL";
        var label = carriedAxes.HasFlag(AssessmentAxes.Label) ? "i.colorLabels" : "NULL";
        var terms = new List<string>();
        if (carriedAxes.HasFlag(AssessmentAxes.Rating)) terms.Add("i.rating IS NOT NULL");
        if (carriedAxes.HasFlag(AssessmentAxes.Flag)) terms.Add("i.pick <> 0");
        if (carriedAxes.HasFlag(AssessmentAxes.Label)) terms.Add("i.colorLabels <> ''");
        if (terms.Count == 0) return [];

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT rf.absolutePath, fo.pathFromRoot, fi.idx_filename,
                   {rating}, {flag}, {label}, i.masterImage
            FROM Adobe_images i
            JOIN AgLibraryFile fi ON fi.id_local = i.rootFile
            JOIN AgLibraryFolder fo ON fo.id_local = fi.folder
            JOIN AgLibraryRootFolder rf ON rf.id_local = fo.rootFolder
            WHERE {string.Join(" OR ", terms)};
            """;
        var result = new List<CatalogImportRecord>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var relativePath = (reader.IsDBNull(1) ? string.Empty : reader.GetString(1)) +
                               (reader.IsDBNull(2) ? string.Empty : reader.GetString(2));
            result.Add(new CatalogImportRecord(
                reader.GetString(0), relativePath,
                ReadRating(reader, 3, carriedAxes),
                ReadFlag(reader, 4, carriedAxes),
                ReadLabel(reader, 5, carriedAxes, colorLabelNames),
                !reader.IsDBNull(6)));
        }
        return result;
    }

    private static CatalogImportFact<int> ReadRating(
        SqliteDataReader reader, int ordinal, AssessmentAxes axes)
    {
        if (!axes.HasFlag(AssessmentAxes.Rating)) return CatalogImportFact<int>.NotCarried;
        if (reader.IsDBNull(ordinal)) return CatalogImportFact<int>.Empty;
        var rating = (int)Math.Round(reader.GetDouble(ordinal), MidpointRounding.AwayFromZero);
        return rating switch
        {
            0 => CatalogImportFact<int>.Empty,
            >= 1 and <= 5 => CatalogImportFact<int>.Mapped(rating),
            _ => CatalogImportFact<int>.Unsupported(Convert.ToString(reader.GetValue(ordinal)))
        };
    }

    private static CatalogImportFact<ImageFlag> ReadFlag(
        SqliteDataReader reader, int ordinal, AssessmentAxes axes)
    {
        if (!axes.HasFlag(AssessmentAxes.Flag)) return CatalogImportFact<ImageFlag>.NotCarried;
        if (reader.IsDBNull(ordinal)) return CatalogImportFact<ImageFlag>.Empty;
        var value = (int)Math.Round(reader.GetDouble(ordinal), MidpointRounding.AwayFromZero);
        return value switch
        {
            -1 => CatalogImportFact<ImageFlag>.Mapped(ImageFlag.Rejected),
            0 => CatalogImportFact<ImageFlag>.Empty,
            1 => CatalogImportFact<ImageFlag>.Mapped(ImageFlag.Picked),
            _ => CatalogImportFact<ImageFlag>.Unsupported(Convert.ToString(reader.GetValue(ordinal)))
        };
    }

    private static CatalogImportFact<ColorLabel> ReadLabel(
        SqliteDataReader reader,
        int ordinal,
        AssessmentAxes axes,
        IReadOnlyDictionary<ColorLabel, string> names)
    {
        if (!axes.HasFlag(AssessmentAxes.Label)) return CatalogImportFact<ColorLabel>.NotCarried;
        if (reader.IsDBNull(ordinal)) return CatalogImportFact<ColorLabel>.Empty;
        var token = reader.GetString(ordinal);
        if (token.Length == 0) return CatalogImportFact<ColorLabel>.Empty;
        foreach (var (slot, name) in ColorLabelNames.Defaults.Concat(names))
        {
            if (string.Equals(token, name, StringComparison.OrdinalIgnoreCase))
                return CatalogImportFact<ColorLabel>.Mapped(slot);
        }
        return CatalogImportFact<ColorLabel>.Unsupported(token);
    }

    private static void TryDeleteSnapshotDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
