using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal static partial class StraightenGateOwnerFixtures
{
    internal static readonly string[] LandscapeNames = ["DSCF0423.JPG", "DSCF2263.RAF", "DSCF6915.JPG",
        "DSCF7257.RAF", "DSCF7806.JPG", "DSCF7825.JPG", "DSCF8355.JPG", "DSCF8477.JPG", "DSCF8495.JPG"];

    internal static readonly string[] LandscapeReportOnly = ["DSCF8521.JPG", "DSCF8525.JPG"];

    internal static readonly IReadOnlyDictionary<string, double> LandscapeLabels = new Dictionary<string, double>
    {
        ["DSCF0423.JPG"] = 1.8,
        ["DSCF2263.RAF"] = 1.8,
        ["DSCF6915.JPG"] = 4.2,
        ["DSCF7257.RAF"] = 1.8,
        ["DSCF7806.JPG"] = 2.1,
        ["DSCF7825.JPG"] = -.9,
        ["DSCF8355.JPG"] = -1.5,
        ["DSCF8477.JPG"] = -1,
        ["DSCF8495.JPG"] = -1.6
    };

    internal static readonly IReadOnlyDictionary<string, double> JudgementLabels = new Dictionary<string, double>
    {
        ["DSCF0129.RAF"] = -.72,
        ["IMG_4952.JPG"] = -2.83,
        ["IMG_6854.HEIC"] = -1.02
    };

    internal static string OwnerFolder()
    {
        var folder = Environment.GetEnvironmentVariable(DirectoryVariable);
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(folder), $"Owner photo set unavailable: {DirectoryVariable} is unset");

        return folder!;
    }

    internal static IReadOnlyDictionary<string, double> ReadLandscapeLabels()
    {
        var path = Path.Combine(OwnerFolder(), "levels.txt");
        Assert.True(File.Exists(path), "Missing owner levels.txt");
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));
        var labels = ParseLandscapeLabels(File.ReadAllLines(path));
        VerifyLandscapeLabels(labels);

        return LandscapeLabels;
    }

    internal static void VerifyLandscapeLabels(IReadOnlyDictionary<string, double> labels)
    {
        Assert.Equal(LandscapeLabels.Count, labels.Count);

        foreach (var (name, expected) in LandscapeLabels)
        {
            Assert.True(labels.TryGetValue(name, out var actual), $"Missing owner level: {name}");
            Assert.True(actual == expected, $"Owner level changed: {name}, expected {expected}, actual {actual}");
        }
    }

    internal static IReadOnlyDictionary<string, double> ParseLandscapeLabels(IEnumerable<string> lines)
    {
        var labels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var match = Regex.Match(line.Trim(), @"^(.*?)\s+([+-]?\d+(?:\.\d+)?)$");
            Assert.True(match.Success, "Invalid owner levels.txt row; expected name and angle");
            var name = Path.GetFileName(match.Groups[1].Value.Trim().Trim('"').Replace('\\', '/'));
            Assert.True(labels.TryAdd(name, double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)),
                $"Duplicate owner level: {name}");
        }

        return labels;
    }

    // Emit only metadata for Claude to pin in Manifest before production changes.
    // Missing hashes cannot be invented or silently trusted on a later run.
    internal static object LandscapeMetadata(string name)
    {
        var path = Path.Combine(OwnerFolder(), name);
        Assert.True(File.Exists(path), $"Missing straighten owner fixture: {name}");
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        if (Manifest.TryGetValue(name, out var expected))
        {
            Assert.Equal(expected, hash);
        }

        var labels = ReadLandscapeLabels();

        return new { name, sha256 = hash, pinned = Manifest.ContainsKey(name),
            label = labels.TryGetValue(name, out var label) ? label : (double?)null };
    }

    internal static PreviewBasePair LoadLandscape(string name)
    {
        OwnerFolder();
        Assert.True(Manifest.ContainsKey(name), $"Pin the SHA-256 for {name} in the owner Manifest before measuring");

        return Load(name);
    }
}
