using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogImportViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(), $"happy-photon-import-vm-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task Apply_UpdatesLiveFiltersSelectionAndViewportWithoutFolderReload()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        var firstPath = Path.Combine(_root, "first.jpg");
        var secondPath = Path.Combine(_root, "second.jpg");
        var states = await catalog.LoadOrCreateImageStatesAsync([firstPath, secondPath]);
        await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(states[firstPath].CatalogId,
                AssessmentAxes.Flag, Flag: ImageFlag.Picked),
            new AssessmentMutation(states[secondPath].CatalogId,
                AssessmentAxes.Flag, Flag: ImageFlag.Picked)
        ], AssessmentAxes.None);
        states = await catalog.LoadImageStatesAsync([firstPath, secondPath]);
        var first = ToImage(firstPath, states[firstPath]);
        var second = ToImage(secondPath, states[secondPath]);
        first.IsSelected = true;
        second.IsSelected = true;
        var vm = CreateViewModel(catalog);
        vm.Library.SetImages([first, second]);
        vm.Library.FlagFilter = FlagFilter.Picked;
        vm.SelectedImage = first;
        var restoredAnchor = string.Empty;
        vm.CaptureLibraryViewportAnchor = () => secondPath;
        vm.RestoreLibraryViewportAnchor = path => restoredAnchor = path;
        var source = Source(firstPath, secondPath);
        var import = new CatalogImportService(catalog);
        var preview = await import.CreatePreviewAsync(
            source,
            new Dictionary<string, string> { ["D:/Photos/"] = _root },
            CatalogImportPolicy.LightroomWins);

        await vm.ApplyCatalogImportAsync(preview);

        Assert.Equal(ImageFlag.Picked, first.Flag);
        Assert.Equal(ImageFlag.Rejected, second.Flag);
        Assert.Same(first, Assert.Single(vm.Library.VisibleImages));
        Assert.True(first.IsSelected);
        Assert.False(second.IsSelected);
        Assert.Same(first, vm.SelectedImage);
        Assert.Equal(secondPath, restoredAnchor);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task Adoption_SkipsLiveImageWhoseRevisionMovedAfterCommit()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        var path = Path.Combine(_root, "photo.jpg");
        var state = (await catalog.LoadOrCreateImageStatesAsync([path]))[path];
        var image = ToImage(path, state);
        var vm = CreateViewModel(catalog);
        vm.Library.SetImages([image]);
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

    [Fact]
    public async Task FirstRunCompletion_SelectsFolderContainingImportedPhoto()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos"));
        var shoot = Directory.CreateDirectory(Path.Combine(photos.FullName, "shoot"));
        var importedPath = Path.Combine(shoot.FullName, "keeper.jpg");
        File.WriteAllBytes(importedPath, [1]);
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        var vm = CreateViewModel(catalog);

        var completion = vm.FindFirstRunImportCompletion(
            Preview(photos.FullName, importedPath), Report(1));

        Assert.NotNull(completion);
        Assert.Equal(photos.FullName, completion.BrowsingRootPath);
        Assert.Equal(shoot.FullName, completion.InitiallySelectedFolderPath);
        Assert.Null(completion.Message);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task FirstRunCompletion_UnrelatedFilesUseAccurateUnavailableMessage()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos"));
        var shoot = Directory.CreateDirectory(Path.Combine(photos.FullName, "shoot"));
        File.WriteAllBytes(Path.Combine(shoot.FullName, "unrelated.jpg"), [1]);
        var missingImportedPath = Path.Combine(shoot.FullName, "keeper.jpg");
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        var vm = CreateViewModel(catalog);

        var completion = vm.FindFirstRunImportCompletion(
            Preview(photos.FullName, missingImportedPath), Report(1));

        Assert.NotNull(completion);
        Assert.Equal(photos.FullName, completion.BrowsingRootPath);
        Assert.Equal(photos.FullName, completion.InitiallySelectedFolderPath);
        Assert.Contains("couldn't automatically find", completion.Message);
        await vm.DisposeAsync();
    }

    private LightroomCatalogContents Source(string firstPath, string secondPath) =>
        new(Path.Combine(_root, "source.lrcat"), 1303001, 13, true,
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

    private static MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        new(catalog, baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration));

    private CatalogImportPreview Preview(string root, string importedPath) =>
        new(Path.Combine(_root, "source.lrcat"),
            CatalogImportPolicy.LightroomWins,
            new Dictionary<string, string> { ["D:/Photos/"] = root },
            [], Report(1), "key", null, "{}", [importedPath]);

    private static CatalogImportReport Report(int matched) =>
        new(matched, matched, matched, 0, matched, 0, 0, 0,
            new(0, 0, 0, 0, 0),
            new(0, 0, 0, 0, 0),
            new(0, 0, 0, 0, 0),
            new Dictionary<string, int>(), [], [], false);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
