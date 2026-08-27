using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogImportViewModelTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("import-vm");

    [Fact]
    public async Task ReadLightroomCatalog_IsAvailableOnCurrentPlatform()
    {
        using var fixture = new LightroomCatalogFixture();
        var sourceRoot = Path.GetDirectoryName(fixture.CatalogPath)! +
                         Path.DirectorySeparatorChar;
        fixture.AddPhoto(1, sourceRoot, "", "photo.jpg", rating: 4);
        fixture.CloseWriter();
        using var catalog = await _fx.CreateCatalogAsync("catalog");
        var vm = CreateViewModel(catalog);

        var source = await vm.ReadLightroomCatalogAsync(fixture.CatalogPath);

        Assert.Equal(13, source.MajorVersion);
        Assert.Single(source.Records);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task Apply_UpdatesLiveFiltersSelectionAndViewportWithoutFolderReload()
    {
        using var catalog = await _fx.CreateCatalogAsync("catalog");
        var firstPath = _fx.Path("first.jpg");
        var secondPath = _fx.Path("second.jpg");
        var states = await catalog.LoadOrCreateImageStatesAsync([firstPath, secondPath]);
        await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(states[firstPath].Single().CatalogId,
                AssessmentAxes.Flag, Flag: ImageFlag.Picked),
            new AssessmentMutation(states[secondPath].Single().CatalogId,
                AssessmentAxes.Flag, Flag: ImageFlag.Picked)
        ]);
        states = await catalog.LoadImageStatesAsync([firstPath, secondPath]);
        var first = ToImage(firstPath, states[firstPath].Single());
        var second = ToImage(secondPath, states[secondPath].Single());
        first.IsSelected = true;
        second.IsSelected = true;
        var vm = CreateViewModel(catalog);
        vm.Browse.SetImages([first, second]);
        vm.Browse.FlagFilter = FlagFilter.Picked;
        vm.SelectedImage = first;
        var restoredAnchor = string.Empty;
        vm.CaptureBrowseViewportAnchor = () => secondPath;
        vm.RestoreBrowseViewportAnchor = path => restoredAnchor = path;
        var source = Source(firstPath, secondPath);
        var import = new CatalogImportService(catalog, _ => true);
        var preview = await import.CreatePreviewAsync(
            source,
            new Dictionary<string, string> { ["D:/Photos/"] = _fx.Root },
            CatalogImportPolicy.LightroomWins);

        await vm.ApplyCatalogImportAsync(preview);

        Assert.Equal(ImageFlag.Picked, first.Flag);
        Assert.Equal(ImageFlag.Rejected, second.Flag);
        Assert.Same(first, Assert.Single(vm.Browse.VisibleImages));
        Assert.True(first.IsSelected);
        Assert.False(second.IsSelected);
        Assert.Same(first, vm.SelectedImage);
        Assert.Equal(secondPath, restoredAnchor);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task Adoption_SkipsLiveImageWhoseRevisionMovedAfterCommit()
    {
        using var catalog = await _fx.CreateCatalogAsync("catalog");
        var path = _fx.Path("photo.jpg");
        var state = (await catalog.LoadOrCreateImageStatesAsync([path]))[path].Single();
        var image = ToImage(path, state);
        var vm = CreateViewModel(catalog);
        vm.Browse.SetImages([image]);
        image.AssessmentRevision = 9;
        var snapshot = new AssessmentSnapshot(
            state.CatalogId, path, ImageFlag.Picked, 5, ColorLabel.Blue,
            1, DateTime.UtcNow, AssessmentAxes.None);

        vm.AdoptImportedAssessments([new CatalogImportAdoption(0, snapshot)]);

        Assert.Equal(ImageFlag.Unflagged, image.Flag);
        Assert.Equal(0, image.Rating);
        Assert.Equal(ColorLabel.None, image.ColorLabel);
        await vm.DisposeAsync();
    }

    private LightroomCatalogContents Source(string firstPath, string secondPath) =>
        new(_fx.Path("source.lrcat"), 1303001, 13, true,
            AssessmentAxes.All, [new CatalogSourceRoot("D:/Photos/", 2)],
            [
                new CatalogImportRecord(
                    "D:/Photos/", Path.GetFileName(firstPath),
                    CatalogImportFact<int>.Empty,
                    CatalogImportFact<ImageFlag>.Mapped(ImageFlag.Picked),
                    CatalogImportFact<ColorLabel>.Empty, false),
                new CatalogImportRecord(
                    "D:/Photos/", Path.GetFileName(secondPath),
                    CatalogImportFact<int>.Empty,
                    CatalogImportFact<ImageFlag>.Mapped(ImageFlag.Rejected),
                    CatalogImportFact<ColorLabel>.Empty, false)
            ], []);

    private static ImageFile ToImage(string path, CatalogImageState state) =>
        new(path)
        {
            CatalogId = state.CatalogId,
            Flag = state.Flag,
            Rating = state.Rating,
            ColorLabel = state.ColorLabel,
            AssessmentRevision = state.AssessmentRevision,
            AssessedUtc = state.AssessedUtc,
            PendingAssessmentAxes = state.PendingAxes
        };

    private MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        _fx.CreateViewModel(catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration));

    public void Dispose() => _fx.Dispose();
}
