using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawJpegOneCaptureTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("raw-jpeg-one-capture");

    [Theory]
    [InlineData(AssessmentAxis.Flag)]
    [InlineData(AssessmentAxis.Rating)]
    [InlineData(AssessmentAxis.Label)]
    public async Task PairedAssessment_UpdatesBothPrimaryRowsButNotRawVersion(
        AssessmentAxis axis)
    {
        var folder = CreateFolder("capture.jpg", "capture.dng");
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        var states = await catalog.LoadOrCreateImageStatesAsync(
            Directory.GetFiles(folder));
        var rawId = states.Single(entry => entry.Key.EndsWith(".dng"))
            .Value.Single().CatalogId;
        Assert.NotNull(await catalog.CreateVersionAsync(rawId));
        await using var vm = CreateViewModel(catalog);
        await vm.LoadFolderAsync(folder);
        var jpeg = Find(vm, "capture.jpg", 1);
        var raw = Find(vm, "capture.dng", 1);
        var rawV2 = Find(vm, "capture.dng", 2);
        raw.CatalogId = 0;
        vm.SelectedImage = jpeg;
        vm.IsDevelopMode = true;

        await ExecuteAsync(vm, axis);

        Assert.NotEqual(0, raw.CatalogId);
        AssertValue(jpeg, axis, expected: true);
        AssertValue(raw, axis, expected: true);
        AssertValue(rawV2, axis, expected: false);
        var persisted = await catalog.LoadImageStatesAsync(
            [jpeg.FilePath, raw.FilePath]);
        AssertState(persisted[jpeg.FilePath].Single(), axis, expected: true);
        AssertState(persisted[raw.FilePath].Single(state => state.Version == 1),
            axis, expected: true);
        AssertState(persisted[raw.FilePath].Single(state => state.Version == 2),
            axis, expected: false);
        Assert.DoesNotContain("photos", vm.AssessmentFeedback ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PairedRating_MarksBothPrimarySnapshotsForSidecars()
    {
        var folder = CreateFolder("capture.jpg", "capture.dng");
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        await vm.LoadFolderAsync(folder);
        var jpeg = Find(vm, "capture.jpg", 1);
        vm.SelectedImage = jpeg;
        vm.XmpSidecarMode = XmpSidecarMode.ReadWrite;

        await vm.SetRatingCommand.ExecuteAsync(4);

        var states = await catalog.LoadImageStatesAsync(
            Directory.GetFiles(folder));
        Assert.All(states.Values.SelectMany(versions => versions), state =>
            Assert.Equal(AssessmentAxes.Rating, state.PendingAxes));
    }

    [Fact]
    public async Task JpegVersionAssessment_LeavesBothRawRowsUntouched()
    {
        var folder = CreateFolder("capture.jpg", "capture.dng");
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        var states = await catalog.LoadOrCreateImageStatesAsync(
            Directory.GetFiles(folder));
        var jpegId = states.Single(entry => entry.Key.EndsWith(".jpg"))
            .Value.Single().CatalogId;
        var rawId = states.Single(entry => entry.Key.EndsWith(".dng"))
            .Value.Single().CatalogId;
        Assert.NotNull(await catalog.CreateVersionAsync(jpegId));
        Assert.NotNull(await catalog.CreateVersionAsync(rawId));
        await using var vm = CreateViewModel(catalog);
        await vm.LoadFolderAsync(folder);
        var jpegV2 = Find(vm, "capture.jpg", 2);
        vm.SelectedImage = jpegV2;
        vm.IsDevelopMode = true;

        await vm.SetRatingCommand.ExecuteAsync(4);

        Assert.Equal(4, jpegV2.Rating);
        Assert.All(
            vm.Browse.AllImages.Where(image => image.FileName == "capture.dng"),
            raw => Assert.Equal(0, raw.Rating));
        var persisted = await catalog.LoadImageStatesAsync(
            [jpegV2.FilePath, Find(vm, "capture.dng", 1).FilePath]);
        Assert.All(
            persisted.Single(entry => entry.Key.EndsWith(".dng")).Value,
            raw => Assert.Equal(0, raw.Rating));
    }

    [Fact]
    public async Task MultiSelectAndPairsOff_KeepTileCountsAndTargetScope()
    {
        var folder = CreateFolder(
            "first.jpg", "first.dng", "second.jpg", "second.dng");
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        await vm.LoadFolderAsync(folder);
        var first = Find(vm, "first.jpg", 1);
        var second = Find(vm, "second.jpg", 1);
        vm.Browse.SelectOnly(first);
        vm.Browse.ToggleSelection(second);
        vm.SelectedImage = first;

        await vm.SetRatingCommand.ExecuteAsync(4);

        Assert.Equal("Rated 2 photos", vm.TransientStatus);
        Assert.All(vm.Browse.AllImages, image => Assert.Equal(4, image.Rating));

        vm.ShowCapturePairs = false;
        var firstRaw = Find(vm, "first.dng", 1);
        vm.Browse.SelectOnly(first);
        vm.SelectedImage = first;
        await vm.SetRatingCommand.ExecuteAsync(2);

        Assert.Equal(2, first.Rating);
        Assert.Equal(4, firstRaw.Rating);
    }

    [Fact]
    public async Task Switch_IsDevelopOnlyAndUsesVisibleCaptureForNavigation()
    {
        var folder = CreateFolder(
            "first.jpg", "first.dng", "second.jpg", "second.dng", "plain.jpg");
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        await vm.LoadFolderAsync(folder);
        var firstJpeg = Find(vm, "first.jpg", 1);
        var firstRaw = Find(vm, "first.dng", 1);
        var adjacentJpeg = vm.Browse.NextVisible(firstJpeg)!;
        var plain = Find(vm, "plain.jpg", 1);
        vm.SelectedImage = firstJpeg;

        Assert.False(vm.SwitchCaptureMemberCommand.CanExecute(null));
        vm.SwitchCaptureMemberCommand.Execute(null);
        Assert.Same(firstJpeg, vm.SelectedImage);

        vm.WorkspaceMode = WorkspaceMode.Export;
        vm.SwitchCaptureMemberCommand.Execute(null);
        Assert.Same(firstJpeg, vm.SelectedImage);
        vm.WorkspaceMode = WorkspaceMode.Browse;
        vm.Browse.SelectOnly(firstJpeg);
        vm.Browse.ToggleSelection(adjacentJpeg);
        vm.EnterCompareCommand.Execute(null);
        vm.SwitchCaptureMemberCommand.Execute(null);
        Assert.Same(firstJpeg, vm.SelectedImage);
        vm.ExitCompareCommand.Execute(null);

        vm.IsDevelopMode = true;
        Assert.True(vm.SwitchCaptureMemberCommand.CanExecute(null));
        vm.SwitchCaptureMemberCommand.Execute(null);
        Assert.Same(firstRaw, vm.SelectedImage);
        Assert.True(vm.IsViewingPairedRaw);
        Assert.True(vm.SelectNextImageCommand.CanExecute(null));

        vm.SelectNextImageCommand.Execute(null);
        Assert.Same(adjacentJpeg, vm.SelectedImage);
        vm.SelectedImage = plain;
        Assert.False(vm.SwitchCaptureMemberCommand.CanExecute(null));
        vm.IsFullScreenMode = true;
        vm.SelectedImage = firstJpeg;
        vm.SwitchCaptureMemberCommand.Execute(null);
        Assert.Same(firstJpeg, vm.SelectedImage);
    }

    [Fact]
    public async Task Switch_IsDisabledForVersionTwoAndVersionOneRoundTrips()
    {
        var folder = CreateFolder("capture.jpg", "capture.dng");
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        var states = await catalog.LoadOrCreateImageStatesAsync(
            Directory.GetFiles(folder));
        var jpegId = states.Single(entry => entry.Key.EndsWith(".jpg"))
            .Value.Single().CatalogId;
        Assert.NotNull(await catalog.CreateVersionAsync(jpegId));
        await using var vm = CreateViewModel(catalog);
        await vm.LoadFolderAsync(folder);
        var jpegV1 = Find(vm, "capture.jpg", 1);
        var jpegV2 = Find(vm, "capture.jpg", 2);
        var rawV1 = Find(vm, "capture.dng", 1);
        vm.IsDevelopMode = true;
        vm.SelectedImage = jpegV2;

        Assert.False(vm.SwitchCaptureMemberCommand.CanExecute(null));
        vm.SwitchCaptureMemberCommand.Execute(null);
        Assert.Same(jpegV2, vm.SelectedImage);

        vm.SelectedImage = jpegV1;
        Assert.True(vm.SwitchCaptureMemberCommand.CanExecute(null));
        vm.SwitchCaptureMemberCommand.Execute(null);
        Assert.Same(rawV1, vm.SelectedImage);
        vm.SwitchCaptureMemberCommand.Execute(null);
        Assert.Same(jpegV1, vm.SelectedImage);
    }

    [Fact]
    public async Task LeavingDevelopForBrowse_UsesPairedTileAsActiveImage()
    {
        var folder = CreateFolder("capture.jpg", "capture.dng");
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        await vm.LoadFolderAsync(folder);
        var jpeg = Find(vm, "capture.jpg", 1);
        var raw = Find(vm, "capture.dng", 1);
        vm.SelectedImage = jpeg;
        vm.IsDevelopMode = true;
        vm.SwitchCaptureMemberCommand.Execute(null);
        Assert.Same(raw, vm.SelectedImage);

        vm.SwitchToBrowseCommand.Execute(null);

        Assert.Same(jpeg, vm.SelectedImage);
    }

    [Fact]
    public async Task ExportFromPairedRaw_ActivatesItsTileWithinMultiSelection()
    {
        var folder = CreateFolder(
            "first.jpg", "first.dng", "second.jpg", "second.dng");
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        await vm.LoadFolderAsync(folder);
        var firstJpeg = Find(vm, "first.jpg", 1);
        var secondJpeg = Find(vm, "second.jpg", 1);
        var secondRaw = Find(vm, "second.dng", 1);
        vm.Browse.SelectOnly(firstJpeg);
        vm.Browse.ToggleSelection(secondJpeg);
        vm.SelectedImage = secondJpeg;
        vm.IsDevelopMode = true;
        vm.SwitchCaptureMemberCommand.Execute(null);
        Assert.Same(secondRaw, vm.SelectedImage);

        vm.SwitchToExportCommand.Execute(null);

        Assert.Equal(2, vm.ExportCaptures.Count);
        Assert.Same(secondJpeg, vm.ActiveExportCapture?.Image);
        Assert.Same(secondJpeg, vm.SelectedImage);
    }

    [Fact]
    public async Task RawAssessment_PreservesOrReplacesSelectionByItsTile()
    {
        var folder = CreateFolder(
            "first.jpg", "first.dng", "second.jpg", "second.dng");
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        await vm.LoadFolderAsync(folder);
        var firstJpeg = Find(vm, "first.jpg", 1);
        var firstRaw = Find(vm, "first.dng", 1);
        var secondJpeg = Find(vm, "second.jpg", 1);
        secondJpeg.Rating = 3;
        await catalog.SaveRatingAsync(secondJpeg.CatalogId, 3);
        vm.SelectedImage = firstJpeg;
        vm.IsDevelopMode = true;
        vm.SwitchCaptureMemberCommand.Execute(null);

        await vm.SetRatingCommand.ExecuteAsync(3);
        Assert.Same(firstRaw, vm.SelectedImage);

        vm.Browse.MinimumRating = 3;
        Assert.Same(firstRaw, vm.SelectedImage);
        await vm.SetRatingCommand.ExecuteAsync(1);
        Assert.Same(secondJpeg, vm.SelectedImage);
    }

    [Fact]
    public async Task Switch_RestoresOneShotNormalizedViewportAfterPaint()
    {
        var folder = CreateFolder("capture.jpg", "capture.dng");
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        await using var vm = CreateViewModel(catalog, new CountingPairLoader());
        await vm.LoadFolderAsync(folder);
        vm.SelectedImage = Find(vm, "capture.jpg", 1);
        vm.IsDevelopMode = true;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);
        var expected = new NormalizedViewport(new NormalizedPoint(0.7, 0.3), 2);
        NormalizedViewport? restored = null;
        vm.CaptureDevelopViewport = () => expected;
        vm.RestoreDevelopViewport = (_, viewport) => restored = viewport;

        vm.SwitchCaptureMemberCommand.Execute(null);

        await TestWaits.UntilAsync(() => restored != null);
        Assert.Equal(expected, restored);
        Assert.False(vm.IsZoomFitMode);
    }

    [Fact]
    public async Task ReturnSwitch_PaintsCachedMemberWhileFreshDecodeIsGated()
    {
        var folder = CreateFolder("capture.jpg", "capture.dng");
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        var loader = new CountingPairLoader();
        await using var vm = _fixture.CreateViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            postSelection: action => action(),
            timeProvider: new TestTimeProvider());
        vm.RestoreShowCapturePairs(true);
        await vm.LoadFolderAsync(folder);
        var jpeg = Find(vm, "capture.jpg", 1);
        var raw = Find(vm, "capture.dng", 1);
        vm.SelectedImage = jpeg;
        vm.IsDevelopMode = true;
        await TestWaits.UntilAsync(() =>
            loader.DecodeCount >= 1 && vm.PreviewImage != null);
        vm.SwitchCaptureMemberCommand.Execute(null);
        await TestWaits.UntilAsync(() =>
            ReferenceEquals(vm.SelectedImage, raw) &&
            loader.DecodeCount >= 2 && vm.PreviewImage != null);
        await using var cache = new PreviewCacheService(catalog);
        await TestWaits.UntilAsync(() => cache.IsCacheValid(jpeg));
        var freshStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFresh = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ImageService.Previews.SourceWorkGateAsync = () =>
        {
            freshStarted.TrySetResult();
            return releaseFresh.Task.WaitAsync(TestWaits.Condition);
        };

        try
        {
            vm.SwitchCaptureMemberCommand.Execute(null);
            await freshStarted.Task.WaitAsync(TestWaits.Condition);
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);
            Assert.Same(jpeg, vm.SelectedImage);
        }
        finally
        {
            vm.ImageService.Previews.SourceWorkGateAsync = null;
            releaseFresh.TrySetResult();
        }
    }

    private MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        IBaseImageLoader? loader = null)
    {
        var viewModel = _fixture.CreateViewModel(
            catalog,
            loader ?? new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            postSelection: action => action());
        viewModel.RestoreShowCapturePairs(true);
        return viewModel;
    }

    private string CreateFolder(params string[] names)
    {
        var folder = _fixture.Path(Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var name in names) TestImages.WriteJpeg(Path.Combine(folder, name));
        return folder;
    }

    private static ImageFile Find(MainWindowViewModel vm, string name, int version) =>
        vm.Browse.AllImages.Single(image =>
            image.FileName == name && image.Version == version);

    private static Task ExecuteAsync(MainWindowViewModel vm, AssessmentAxis axis) =>
        axis switch
        {
            AssessmentAxis.Flag => vm.TogglePickedImageCommand.ExecuteAsync(null),
            AssessmentAxis.Rating => vm.SetRatingCommand.ExecuteAsync(4),
            _ => vm.SetColorLabelCommand.ExecuteAsync(ColorLabel.Red)
        };

    private static void AssertValue(
        ImageFile image,
        AssessmentAxis axis,
        bool expected)
    {
        if (axis == AssessmentAxis.Flag)
            Assert.Equal(expected ? ImageFlag.Picked : ImageFlag.Unflagged, image.Flag);
        else if (axis == AssessmentAxis.Rating)
            Assert.Equal(expected ? 4 : 0, image.Rating);
        else
            Assert.Equal(expected ? ColorLabel.Red : ColorLabel.None, image.ColorLabel);
    }

    private static void AssertState(
        CatalogImageState state,
        AssessmentAxis axis,
        bool expected)
    {
        if (axis == AssessmentAxis.Flag)
            Assert.Equal(expected ? ImageFlag.Picked : ImageFlag.Unflagged, state.Flag);
        else if (axis == AssessmentAxis.Rating)
            Assert.Equal(expected ? 4 : 0, state.Rating);
        else
            Assert.Equal(expected ? ColorLabel.Red : ColorLabel.None, state.ColorLabel);
    }

    public void Dispose() => _fixture.Dispose();

    public enum AssessmentAxis
    {
        Flag,
        Rating,
        Label
    }

}
