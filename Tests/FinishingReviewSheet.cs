using System.Net;
using System.Text;
using System.Text.Json;
using HappyPhoton.Models;
using ImageMagick;
using Xunit;
using static HappyPhoton.Tests.FinishingGateSupport;

namespace HappyPhoton.Tests;

internal static class FinishingReviewSheet
{
    internal static string[] PhotoPaths()
    {
        var paths = Controls.Select(control => GoldenTestPaths.Asset(control.Name)).ToList();
        var ownerFolder = Environment.GetEnvironmentVariable("HAPPY_PHOTON_LOOKS_REVIEW_DIR");

        if (!string.IsNullOrWhiteSpace(ownerFolder))
        {
            Assert.True(Directory.Exists(ownerFolder), "Owner review folder does not exist");
            paths.AddRange(Directory.EnumerateFiles(ownerFolder).Order(StringComparer.OrdinalIgnoreCase)
                .Where(path => ImageFile.SupportedExtensions.Contains(Path.GetExtension(path))));
        }

        return paths.ToArray();
    }

    internal static StringBuilder Start(string title) => new StringBuilder(
        "<!doctype html><meta charset=utf-8><meta name=viewport content='width=device-width'>" +
        "<style>body{font:16px system-ui;margin:2em}img{max-width:100%;height:auto}" +
        "figure{margin:2em 0}table{border-collapse:collapse}td,th{padding:.4em;text-align:left}</style>")
        .Append("<title>").Append(Encode(title)).Append("</title><h1>").Append(Encode(title)).Append("</h1>");

    internal static string Encode(string text) => WebUtility.HtmlEncode(text);

    internal static string NewDirectory(string name) => Directory.CreateDirectory(Path.Combine(Folder,
        name + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"))).FullName;

    internal static void Pair(StringBuilder html, string directory, string slug, string caption,
        MagickImage left, MagickImage right)
    {
        Assert.Equal(left.Width, right.Width);
        Assert.Equal(left.Height, right.Height);
        using var images = new MagickImageCollection { new MagickImage(left), new MagickImage(right) };
        using var pair = images.AppendHorizontally();
        pair.Depth = 8;
        var path = Path.Combine(directory, slug + ".png");
        pair.Write(path);
        using var saved = new MagickImage(path);
        Assert.Equal(left.Width * 2, saved.Width);
        Assert.Equal(left.Height, saved.Height);
        html.Append("<figure><figcaption>").Append(Encode(caption)).Append("</figcaption><a href='")
            .Append(Encode(slug)).Append(".png'><img loading=lazy src='").Append(Encode(slug))
            .Append(".png'></a></figure>");
    }

    internal static JsonElement[]? Evidence(FinishingCandidate candidate)
    {
        var evidence = new List<JsonElement>();

        foreach (var (gate, fixture) in new[]
        {
            ("G1", "raw"), ("G1", "standard"), ("G2", "raw"), ("G2", "standard"),
            ("G3", "raw"), ("G3", "standard"), ("G4", "all")
        })
        {
            var path = Path.Combine(Folder, "receipts", $"{gate}-{fixture}-{candidate.Id}.json");
            if (!File.Exists(path)) return null;

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var receipt = document.RootElement;
            if (!CurrentReceipt(receipt, candidate, gate, fixture)) return null;

            evidence.Add(receipt.Clone());
        }

        return evidence.ToArray();
    }

    internal static bool CurrentReceipt(JsonElement receipt, FinishingCandidate candidate, string gate, string fixture) =>
        receipt.GetProperty("pass").GetBoolean() && receipt.GetProperty("gate").GetString() == gate &&
        receipt.GetProperty("fixture").GetString() == fixture &&
        receipt.GetProperty("candidate").GetString() == candidate.Id &&
        receipt.GetProperty("candidateSha256").GetString() == CandidateHash(candidate) &&
        receipt.GetProperty("candidateSha256").GetString() == candidate.LoadedSha256 &&
        receipt.GetProperty("assemblySha256").GetString() == AssemblyHash &&
        receipt.GetProperty("productionSha256").GetString() == ProductionHash &&
        RecordsMatchReceipt(receipt);

    private static bool RecordsMatchReceipt(JsonElement receipt)
    {
        if (!receipt.TryGetProperty("records", out var records) ||
            records.ValueKind != JsonValueKind.Array || records.GetArrayLength() == 0)
        {
            return false;
        }

        return records.EnumerateArray().All(record =>
            new[] { "candidateSha256", "assemblySha256", "productionSha256" }.All(field =>
                record.TryGetProperty(field, out var hash) && hash.ValueKind == JsonValueKind.String &&
                hash.GetString() == receipt.GetProperty(field).GetString()));
    }

    internal static void AppendEvidence(StringBuilder html, JsonElement[] receipts)
    {
        html.Append("<details><summary>G1–G4 measurements and thresholds</summary><pre>");

        foreach (var receipt in receipts)
        {
            html.Append(Encode(JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true })));
        }

        html.Append("</pre></details><h2>Full-look preview / export parity</h2>")
            .Append("<table><tr><th>Fixture / size</th><th>Mean / p99 ΔE</th><th>Bounds</th><th>Exceedance</th></tr>");

        foreach (var receipt in receipts.Where(item => item.GetProperty("gate").GetString() == "G3"))
        {
            foreach (var row in receipt.GetProperty("records").EnumerateArray())
            {
                var value = row.GetProperty("values");
                if (value.GetProperty("arm").GetString() != "full") continue;

                html.Append("<tr><td>").Append(Encode(row.GetProperty("fixture").GetString()!)).Append(' ')
                    .Append(value.GetProperty("width")).Append('×').Append(value.GetProperty("height"))
                    .Append("</td><td>").Append(value.GetProperty("mean")).Append(" / ").Append(value.GetProperty("p99"))
                    .Append("</td><td>").Append(value.GetProperty("meanLimit")).Append(" / ").Append(value.GetProperty("p99Limit"))
                    .Append("</td><td>").Append(value.GetProperty("withinBound").GetBoolean() ? "none" : "EXCEEDS — owner acceptance required")
                    .Append("</td></tr>");
            }
        }

        html.Append("</table>");
    }

    internal static (string Name, EditSettings Settings)[] FollowOnLooks()
        => FinishingFollowOn.Looks();
}
