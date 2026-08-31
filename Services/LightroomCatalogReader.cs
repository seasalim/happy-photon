using HappyPhoton.Models;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.RegularExpressions;

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
        var cropColumns = tables.Contains("Adobe_imageDevelopSettings")
            ? await ReadColumnsAsync(connection, "Adobe_imageDevelopSettings", cancellationToken)
            : [];
        var carriesCrops = columns["Adobe_images"].Contains("orientation") &&
            new[] { "image", "text", "fileWidth", "fileHeight", "croppedWidth", "croppedHeight" }
                .All(cropColumns.Contains);
        var warnings = GetSchemaWarnings(carriedAxes).ToList();
        if (!carriesCrops) warnings.Add("Crops are unavailable in this catalog schema.");
        var roots = await ReadRootsAsync(connection, cancellationToken);
        var records = await ReadRecordsAsync(
            connection, carriedAxes, carriesCrops, colorLabelNames, cancellationToken);
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
        bool carriesCrops,
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
        if (carriesCrops) terms.Add("ds.text IS NOT NULL");
        if (terms.Count == 0) return [];

        var cropColumns = carriesCrops
            ? "i.orientation, ds.text, ds.fileWidth, ds.fileHeight, ds.croppedWidth, ds.croppedHeight"
            : "NULL, NULL, NULL, NULL, NULL, NULL";
        var cropJoin = carriesCrops
            ? "LEFT JOIN Adobe_imageDevelopSettings ds ON ds.image = i.id_local"
            : string.Empty;

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT rf.absolutePath, fo.pathFromRoot, fi.idx_filename,
                   {rating}, {flag}, {label}, i.masterImage, {cropColumns}
            FROM Adobe_images i
            {cropJoin}
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
            var crop = carriesCrops ? ReadCrop(reader, 7) : null;
            var record = new CatalogImportRecord(
                reader.GetString(0), relativePath,
                ReadRating(reader, 3, carriedAxes),
                ReadFlag(reader, 4, carriedAxes),
                ReadLabel(reader, 5, carriedAxes, colorLabelNames),
                !reader.IsDBNull(6), crop);
            if (record.Rating.Kind is CatalogImportFactKind.Empty or CatalogImportFactKind.NotCarried &&
                record.Flag.Kind is CatalogImportFactKind.Empty or CatalogImportFactKind.NotCarried &&
                record.ColorLabel.Kind is CatalogImportFactKind.Empty or CatalogImportFactKind.NotCarried &&
                crop?.Kind is null or XmpFactKind.Empty)
                continue;
            result.Add(record);
        }
        return result;
    }

    internal static LightroomCropFact ParseCrop(
        string? blob, string? orientation, double? fileWidth, double? fileHeight,
        double? croppedWidth, double? croppedHeight)
    {
        if (blob == null) return LightroomCropFact.Empty;
        if (!TryScanTopLevel(blob, out var values)) return LightroomCropFact.Unsupported;
        var edges = new[] { "CropLeft", "CropTop", "CropRight", "CropBottom" };
        var present = edges.Count(values.ContainsKey);
        if (present == 0) return LightroomCropFact.Empty;
        if (present != 4 || !edges.Select(name => ParseNumber(values[name])).All(value => value is >= 0 and <= 1))
            return LightroomCropFact.Unsupported;
        var left = ParseNumber(values[edges[0]])!.Value;
        var top = ParseNumber(values[edges[1]])!.Value;
        var right = ParseNumber(values[edges[2]])!.Value;
        var bottom = ParseNumber(values[edges[3]])!.Value;
        if (left == 0 && top == 0 && right == 1 && bottom == 1) return LightroomCropFact.Empty;
        if (left >= right || top >= bottom || !IsZero(values, "CropAngle") ||
            !IsZero(values, "CropConstrainToWarp", allowFalse: true) || orientation != "AB" ||
            fileWidth is not > 0 || fileHeight is not > 0 ||
            croppedWidth is not > 0 || croppedHeight is not > 0 ||
            Math.Abs((right - left) * fileWidth.Value - croppedWidth.Value) > 1 ||
            Math.Abs((bottom - top) * fileHeight.Value - croppedHeight.Value) > 1)
            return LightroomCropFact.Unsupported;
        return new(XmpFactKind.Matched,
            new CropRegion { Left = left, Top = top, Right = right, Bottom = bottom },
            orientation);
    }

    private static LightroomCropFact ReadCrop(SqliteDataReader reader, int ordinal) =>
        ParseCrop(reader.IsDBNull(ordinal + 1) ? null : reader.GetString(ordinal + 1),
            reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal),
            ReadNullableDouble(reader, ordinal + 2), ReadNullableDouble(reader, ordinal + 3),
            ReadNullableDouble(reader, ordinal + 4), ReadNullableDouble(reader, ordinal + 5));

    private static double? ReadNullableDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : double.TryParse(Convert.ToString(reader.GetValue(ordinal)),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value : null;

    private static double? ParseNumber(string value) =>
        double.TryParse(value.Trim().TrimEnd(','), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) ? parsed : null;

    private static bool IsZero(
        IReadOnlyDictionary<string, string> values, string key, bool allowFalse = false) =>
        !values.TryGetValue(key, out var text) || ParseNumber(text) == 0 ||
        allowFalse && text.Trim().TrimEnd(',').Equals("false", StringComparison.OrdinalIgnoreCase);

    private static bool TryScanTopLevel(string blob, out Dictionary<string, string> values)
    {
        values = new(StringComparer.Ordinal);
        var depth = 0;
        var quoted = false;
        var escaped = false;
        var duplicate = false;
        var unconsumedCrop = false;
        foreach (var line in blob.Replace("\r\n", "\n").Split('\n'))
        {
            var consumedCropIndex = -1;
            if (depth == 1)
            {
                var match = TopLevelAssignment.Match(line);
                if (!match.Success && line.TrimStart().StartsWith("Crop", StringComparison.Ordinal)) return false;
                if (match.Success && !values.TryAdd(match.Groups[1].Value, match.Groups[2].Value))
                    duplicate = true;
                if (match.Success && match.Groups[1].Value.StartsWith("Crop", StringComparison.Ordinal))
                    consumedCropIndex = match.Groups[1].Index;
            }
            for (var index = 0; index < line.Length; index++)
            {
                var character = line[index];
                if (!quoted && depth == 1 && index != consumedCropIndex &&
                    StartsCropAssignment(line, index))
                    unconsumedCrop = true;
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (character == '\\') escaped = true;
                    else if (character == '"') quoted = false;
                }
                else if (character == '"') quoted = true;
                else if (character == '{') depth++;
                else if (character == '}') depth--;
                if (depth < 0) return false;
            }
        }
        return !duplicate && !unconsumedCrop && !quoted && depth == 0 &&
            blob.TrimStart().StartsWith("s = {");
    }

    private static bool StartsCropAssignment(string line, int index)
    {
        if (!line.AsSpan(index).StartsWith("Crop") || index > 0 &&
            (char.IsLetterOrDigit(line[index - 1]) || line[index - 1] == '_')) return false;
        var end = index + 4;
        while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_')) end++;
        while (end < line.Length && char.IsWhiteSpace(line[end])) end++;
        return end < line.Length && line[end] == '=';
    }

    private static readonly Regex TopLevelAssignment = new(
        @"^\s*([A-Za-z][A-Za-z0-9_]*)\s*=\s*(.*?)\s*,?\s*$",
        RegexOptions.CultureInvariant);

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
