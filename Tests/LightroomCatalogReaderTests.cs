using HappyPhoton.Models;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LightroomCatalogReaderTests
{
    [Fact]
    public async Task Read_CropOnlyMasterUsesExactFractionsAndSkipsEmptyMaster()
    {
        using var fixture = new LightroomCatalogFixture(version: 1504001);
        var root = Path.GetDirectoryName(fixture.CatalogPath)! + "\\";
        fixture.AddPhoto(1, root, "", "rt-plain.raf");
        fixture.AddDevelopSettings(1, CropBlob(), fileWidth: 4000, fileHeight: 3000,
            croppedWidth: 3062, croppedHeight: 2296);
        fixture.AddPhoto(2, root, "", "empty.raf");
        fixture.AddDevelopSettings(2, "s = { Exposure = 0 }");
        fixture.CloseWriter();

        var record = Assert.Single((await new LightroomCatalogReader()
            .ReadAsync(fixture.CatalogPath)).Records);

        Assert.Equal(XmpFactKind.Matched, record.Crop!.Kind);
        Assert.Equal(0.018746, record.Crop.Crop!.Left, 6);
        Assert.Equal(0.153254, record.Crop.Crop.Top, 6);
        Assert.Equal(0.784155, record.Crop.Crop.Right, 6);
        Assert.Equal(0.918663, record.Crop.Crop.Bottom, 6);
    }

    [Theory]
    [InlineData("CropAngle = 1,", XmpFactKind.Unsupported)]
    [InlineData("CropConstrainToWarp = 1,", XmpFactKind.Unsupported)]
    [InlineData("CropConstrainToWarp = false,", XmpFactKind.Matched)]
    [InlineData("", XmpFactKind.Matched)]
    public void ParseCrop_RejectsAngleAndWarp(string extra, XmpFactKind expected)
    {
        var fact = LightroomCatalogReader.ParseCrop(CropBlob(extra), "AB",
            4000, 3000, 3062, 2296);

        Assert.Equal(expected, fact.Kind);
    }

    [Fact]
    public void ParseCrop_RejectsCrossCheckUnknownOrientationAndDuplicateTopLevelKey()
    {
        Assert.Equal(XmpFactKind.Unsupported,
            LightroomCatalogReader.ParseCrop(CropBlob(), "AB", 4000, 3000, 3000, 2296).Kind);
        Assert.Equal(XmpFactKind.Unsupported,
            LightroomCatalogReader.ParseCrop(CropBlob(), "CD", 4000, 3000, 3062, 2296).Kind);
        Assert.Equal(XmpFactKind.Unsupported,
            LightroomCatalogReader.ParseCrop(CropBlob(), "BC", 4000, 3000, 3062, 2296).Kind);
        Assert.Equal(XmpFactKind.Unsupported,
            LightroomCatalogReader.ParseCrop(CropBlob("CropLeft = 0.018746,"), "AB",
                4000, 3000, 3062, 2296).Kind);
        Assert.Equal(XmpFactKind.Unsupported,
            LightroomCatalogReader.ParseCrop("s = {\nCropLeft: 0.1\n}", "AB",
                4000, 3000, 3062, 2296).Kind);
    }

    [Fact]
    public void ParseCrop_CompactTableWithCropAssignmentsIsUnsupported()
    {
        var fact = LightroomCatalogReader.ParseCrop(
            "s = { CropLeft = 0.018746, CropTop = 0.153254, CropRight = 0.784155, CropBottom = 0.918663 }",
            "AB", 4000, 3000, 3062, 2296);

        Assert.Equal(XmpFactKind.Unsupported, fact.Kind);
    }

    [Fact]
    public void ParseCrop_NoEdgesOrFullFrameIsEmpty()
    {
        Assert.Equal(XmpFactKind.Empty,
            LightroomCatalogReader.ParseCrop("s = { Exposure = 0 }", "AB",
                null, null, null, null).Kind);
        Assert.Equal(XmpFactKind.Empty,
            LightroomCatalogReader.ParseCrop("s = {\nCropLeft = 0,\nCropTop = 0,\nCropRight = 1,\nCropBottom = 1\n}",
                "AB", null, null, null, null).Kind);
    }

    [Fact]
    public async Task Read_MissingCropSchemaWarnsWithoutThrowing()
    {
        using var fixture = new LightroomCatalogFixture();
        var root = Path.GetDirectoryName(fixture.CatalogPath)! + "\\";
        fixture.AddPhoto(1, root, "", "photo.jpg", rating: 3);
        fixture.Execute("DROP TABLE Adobe_imageDevelopSettings;");
        fixture.CloseWriter();

        var result = await new LightroomCatalogReader().ReadAsync(fixture.CatalogPath);

        Assert.Contains(result.SchemaWarnings, warning => warning.Contains("Crops are unavailable"));
        Assert.Null(Assert.Single(result.Records).Crop);
    }

    [Fact]
    public void ParseCrop_IgnoresNestedKeysAndQuotedBracesWithoutThrowing()
    {
        var blob = CropBlob("Nested = { CropLeft = 9 },\nCaption = \"{still text}\",");

        var fact = LightroomCatalogReader.ParseCrop(blob, "AB",
            4000, 3000, 3062, 2296);

        Assert.Equal(XmpFactKind.Matched, fact.Kind);
        for (var index = 0; index < 200; index++)
            _ = LightroomCatalogReader.ParseCrop(blob[..(index % blob.Length)], "AB",
                4000, 3000, 3062, 2296);
    }

    [Fact]
    public void ParseCrop_FuzzedArchivedBlobsNeverThrow()
    {
        var blobs = new List<string>();
        foreach (var name in new[]
                 { "lrcrop-v3-rt-plain.lua", "lrcrop-v3-rt-warpflag.lua" })
        {
            blobs.Add(File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "assets", "lightroom", name)));
        }
        var archiveRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "lrcrop-fixture-source"));
        foreach (var name in new[] { "trial-v1.lrcat", "trial-v2.lrcat", "trial.lrcat" })
        {
            var path = Path.Combine(archiveRoot, name);
            if (!File.Exists(path)) continue;
            var builder = new SqliteConnectionStringBuilder
                { DataSource = path, Mode = SqliteOpenMode.ReadOnly };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT text FROM Adobe_imageDevelopSettings WHERE text IS NOT NULL;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) blobs.Add(reader.GetString(0));
        }
        foreach (var blob in blobs)
        {
            for (var index = 0; index < blob.Length; index += 11)
            {
                _ = LightroomCatalogReader.ParseCrop(blob[..index], "AB",
                    4000, 3000, 3062, 2296);
                _ = LightroomCatalogReader.ParseCrop(blob.Remove(index, 1), "AB",
                    4000, 3000, 3062, 2296);
            }
        }
    }

    [Fact]
    public async Task ArchivedV3Smoke_HasPinnedCropFactsWhenFixtureIsPresent()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "lrcrop-fixture-source", "trial.lrcat"));
        if (!File.Exists(path)) return;
        Assert.Equal("2C19B3477504620EA526D128654CA4D5C53F07ECC5ED340FFBF12FCAC2ABA311",
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))));
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly };
        using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ds.text, i.orientation, ds.fileWidth, ds.fileHeight, ds.croppedWidth, ds.croppedHeight FROM Adobe_images i JOIN Adobe_imageDevelopSettings ds ON ds.image=i.id_local WHERE i.masterImage IS NULL;";
        using var reader = await command.ExecuteReaderAsync();
        var facts = new List<XmpFactKind>();
        while (await reader.ReadAsync())
            facts.Add(LightroomCatalogReader.ParseCrop(reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2), reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4), reader.IsDBNull(5) ? null : reader.GetDouble(5)).Kind);
        Assert.Equal(7, facts.Count(kind => kind == XmpFactKind.Matched));
        Assert.Equal(12, facts.Count(kind => kind == XmpFactKind.Empty));
        Assert.Single(facts, kind => kind == XmpFactKind.Unsupported);
    }
    [Fact]
    public async Task Read_ActiveWalRefusesWithoutChangingSourceOrSidecars()
    {
        using var fixture = new LightroomCatalogFixture(useWal: true);
        var root = Path.GetDirectoryName(fixture.CatalogPath)! + Path.DirectorySeparatorChar;
        fixture.AddPhoto(1, root, "", "photo.jpg", rating: 4, pick: 1, label: "Red");
        var before = fixture.CaptureSourceFiles();

        var error = await Assert.ThrowsAsync<IOException>(() =>
            new LightroomCatalogReader().ReadAsync(fixture.CatalogPath));

        Assert.Contains("Close Lightroom completely", error.Message);
        AssertSourceUnchanged(before, fixture.CaptureSourceFiles());
    }

    [Fact]
    public async Task Read_MapsVerdictsAndReportsVirtualCopyAndUnsupportedLabel()
    {
        using var fixture = new LightroomCatalogFixture();
        var root = Path.GetDirectoryName(fixture.CatalogPath)! + "\\";
        fixture.AddPhoto(1, root, "2026\\", "keeper.JPG", 4.4, 1, "blue");
        fixture.AddPhoto(2, root, "2026\\", "copy.JPG", null, -1, "Client Pick", true);
        fixture.CloseWriter();
        var before = fixture.CaptureSourceFiles();

        var result = await new LightroomCatalogReader().ReadAsync(fixture.CatalogPath);

        Assert.True(result.IsVerifiedVersion);
        Assert.Equal(13, result.MajorVersion);
        Assert.Equal(2, result.Records.Count);
        Assert.Equal(4, result.Records[0].Rating.Value);
        Assert.Equal(ColorLabel.Blue, result.Records[0].ColorLabel.Value);
        Assert.True(result.Records[1].IsVirtualCopy);
        Assert.Equal(CatalogImportFactKind.Unsupported, result.Records[1].ColorLabel.Kind);
        AssertSourceUnchanged(before, fixture.CaptureSourceFiles());
    }

    [Fact]
    public async Task Read_UnknownCompatibleVersionWarnsAndMissingAxisDegrades()
    {
        using var fixture = new LightroomCatalogFixture(
            version: 1600000, includeLabel: false);
        var root = Path.GetDirectoryName(fixture.CatalogPath)! + "\\";
        fixture.AddPhoto(1, root, "", "photo.jpg", rating: 3);
        fixture.CloseWriter();

        var result = await new LightroomCatalogReader().ReadAsync(fixture.CatalogPath);

        Assert.False(result.IsVerifiedVersion);
        Assert.False(result.CarriedAxes.HasFlag(AssessmentAxes.Label));
        Assert.Contains(result.SchemaWarnings, warning => warning.Contains("Color labels"));
        Assert.Equal(CatalogImportFactKind.NotCarried,
            Assert.Single(result.Records).ColorLabel.Kind);
    }

    [Fact]
    public async Task Read_AllEmptyMasterIsDiscarded()
    {
        using var fixture = new LightroomCatalogFixture();
        var root = Path.GetDirectoryName(fixture.CatalogPath)! + "\\";
        fixture.AddPhoto(1, root, "", "photo.jpg", rating: 0);
        fixture.CloseWriter();

        Assert.Empty((await new LightroomCatalogReader()
            .ReadAsync(fixture.CatalogPath)).Records);
    }

    [Fact]
    public async Task Read_MissingCoreSchemaRefusesClearlyAndLeavesSourceUntouched()
    {
        using var fixture = new LightroomCatalogFixture();
        fixture.Execute("DROP TABLE AgLibraryFile;");
        fixture.CloseWriter();
        var before = fixture.CaptureSourceFiles();

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new LightroomCatalogReader().ReadAsync(fixture.CatalogPath));

        Assert.Contains("not compatible", error.Message);
        Assert.Contains("AgLibraryFile", error.Message);
        AssertSourceUnchanged(before, fixture.CaptureSourceFiles());
    }

    [Fact]
    public async Task Read_CancellationLeavesSourceAndSidecarsUntouched()
    {
        using var fixture = new LightroomCatalogFixture();
        fixture.CloseWriter();
        var before = fixture.CaptureSourceFiles();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LightroomCatalogReader().ReadAsync(
                fixture.CatalogPath, cancellationToken: cancellation.Token));

        AssertSourceUnchanged(before, fixture.CaptureSourceFiles());
    }

    [Fact]
    public async Task Read_LockFileRefusesBeforeOpeningCatalog()
    {
        using var fixture = new LightroomCatalogFixture();
        fixture.CloseWriter();
        File.WriteAllText(fixture.CatalogPath + ".lock", "locked");

        var error = await Assert.ThrowsAsync<IOException>(() =>
            new LightroomCatalogReader().ReadAsync(fixture.CatalogPath));

        Assert.Equal("Close Lightroom before importing this catalog.", error.Message);
    }

    [Theory]
    [InlineData("Rouge")]
    [InlineData("Red, Blue")]
    [InlineData(" Red ")]
    public async Task Read_UnexpectedLabelTokensRemainUnsupported(string token)
    {
        using var fixture = new LightroomCatalogFixture();
        var root = Path.GetDirectoryName(fixture.CatalogPath)! + "\\";
        fixture.AddPhoto(1, root, "", "photo.jpg", label: token);
        fixture.CloseWriter();

        var record = Assert.Single((await new LightroomCatalogReader()
            .ReadAsync(fixture.CatalogPath)).Records);

        Assert.Equal(CatalogImportFactKind.Unsupported, record.ColorLabel.Kind);
        Assert.Equal(token, record.ColorLabel.SourceToken);
    }

    private static void AssertSourceUnchanged(
        IReadOnlyDictionary<string, LightroomCatalogFixture.FileStamp> expected,
        IReadOnlyDictionary<string, LightroomCatalogFixture.FileStamp> actual)
    {
        Assert.Equal(expected.Keys, actual.Keys);
        foreach (var path in expected.Keys)
        {
            Assert.Equal(expected[path].Exists, actual[path].Exists);
            Assert.Equal(expected[path].Length, actual[path].Length);
            Assert.Equal(expected[path].LastWriteUtc, actual[path].LastWriteUtc);
            Assert.Equal(expected[path].Bytes, actual[path].Bytes);
        }
    }

    private static string CropBlob(string extra = "") => $$"""
        s = { AILook = {  },
        CropBottom = 0.918663,
        CropLeft = 0.018746,
        CropRight = 0.784155,
        CropTop = 0.153254,
        {{extra}}
        Look = { Parameters = { CropLeft = 9 }, Caption = "quoted } brace" },
        PerspectiveVertical = 0 }
        """;
}
