using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LightroomCatalogReaderTests
{
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
    public async Task Read_ZeroRatingIsEmpty()
    {
        using var fixture = new LightroomCatalogFixture();
        var root = Path.GetDirectoryName(fixture.CatalogPath)! + "\\";
        fixture.AddPhoto(1, root, "", "photo.jpg", rating: 0);
        fixture.CloseWriter();

        var record = Assert.Single((await new LightroomCatalogReader()
            .ReadAsync(fixture.CatalogPath)).Records);

        Assert.Equal(CatalogImportFactKind.Empty, record.Rating.Kind);
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
}
