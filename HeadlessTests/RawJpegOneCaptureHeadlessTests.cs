using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawJpegOneCaptureHeadlessTests
{
    [AvaloniaFact]
    public async Task Switch_PreservesZoomAndNormalizedCenter()
    {
        await using var context = await CreateContextAsync(new CountingPairLoader());
        var (window, vm) = context;
        var viewer = window.FindControl<DevelopViewerPane>(
            "DevelopViewerPane")!.Viewer;
        await PumpUntilAsync(() => viewer.Source != null);
        var target = new NormalizedViewport(new NormalizedPoint(0.7, 0.3), 2);
        vm.ApplyManualZoom(viewer.GetFitZoomLevel() * target.ZoomRelativeToFit);
        viewer.ApplyNormalizedViewport(target);
        await PumpUntilAsync(() =>
            Math.Abs(viewer.CaptureNormalizedViewport().ZoomRelativeToFit - 2) < 0.001);
        var before = viewer.CaptureNormalizedViewport();

        vm.SwitchCaptureMemberCommand.Execute(null);

        await PumpUntilAsync(() =>
            vm.SelectedImage?.IsRaw == true &&
            viewer.Source != null &&
            Math.Abs(viewer.CaptureNormalizedViewport().ZoomRelativeToFit -
                before.ZoomRelativeToFit) < 0.001);
        var after = viewer.CaptureNormalizedViewport();
        Assert.InRange(Math.Abs(after.Center.X - before.Center.X), 0, 1.0 / 400);
        Assert.InRange(Math.Abs(after.Center.Y - before.Center.Y), 0, 1.0 / 400);
        Assert.InRange(
            Math.Abs(after.ZoomRelativeToFit - before.ZoomRelativeToFit),
            0,
            0.001);
    }

    [AvaloniaFact]
    public async Task RapidDoubleSwitch_PreservesOriginalViewport()
    {
        await using var context = await CreateContextAsync(new CountingPairLoader());
        var (window, vm) = context;
        var viewer = window.FindControl<DevelopViewerPane>(
            "DevelopViewerPane")!.Viewer;
        await PumpUntilAsync(() => viewer.Source != null);
        var jpeg = vm.SelectedImage;
        var target = new NormalizedViewport(new NormalizedPoint(0.7, 0.3), 2);
        vm.ApplyManualZoom(viewer.GetFitZoomLevel() * target.ZoomRelativeToFit);
        viewer.ApplyNormalizedViewport(target);
        await PumpUntilAsync(() =>
            Math.Abs(viewer.CaptureNormalizedViewport().ZoomRelativeToFit - 2) < 0.001);
        var before = viewer.CaptureNormalizedViewport();

        vm.SwitchCaptureMemberCommand.Execute(null);
        vm.SwitchCaptureMemberCommand.Execute(null);

        await PumpUntilAsync(() =>
            ReferenceEquals(vm.SelectedImage, jpeg) &&
            viewer.Source != null &&
            Math.Abs(viewer.CaptureNormalizedViewport().ZoomRelativeToFit -
                before.ZoomRelativeToFit) < 0.001);
        var after = viewer.CaptureNormalizedViewport();
        Assert.InRange(Math.Abs(after.Center.X - before.Center.X), 0, 1.0 / 400);
        Assert.InRange(Math.Abs(after.Center.Y - before.Center.Y), 0, 1.0 / 400);
        Assert.InRange(
            Math.Abs(after.ZoomRelativeToFit - before.ZoomRelativeToFit),
            0,
            0.001);
    }

    [AvaloniaFact]
    public async Task PaintThenNavigate_DoesNotRestoreViewportOntoNextCapture()
    {
        await using var context = await CreateContextAsync(
            new CountingPairLoader(), "capture.jpg", "capture.dng", "plain.jpg");
        var (window, vm) = context;
        var viewer = window.FindControl<DevelopViewerPane>(
            "DevelopViewerPane")!.Viewer;
        await PumpUntilAsync(() => viewer.Source != null);
        var plain = vm.Browse.AllImages.Single(image =>
            image.FileName == "plain.jpg");
        var target = new NormalizedViewport(new NormalizedPoint(0.7, 0.3), 2);
        vm.ApplyManualZoom(viewer.GetFitZoomLevel() * target.ZoomRelativeToFit);
        viewer.ApplyNormalizedViewport(target);
        await PumpUntilAsync(() =>
            Math.Abs(viewer.CaptureNormalizedViewport().ZoomRelativeToFit - 2) < 0.001);
        var releaseNextPaint = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var restore = vm.RestoreDevelopViewport!;
        vm.RestoreDevelopViewport = (paintedImage, viewport) =>
        {
            restore(paintedImage, viewport);
            vm.ImageService.Previews.SourceWorkGateAsync = () =>
                releaseNextPaint.Task.WaitAsync(TestWaits.Condition);
            vm.SelectedImage = plain;
        };

        try
        {
            vm.SwitchCaptureMemberCommand.Execute(null);
            await PumpUntilAsync(() => ReferenceEquals(vm.SelectedImage, plain));

            Assert.True(vm.IsZoomFitMode);
        }
        finally
        {
            vm.ImageService.Previews.SourceWorkGateAsync = null;
            releaseNextPaint.TrySetResult();
        }
    }

    [AvaloniaFact]
    public async Task Assessment_EnqueuesBothPrimarySidecars()
    {
        using var root = new TemporaryDirectory();
        var folder = CreateFolder(root.Path, "capture.jpg", "capture.dng");
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await catalog.SetAppSettingAsync(
            MainWindowViewModel.XmpSidecarModeKey,
            XmpSidecarMode.ReadWrite.ToString());
        await using var vm = CreateViewModel(catalog, new NullBaseLoader());
        await vm.RestoreXmpSettingsAsync();
        await vm.LoadFolderAsync(folder);
        vm.SelectedImage = vm.Browse.AllImages.Single(image =>
            image.FileName == "capture.jpg");

        await vm.SetRatingCommand.ExecuteAsync(4);

        var jpegSidecar = Path.Combine(folder, "capture.jpg.xmp");
        var rawSidecar = Path.Combine(folder, "capture.dng.xmp");
        await TestWaits.UntilAsync(() =>
            File.Exists(jpegSidecar) && File.Exists(rawSidecar));
        Assert.Contains("xmp:Rating=\"4\"", await File.ReadAllTextAsync(jpegSidecar));
        Assert.Contains("xmp:Rating=\"4\"", await File.ReadAllTextAsync(rawSidecar));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Toggle_IsPresentDisabledWhenUnpairedAndTracksMember()
    {
        await using var context = await CreateContextAsync(
            new NullBaseLoader(), "capture.jpg", "capture.dng", "plain.jpg");
        var (window, vm) = context;
        var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
        var toggle = pane.FindControl<ToggleButton>("RawJpegSwitchButton")!;
        var beforeAfter = pane.FindControl<ToggleButton>("BeforeAfterSplitButton")!;
        var actions = Assert.IsType<StackPanel>(
            pane.FindControl<Border>("DevelopControlBar")!.Child);
        Assert.Equal(
            actions.Children.IndexOf(beforeAfter) + 1,
            actions.Children.IndexOf(toggle));
        var jpeg = vm.Browse.AllImages.Single(image =>
            image.FileName == "capture.jpg");
        var raw = vm.Browse.AllImages.Single(image =>
            image.FileName == "capture.dng");
        var plain = vm.Browse.AllImages.Single(image =>
            image.FileName == "plain.jpg");

        vm.SelectedImage = plain;
        Dispatcher.UIThread.RunJobs();
        Assert.True(toggle.IsVisible);
        Assert.False(toggle.IsEffectivelyEnabled);

        vm.SelectedImage = jpeg;
        Dispatcher.UIThread.RunJobs();
        Assert.True(toggle.IsEffectivelyEnabled);
        Assert.False(toggle.IsChecked);
        vm.SwitchCaptureMemberCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(raw, vm.SelectedImage);
        Assert.True(toggle.IsChecked);
        Assert.StartsWith("capture.dng", vm.ActiveFileName);

        toggle.Command!.Execute(toggle.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(jpeg, vm.SelectedImage);
        Assert.False(toggle.IsChecked);
        Assert.StartsWith("capture.jpg", vm.ActiveFileName);
    }

    private static async Task<WindowContext> CreateContextAsync(
        IBaseImageLoader loader,
        params string[] names)
    {
        var root = new TemporaryDirectory();
        var folder = CreateFolder(
            root.Path,
            names.Length == 0 ? ["capture.jpg", "capture.dng"] : names);
        var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog, loader);
        await vm.LoadFolderAsync(folder);
        vm.SelectedImage = vm.Browse.AllImages.Single(image =>
            image.FileName == "capture.jpg");
        vm.IsDevelopMode = true;
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new WindowContext(root, catalog, vm, window);
    }

    private static MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        IBaseImageLoader loader)
    {
        var viewModel = new MainWindowViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            postSelection: action => action());
        viewModel.RestoreShowCapturePairs(true);
        return viewModel;
    }

    private static string CreateFolder(string root, params string[] names)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "photos"));
        foreach (var name in names)
            TestImages.WriteJpeg(Path.Combine(folder.FullName, name));
        return folder.FullName;
    }

    private static Task PumpUntilAsync(Func<bool> condition) =>
        TestWaits.UntilAsync(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return condition();
        });

    private sealed class WindowContext(
        TemporaryDirectory root,
        CatalogService catalog,
        MainWindowViewModel viewModel,
        MainWindow window) : IAsyncDisposable
    {
        public void Deconstruct(out MainWindow windowValue, out MainWindowViewModel vm)
        {
            windowValue = window;
            vm = viewModel;
        }

        public async ValueTask DisposeAsync()
        {
            window.DataContext = null;
            window.Close();
            await viewModel.DisposeAsync();
            catalog.Dispose();
            root.Dispose();
        }
    }
}
