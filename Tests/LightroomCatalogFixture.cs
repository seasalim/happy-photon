using Microsoft.Data.Sqlite;

namespace HappyPhoton.Tests;

internal sealed class LightroomCatalogFixture : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"happy-photon-lightroom-{Guid.NewGuid():N}");
    private SqliteConnection? _writer;

    public string CatalogPath { get; }

    public LightroomCatalogFixture(
        long version = 1303001,
        bool includeRating = true,
        bool includeFlag = true,
        bool includeLabel = true,
        bool useWal = false)
    {
        Directory.CreateDirectory(_directory);
        CatalogPath = Path.Combine(_directory, "fixture.lrcat");
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = CatalogPath,
            Pooling = false
        };
        _writer = new SqliteConnection(builder.ToString());
        _writer.Open();
        if (useWal) Execute("PRAGMA journal_mode=WAL;");
        Execute("CREATE TABLE Adobe_variablesTable (name TEXT, value TEXT);");
        Execute("INSERT INTO Adobe_variablesTable VALUES ('Adobe_DBVersion', @version);",
            ("@version", version.ToString()));
        Execute("CREATE TABLE AgLibraryRootFolder (id_local INTEGER PRIMARY KEY, absolutePath TEXT);");
        Execute("CREATE TABLE AgLibraryFolder (id_local INTEGER PRIMARY KEY, rootFolder INTEGER, pathFromRoot TEXT);");
        Execute("CREATE TABLE AgLibraryFile (id_local INTEGER PRIMARY KEY, folder INTEGER, idx_filename TEXT);");
        var optional = new List<string>();
        if (includeRating) optional.Add("rating REAL");
        if (includeFlag) optional.Add("pick REAL");
        if (includeLabel) optional.Add("colorLabels TEXT");
        Execute($"CREATE TABLE Adobe_images (id_local INTEGER PRIMARY KEY, rootFile INTEGER, masterImage INTEGER, orientation TEXT{(optional.Count == 0 ? "" : ", " + string.Join(", ", optional))});");
        Execute("CREATE TABLE Adobe_imageDevelopSettings (image INTEGER, text TEXT, fileWidth REAL, fileHeight REAL, croppedWidth REAL, croppedHeight REAL);");
    }

    public void AddPhoto(
        int id,
        string root,
        string relativeFolder,
        string fileName,
        double? rating = null,
        double pick = 0,
        string label = "",
        bool virtualCopy = false)
    {
        Execute("INSERT OR IGNORE INTO AgLibraryRootFolder VALUES (@id, @root);",
            ("@id", id), ("@root", root));
        Execute("INSERT INTO AgLibraryFolder VALUES (@id, @rootId, @relative);",
            ("@id", id), ("@rootId", id), ("@relative", relativeFolder));
        Execute("INSERT INTO AgLibraryFile VALUES (@id, @folder, @name);",
            ("@id", id), ("@folder", id), ("@name", fileName));

        var columns = GetImageColumns();
        var names = new List<string> { "id_local", "rootFile", "masterImage", "orientation" };
        var values = new List<string> { "@id", "@rootFile", "@master", "@orientation" };
        var parameters = new List<(string, object?)>
        {
            ("@id", id), ("@rootFile", id), ("@master", virtualCopy ? 1 : null),
            ("@orientation", "AB")
        };
        if (columns.Contains("rating"))
        {
            names.Add("rating"); values.Add("@rating"); parameters.Add(("@rating", rating));
        }
        if (columns.Contains("pick"))
        {
            names.Add("pick"); values.Add("@pick"); parameters.Add(("@pick", pick));
        }
        if (columns.Contains("colorLabels"))
        {
            names.Add("colorLabels"); values.Add("@label"); parameters.Add(("@label", label));
        }
        Execute(
            $"INSERT INTO Adobe_images ({string.Join(",", names)}) VALUES ({string.Join(",", values)});",
            parameters.ToArray());
    }

    public void AddDevelopSettings(
        int imageId, string text, string orientation = "AB",
        double fileWidth = 4000, double fileHeight = 3000,
        double croppedWidth = 3200, double croppedHeight = 2400)
    {
        AddDevelopSettingsRaw(imageId, text, orientation,
            fileWidth, fileHeight, croppedWidth, croppedHeight);
    }

    public void AddDevelopSettingsRaw(
        int imageId, string text, string orientation,
        object? fileWidth, object? fileHeight,
        object? croppedWidth, object? croppedHeight)
    {
        Execute("UPDATE Adobe_images SET orientation = @orientation WHERE id_local = @id;",
            ("@orientation", orientation), ("@id", imageId));
        Execute("INSERT INTO Adobe_imageDevelopSettings VALUES (@id, @text, @fw, @fh, @cw, @ch);",
            ("@id", imageId), ("@text", text), ("@fw", fileWidth),
            ("@fh", fileHeight), ("@cw", croppedWidth), ("@ch", croppedHeight));
    }

    public void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = _writer!.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void CloseWriter()
    {
        _writer?.Dispose();
        _writer = null;
    }

    public IReadOnlyDictionary<string, FileStamp> CaptureSourceFiles()
    {
        var result = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in SidecarPaths())
        {
            if (!File.Exists(path))
            {
                result[path] = new FileStamp(false, [], default, 0);
                continue;
            }
            var info = new FileInfo(path);
            result[path] = new FileStamp(
                true, ReadShared(path), info.LastWriteTimeUtc, info.Length);
        }
        return result;
    }

    private HashSet<string> GetImageColumns()
    {
        using var command = _writer!.CreateCommand();
        command.CommandText = "PRAGMA table_info(Adobe_images);";
        using var reader = command.ExecuteReader();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) result.Add(reader.GetString(1));
        return result;
    }

    private IEnumerable<string> SidecarPaths()
    {
        yield return CatalogPath;
        yield return CatalogPath + "-wal";
        yield return CatalogPath + "-shm";
        yield return CatalogPath + "-journal";
    }

    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public void Dispose()
    {
        CloseWriter();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    internal sealed record FileStamp(
        bool Exists,
        byte[] Bytes,
        DateTime LastWriteUtc,
        long Length);
}
