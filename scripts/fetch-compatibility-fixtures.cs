#:property PublishAot=false
#:property SelfContained=false

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

const long maximumFixtureBytes = 30L * 1024 * 1024;

if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine(
        "Usage: dotnet run --file scripts/fetch-compatibility-fixtures.cs -- [fixture-slug ...]");
    Console.WriteLine(
        "With no slugs, verifies cached selected fixtures and downloads the missing ones.");
    return 0;
}

var repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
var manifestPath = Path.Combine(
    repositoryRoot, "Tests", "compatibility-fixtures.json");
var cacheDirectory = Path.Combine(
    repositoryRoot, "artifacts", "compatibility-fixtures");
var fixtures = ReadSelectedFixtures(manifestPath, maximumFixtureBytes);
var requested = args.ToHashSet(StringComparer.Ordinal);
if (requested.Count > 0)
{
    var unknown = requested.Except(
        fixtures.Select(fixture => fixture.Slug),
        StringComparer.Ordinal).ToArray();
    if (unknown.Length > 0)
    {
        Console.Error.WriteLine(
            $"Unknown selected fixture slug(s): {string.Join(", ", unknown)}");
        return 1;
    }
    fixtures = fixtures
        .Where(fixture => requested.Contains(fixture.Slug))
        .ToList();
}

Directory.CreateDirectory(cacheDirectory);
using var client = new HttpClient(new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.All
});
client.Timeout = TimeSpan.FromMinutes(10);

foreach (var fixture in fixtures)
{
    var destination = Path.Combine(cacheDirectory, fixture.FileName);
    try
    {
        if (File.Exists(destination))
        {
            var cachedHash = VerifyFile(destination, fixture, maximumFixtureBytes);
            PrintResult(fixture, cachedHash, "cached");
            continue;
        }

        var temporary = Path.Combine(
            cacheDirectory,
            $".{fixture.Slug}.{Guid.NewGuid():N}.download");
        try
        {
            using var response = await client.GetAsync(
                fixture.ProvenanceUrl,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var target = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer);
                    if (read == 0)
                    {
                        break;
                    }
                    total = checked(total + read);
                    if (total > maximumFixtureBytes)
                    {
                        throw new InvalidDataException(
                            $"{fixture.Slug}: download exceeded the 30 MiB cap.");
                    }
                    await target.WriteAsync(buffer.AsMemory(0, read));
                }
            }

            var downloadedHash = VerifyFile(
                temporary, fixture, maximumFixtureBytes);
            File.Move(temporary, destination);
            PrintResult(fixture, downloadedHash, "downloaded");
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}

return 0;

static List<FixtureDownload> ReadSelectedFixtures(
    string manifestPath,
    long maximumFixtureBytes)
{
    using var document = JsonDocument.Parse(
        File.ReadAllText(manifestPath),
        new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
    var root = document.RootElement;
    if (root.GetProperty("schemaVersion").GetInt32() != 1)
    {
        throw new InvalidDataException("Unsupported compatibility manifest schema.");
    }

    var fixtures = new List<FixtureDownload>();
    foreach (var element in root.GetProperty("fixtures").EnumerateArray())
    {
        if (element.GetProperty("selectionStatus").GetString() != "selected")
        {
            continue;
        }
        var fixture = new FixtureDownload(
            RequiredString(element, "slug"),
            RequiredString(element, "extension"),
            new Uri(RequiredString(element, "provenanceUrl"), UriKind.Absolute),
            element.GetProperty("sizeBytes").GetInt64(),
            RequiredString(element, "sha256"));
        if (fixture.Slug.Length > 80 || fixture.Slug[0] == '-' ||
            fixture.Slug[^1] == '-' || fixture.Slug.Any(character =>
                !char.IsAsciiLetterLower(character) &&
                !char.IsAsciiDigit(character) && character != '-'))
        {
            throw new InvalidDataException(
                $"{fixture.Slug}: slug contains unsafe characters.");
        }
        if (fixture.Extension.Length is <= 1 or > 10 ||
            fixture.Extension[0] != '.' ||
            fixture.Extension.Skip(1).Any(character =>
                !char.IsAsciiLetterOrDigit(character)))
        {
            throw new InvalidDataException(
                $"{fixture.Slug}: extension is invalid.");
        }
        if (fixture.ProvenanceUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException(
                $"{fixture.Slug}: provenanceUrl must use HTTPS.");
        }
        if (fixture.SizeBytes is <= 0 || fixture.SizeBytes > maximumFixtureBytes)
        {
            throw new InvalidDataException(
                $"{fixture.Slug}: manifest size is outside the 30 MiB cap.");
        }
        if (fixture.Sha256.Length != 64 ||
            fixture.Sha256.Any(character =>
                !char.IsAsciiHexDigit(character) || char.IsAsciiLetterUpper(character)))
        {
            throw new InvalidDataException(
                $"{fixture.Slug}: manifest SHA-256 is invalid.");
        }
        fixtures.Add(fixture);
    }
    return fixtures;
}

static string RequiredString(JsonElement element, string name)
{
    var value = element.GetProperty(name).GetString();
    return !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidDataException($"Manifest field {name} is required.");
}

static string VerifyFile(
    string path,
    FixtureDownload fixture,
    long maximumFixtureBytes)
{
    var length = new FileInfo(path).Length;
    if (length > maximumFixtureBytes)
    {
        throw new InvalidDataException(
            $"{fixture.Slug}: file exceeds the 30 MiB cap.");
    }
    if (length != fixture.SizeBytes)
    {
        throw new InvalidDataException(
            $"{fixture.Slug}: length mismatch; expected {fixture.SizeBytes}, " +
            $"observed {length}. The cached file was not replaced.");
    }
    using var stream = File.OpenRead(path);
    var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    if (actual != fixture.Sha256)
    {
        throw new InvalidDataException(
            $"{fixture.Slug}: SHA-256 mismatch; expected {fixture.Sha256}, " +
            $"observed {actual}. The cached file was not replaced.");
    }
    return actual;
}

static void PrintResult(
    FixtureDownload fixture,
    string hash,
    string source) =>
    Console.WriteLine(
        $"{fixture.Slug}: endpoint_host={fixture.ProvenanceUrl.Host}; " +
        $"provenance_host={fixture.ProvenanceUrl.Host}; bytes={fixture.SizeBytes}; " +
        $"sha256={hash}; verified={source}");

static string FindRepositoryRoot(string start)
{
    var directory = new DirectoryInfo(Path.GetFullPath(start));
    while (directory != null &&
        !File.Exists(Path.Combine(directory.FullName, "HappyPhoton.sln")))
    {
        directory = directory.Parent;
    }
    return directory?.FullName ?? throw new DirectoryNotFoundException(
        "Could not locate HappyPhoton.sln from the current directory.");
}

internal sealed record FixtureDownload(
    string Slug,
    string Extension,
    Uri ProvenanceUrl,
    long SizeBytes,
    string Sha256)
{
    internal string FileName => $"{Slug}{Extension}";
}
