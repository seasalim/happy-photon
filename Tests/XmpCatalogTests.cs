using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class XmpCatalogTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(), $"happy-photon-xmp-catalog-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task Mutation_BumpsRevisionAndSetsThenRevisionGuardsPendingMask()
    {
        using var catalog = await CreateCatalogAsync();
        var id = await catalog.GetOrCreateImageAsync(Path.Combine(_root, "a.jpg"));

        var snapshot = Assert.Single(await catalog.MutateAssessmentsAsync(
            [new AssessmentMutation(id, AssessmentAxes.Rating, Rating: 4)],
            AssessmentAxes.Rating));

        Assert.Equal(1, snapshot.Revision);
        Assert.Equal(AssessmentAxes.Rating, snapshot.PendingAxes);
        Assert.False(await catalog.ClearPendingAxesAsync(
            id, snapshot.Revision + 1, AssessmentAxes.Rating));
        Assert.True(await catalog.ClearPendingAxesAsync(
            id, snapshot.Revision, AssessmentAxes.Rating));
        Assert.Equal(AssessmentAxes.None,
            Assert.Single(await catalog.LoadAssessmentSnapshotsAsync([id])).PendingAxes);
    }

    [Fact]
    public async Task Reconcile_AdoptsOnlyNewerNonPendingAxes()
    {
        using var catalog = await CreateCatalogAsync();
        var photo = Path.Combine(_root, "photo.cr3");
        var id = await catalog.GetOrCreateImageAsync(photo);
        var local = Assert.Single(await catalog.MutateAssessmentsAsync(
            [new AssessmentMutation(id, AssessmentAxes.Rating, Rating: 1)],
            AssessmentAxes.Rating));
        var sidecar = photo + ".xmp";
        var document = XmpSidecarDocument.Create();
        document.Root!.Descendants(XmpSidecarDocument.Rdf + "Description")
            .Single().SetAttributeValue(XmpSidecarDocument.Xmp + "Rating", "5");
        document.Root.Descendants(XmpSidecarDocument.Rdf + "Description")
            .Single().SetAttributeValue(XmpSidecarDocument.Xmp + "Label", "Blue");
        document.Save(sidecar);
        File.SetLastWriteTimeUtc(sidecar, local.AssessedUtc.AddSeconds(3));

        var result = await new XmpSidecarReconciler(catalog).ReconcileAsync(
            [photo], ColorLabelNames.Defaults, XmpSidecarNaming.FullName);
        var state = (await catalog.LoadImageStatesAsync([photo]))[photo];

        Assert.Equal(1, state.Rating);
        Assert.Equal(ColorLabel.Blue, state.ColorLabel);
        Assert.Equal(AssessmentAxes.Label,
            Assert.Single(result.Adoptions).AdoptedAxes);
    }

    [Fact]
    public async Task Reconcile_RegistersAndAdoptsImageMissingFromFreshCatalog()
    {
        using var catalog = await CreateCatalogAsync();
        var photo = Path.Combine(_root, "fresh.cr3");
        var sidecar = photo + ".xmp";
        var document = XmpSidecarDocument.Create();
        document.Root!.Descendants(XmpSidecarDocument.Rdf + "Description")
            .Single().SetAttributeValue(XmpSidecarDocument.Xmp + "Rating", "4");
        document.Save(sidecar);
        File.SetLastWriteTimeUtc(
            sidecar, new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc));

        var result = await new XmpSidecarReconciler(catalog).ReconcileAsync(
            [photo], ColorLabelNames.Defaults, XmpSidecarNaming.FullName);

        var state = Assert.Single(await catalog.LoadImageStatesAsync([photo])).Value;
        Assert.Equal(4, state.Rating);
        var adoption = Assert.Single(result.Adoptions);
        Assert.Equal(state.CatalogId, adoption.Snapshot.ImageId);
        Assert.Equal(AssessmentAxes.Rating, adoption.AdoptedAxes);
        Assert.False(File.Exists(photo));
    }

    [Fact]
    public async Task Writer_CreatesBesidePhotoAndClearsExactPendingAxis()
    {
        using var catalog = await CreateCatalogAsync();
        var photo = Path.Combine(_root, "cloud-only.cr3");
        var id = await catalog.GetOrCreateImageAsync(photo);
        var snapshot = Assert.Single(await catalog.MutateAssessmentsAsync(
            [new AssessmentMutation(id, AssessmentAxes.Flag,
                Flag: ImageFlag.Picked)], AssessmentAxes.Flag));
        await using var writer = new XmpSidecarWriter(
            catalog, ColorLabelNames.Defaults);
        writer.Start();

        Assert.True(writer.TryEnqueue(
            snapshot, AssessmentAxes.Flag, [photo], XmpSidecarNaming.FullName));
        await writer.DrainAsync();

        var sidecar = photo + ".xmp";
        Assert.True(File.Exists(sidecar));
        var facts = XmpSidecarDocument.ReadFacts(
            System.Xml.Linq.XDocument.Load(sidecar), ColorLabelNames.Defaults);
        Assert.Equal(ImageFlag.Picked, facts.Flag.Value);
        Assert.Equal(AssessmentAxes.None,
            Assert.Single(await catalog.LoadAssessmentSnapshotsAsync([id])).PendingAxes);
        Assert.False(File.Exists(photo));
    }

    [Fact]
    public async Task FreshSchema_HasFullAssessmentShapeAtVersionTwo()
    {
        using var catalog = await CreateCatalogAsync();
        await using var connection = new SqliteConnection(
            $"Data Source={Path.Combine(_root, "catalog", "catalog.db")};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(image_assessments);";
        var columns = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
        Assert.Equal(["image_id", "revision", "assessed_utc", "pending_axes"], columns);
    }

    [Fact]
    public async Task CatalogSettings_RestoreModeAndNamingWithSafeDefaults()
    {
        using var catalog = await CreateCatalogAsync();
        await using (var defaults = new MainWindowViewModel(catalog))
        {
            await defaults.RestoreXmpSettingsAsync();
            Assert.Equal(XmpSidecarMode.Off, defaults.XmpSidecarMode);
            Assert.Equal(XmpSidecarNaming.FullName, defaults.XmpSidecarNaming);
        }
        await catalog.SetAppSettingsAsync(new Dictionary<string, string?>
        {
            [MainWindowViewModel.XmpSidecarModeKey] = "read",
            [MainWindowViewModel.XmpSidecarNamingKey] = "basename"
        });
        await using var restored = new MainWindowViewModel(catalog);

        await restored.RestoreXmpSettingsAsync();

        Assert.Equal(XmpSidecarMode.Read, restored.XmpSidecarMode);
        Assert.Equal(XmpSidecarNaming.BaseName, restored.XmpSidecarNaming);
    }

    private async Task<CatalogService> CreateCatalogAsync()
    {
        var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        return catalog;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }
}
