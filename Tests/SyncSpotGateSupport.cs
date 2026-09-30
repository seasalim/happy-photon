using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Xunit;

namespace HappyPhoton.Tests;

internal static class SyncSpotGateSupport
{
    internal static EditSettings Source() => new() { Repairs = RepairTestWorkload.S64() };

    internal static string RepairsJson(EditSettings settings)
    {
        using var document = JsonDocument.Parse(EditSettingsJson.Serialize(settings));

        return document.RootElement.GetProperty("repairs").GetRawText();
    }

    internal static int PayloadBytes(ITestOutputHelper output)
    {
        var source = Source();
        Assert.Equal(RepairModelTests.S64(), source.Repairs);
        var bytes = Encoding.UTF8.GetBytes(RepairsJson(source));
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        Assert.Equal(11693, bytes.Length);
        Assert.Equal("24B9B001DC66499D8440754A4967E583405EB494216788C80DC8654224B82D5E", hash);
        output.WriteLine($"SYNC_SPOT payloadBytes={bytes.Length} payloadSha256={hash} repairs=64");

        return bytes.Length;
    }

    internal static string Fixture()
    {
        var path = SyncPhotoGateSupport.LocalFixture(SyncPhotoGateSupport.Raw);
        AssertSerial(path);

        return path;
    }

    internal static void AssertSerial(string path)
    {
        SyncProfileGateSupport.RequireLocal(path);
        var directories = ImageMetadataReader.ReadMetadata(path);
        var exif = Assert.Single(directories.OfType<ExifSubIfdDirectory>());
        Assert.Equal("233054000882", exif.GetString(0xA431)?.Trim());
    }

    internal static EditSettings Reset(EditSettings source)
    {
        var reset = source.Clone();
        reset.Repairs = null;
        Assert.Null(reset.Repairs);
        Assert.Equal(64, source.Repairs!.Count);
        Assert.Null(source.AppliedPresetId);
        Assert.Null(reset.AppliedPresetId);
        Assert.Equal(EditSettingsJson.Serialize(new EditSettings()), EditSettingsJson.Serialize(reset));

        return reset;
    }

    internal static async Task AssertTransfer(CatalogService catalog, ImageFile target,
        EditSettings before, EditSettings expected)
    {
        Assert.Null(before.AppliedPresetId);
        Assert.Null(expected.AppliedPresetId);
        var states = (await catalog.LoadImageStatesAsync([target.FilePath]))[target.FilePath];
        var state = Assert.Single(states, state => state.Version == target.Version);
        Assert.Equal(EditSettingsJson.Serialize(expected), EditSettingsJson.Serialize(state.EditSettings));
        var history = await catalog.LoadEditHistoryAsync(target.CatalogId);
        Assert.Equal(1, history.Position);
        Assert.Equal(["Original", "Paste settings"], history.Entries.Select(entry => entry.Label));
        Assert.Equal(EditSettingsJson.Serialize(before), EditSettingsJson.Serialize(history.Entries[0].Settings));
        Assert.Equal(EditSettingsJson.Serialize(expected), EditSettingsJson.Serialize(history.Entries[1].Settings));
    }

    internal static double CenterError(IReadOnlyList<Repair> expected, IReadOnlyList<Repair> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        var maximum = 0d;

        for (var index = 0; index < expected.Count; index++)
        {
            var a = expected[index];
            var b = actual[index];
            var destination = Math.Sqrt(Math.Pow(a.U - b.U, 2) + Math.Pow(a.V - b.V, 2));
            var donor = Math.Sqrt(Math.Pow(a.Su - b.Su, 2) + Math.Pow(a.Sv - b.Sv, 2));
            maximum = Math.Max(maximum, Math.Max(destination, donor) * 16384);
        }

        return maximum;
    }
}


