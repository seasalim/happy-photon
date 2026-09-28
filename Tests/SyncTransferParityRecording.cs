using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal sealed class SyncTransferParityRecording(string name)
{
    private readonly SortedDictionary<string, JsonElement> _outputs = new(StringComparer.Ordinal);

    internal void Add(string key, object value) =>
        _outputs.Add(key, JsonSerializer.SerializeToElement(value));

    internal static JsonElement Settings(EditSettings value) =>
        JsonSerializer.Deserialize<JsonElement>(EditSettingsJson.Serialize(value));

    internal void Verify(ITestOutputHelper output)
    {
        var directory = Path.Combine(GoldenTestPaths.RepositoryRoot, "Tests", "SyncTransferParityData");
        var path = Path.Combine(directory, $"{name}.jsonl.gz");
        var lines = _outputs.Select(pair => JsonSerializer.Serialize(new Row(pair.Key, pair.Value)));
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
        var inputs = _outputs.Keys.Count(key => key.StartsWith("input/", StringComparison.Ordinal));
        var counts = $"outputs={_outputs.Count - inputs}; inputs={inputs}; records={_outputs.Count}";

        if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_RECORD_SYNC_PARITY") == "1")
        {
            // A new baseline must be deliberate; a normal replay can never refresh it.
            Assert.False(File.Exists(path), $"Refusing to overwrite the frozen recording: {path}");
            Directory.CreateDirectory(directory);

            using (var file = File.Create(path))
            using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
            {
                gzip.Write(bytes);
            }

            output.WriteLine($"G1 recorded {name}: {counts}; bytes={new FileInfo(path).Length}; decodedBytes={bytes.Length}");

            return;
        }

        Assert.True(File.Exists(path), $"Missing pre-change recording: {path}");
        using var input = File.OpenRead(path);
        using var compressed = new GZipStream(input, CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        compressed.CopyTo(decoded);
        var expectedBytes = decoded.ToArray();
        var expected = Encoding.UTF8.GetString(expectedBytes).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<Row>(line)!)
            .ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        var differences = expected.Keys.Union(_outputs.Keys, StringComparer.Ordinal)
            .Where(key => !expected.TryGetValue(key, out var before) ||
                !_outputs.TryGetValue(key, out var after) || before.GetRawText() != after.GetRawText())
            .ToArray();
        output.WriteLine($"G1 replay {name}: {counts}; bytes={input.Length}; decodedBytes={bytes.Length}; differences={differences.Length}");
        Assert.True(differences.Length == 0,
            $"G1 differing outputs ({differences.Length}): {string.Join(", ", differences.Take(20))}");
        Assert.Equal(expectedBytes, bytes);
    }

    private sealed record Row(string Key, JsonElement Value);
}
