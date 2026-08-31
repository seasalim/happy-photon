using System.Reflection;
using System.Collections.Concurrent;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class XmpCropViewModelTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("xmp-crop-vm");

    [Fact]
    public async Task DevelopSaveAndHistoryMoves_QueueOnlyGeometryChanges()
    {
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        var image = await CreateImageAsync(catalog, "history.jpg");
        await using var vm = await CreateReadWriteViewModelAsync(catalog, image);
        var writer = Writer(vm);

        var toneBefore = image.EditSettings.Clone();
        image.EditSettings.Exposure = 1;
        var promotions = 0;
        writer.BeforePromotionAsync = (_, _) =>
        {
            promotions++;
            return Task.CompletedTask;
        };
        await SaveAsync(vm, image, "Exposure +1.00", toneBefore);
        await writer.DrainAsync();

        Assert.Equal(0, promotions);
        Assert.False(File.Exists(image.FilePath + ".xmp"));
        Assert.Equal(0, (await SnapshotAsync(catalog, image)).Revision);

        var cropBefore = image.EditSettings.Clone();
        image.EditSettings.Crop = Crop(.1);
        await AssertQueuedWriteAsync(
            catalog, writer, image,
            () => SaveAsync(vm, image, "Crop", cropBefore),
            expectedRevision: 1,
            expectedFact: XmpFactKind.Matched);

        await AssertQueuedWriteAsync(
            catalog, writer, image,
            () => vm.UndoCommand.ExecuteAsync(null),
            expectedRevision: 2,
            expectedFact: XmpFactKind.Empty);

        await AssertQueuedWriteAsync(
            catalog, writer, image,
            () => vm.RedoCommand.ExecuteAsync(null),
            expectedRevision: 3,
            expectedFact: XmpFactKind.Matched);

        var original = vm.HistoryEntries.Single(entry => entry.Label == "Original");
        await AssertQueuedWriteAsync(
            catalog, writer, image,
            () => vm.JumpToHistoryStepCommand.ExecuteAsync(original),
            expectedRevision: 4,
            expectedFact: XmpFactKind.Empty);
    }

    [Fact]
    public async Task FolderSwitchDuringSave_KeepsCapturedCropWriteContext()
    {
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        var image = await CreateImageAsync(catalog, "switch.jpg");
        var other = new ImageFile(_fixture.Path("other.jpg"));
        await using var vm = await CreateReadWriteViewModelAsync(catalog, image);
        var writer = Writer(vm);
        var saveEntered = NewSignal();
        var releaseSave = NewSignal();
        catalog.EditHistoryWriteGateAsync = () =>
        {
            saveEntered.TrySetResult();
            return releaseSave.Task;
        };
        var promotionEntered = NewSignal();
        var releasePromotion = NewSignal();
        writer.BeforePromotionAsync = async (_, cancellationToken) =>
        {
            promotionEntered.TrySetResult();
            await releasePromotion.Task.WaitAsync(cancellationToken);
        };
        var before = image.EditSettings.Clone();
        image.EditSettings.Crop = Crop(.2);

        var save = SaveAsync(vm, image, "Crop", before);
        await saveEntered.Task.WaitAsync(TestWaits.Condition);
        vm.Browse.SetImages([other]);
        releaseSave.TrySetResult();
        await save;
        await promotionEntered.Task.WaitAsync(TestWaits.Condition);

        var pending = await SnapshotAsync(catalog, image);
        Assert.Equal(1, pending.Revision);
        Assert.Equal(AssessmentAxes.Crop, pending.PendingAxes);

        releasePromotion.TrySetResult();
        await writer.DrainAsync();
        Assert.True(File.Exists(image.FilePath + ".xmp"));
        Assert.Equal(XmpFactKind.Matched, ReadCrop(image.FilePath).Kind);
    }

    [Fact]
    public async Task CropAdoption_RefreshesEveryAffectedThumbnail()
    {
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        var first = await CreateImageAsync(catalog, "first.jpg");
        var second = await CreateImageAsync(catalog, "second.jpg");
        var availability = new RecordingUnavailable();
        await using var vm = _fixture.CreateViewModel(
            catalog,
            availabilityService: availability);
        vm.Browse.SetImages([first, second]);
        var owner = new CancellationTokenSource();
        SetField(vm, "_xmpReconcileCts", owner);
        var crop = Crop(.15);
        var result = new XmpReconcileResult(
        [
            Adoption(first, crop),
            Adoption(second, crop)
        ], []);

        InvokeApplyAdoptions(vm, result, owner);
        await TestWaits.UntilAsync(() =>
            vm.DirectThumbnailActivityCount == 0 &&
            availability.Probed.Contains(first.FilePath) &&
            availability.Probed.Contains(second.FilePath));

        Assert.Contains(first.FilePath, availability.Probed);
        Assert.Contains(second.FilePath, availability.Probed);
    }

    private async Task<MainWindowViewModel> CreateReadWriteViewModelAsync(
        CatalogService catalog,
        ImageFile image)
    {
        await catalog.SetAppSettingAsync(
            MainWindowViewModel.XmpSidecarModeKey,
            XmpSidecarMode.ReadWrite.ToString());
        var vm = _fixture.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);
        await vm.RestoreXmpSettingsAsync();
        return vm;
    }

    private async Task<ImageFile> CreateImageAsync(
        CatalogService catalog,
        string name)
    {
        var path = _fixture.Path(name);
        File.Copy(System.IO.Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "Tests", "assets", "srgb-reference.jpg"), path);
        var image = new ImageFile(path);
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        return image;
    }

    private static async Task AssertQueuedWriteAsync(
        CatalogService catalog,
        XmpSidecarWriter writer,
        ImageFile image,
        Func<Task> action,
        int expectedRevision,
        XmpFactKind expectedFact)
    {
        var entered = NewSignal();
        var release = NewSignal();
        writer.BeforePromotionAsync = async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };

        var operation = action();
        await entered.Task.WaitAsync(TestWaits.Condition);
        await operation;
        var pending = await SnapshotAsync(catalog, image);
        Assert.Equal(expectedRevision, pending.Revision);
        Assert.Equal(AssessmentAxes.Crop, pending.PendingAxes);

        release.TrySetResult();
        await writer.DrainAsync();
        writer.BeforePromotionAsync = null;
        Assert.Equal(AssessmentAxes.None,
            (await SnapshotAsync(catalog, image)).PendingAxes);
        Assert.Equal(expectedFact, ReadCrop(image.FilePath).Kind);
    }

    private static XmpReconcileAdoption Adoption(
        ImageFile image,
        CropRegion crop) => new(
        new AssessmentSnapshot(
            image.CatalogId,
            image.FilePath,
            ImageFlag.Unflagged,
            0,
            ColorLabel.None,
            image.AssessmentRevision + 1,
            DateTime.UtcNow,
            AssessmentAxes.None),
        AssessmentAxes.Crop,
        crop.Clone());

    private static Task SaveAsync(
        MainWindowViewModel vm,
        ImageFile image,
        string label,
        EditSettings before)
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            "SaveEditSettingsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(ImageFile), typeof(string), typeof(EditSettings)],
            null)!;
        return (Task)method.Invoke(vm, [image, label, before])!;
    }

    private static XmpSidecarWriter Writer(MainWindowViewModel vm) =>
        (XmpSidecarWriter)typeof(MainWindowViewModel).GetField(
            "_xmpWriter", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(vm)!;

    private static async Task<AssessmentSnapshot> SnapshotAsync(
        CatalogService catalog,
        ImageFile image) => Assert.Single(
        await catalog.LoadAssessmentSnapshotsAsync([image.CatalogId]));

    private static XmpFact<CropRegion> ReadCrop(string imagePath) =>
        XmpSidecarDocument.ReadFacts(
            System.Xml.Linq.XDocument.Load(imagePath + ".xmp"),
            ColorLabelNames.Defaults).Crop;

    private static void InvokeApplyAdoptions(
        MainWindowViewModel vm,
        XmpReconcileResult result,
        CancellationTokenSource owner)
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyXmpAdoptions",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(vm, [result, 0, owner]);
    }

    private static void SetField(
        MainWindowViewModel vm,
        string name,
        object value) => typeof(MainWindowViewModel).GetField(
            name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, value);

    private static CropRegion Crop(double left) => new()
    {
        Left = left,
        Top = .2,
        Right = .8,
        Bottom = .9
    };

    private static TaskCompletionSource NewSignal() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RecordingUnavailable : ISourceAvailabilityService
    {
        public ConcurrentBag<string> Probed { get; } = [];

        public SourceAvailability GetAvailability(string path)
        {
            Probed.Add(path);
            return SourceAvailability.Unavailable;
        }
    }

    public void Dispose() => _fixture.Dispose();
}
