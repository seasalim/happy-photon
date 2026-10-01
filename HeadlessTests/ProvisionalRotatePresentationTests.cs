using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ProvisionalRotatePresentationTests
{
    [AvaloniaFact]
    public async Task VisibleRangeMaskIsSuspendedUntilRealRotationPaints()
    {
        using var fixture = new CatalogVmFixture("rotate-mask");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = CreateVm(fixture, catalog);
        await SelectImage(fixture, catalog, vm, new EditSettings
        {
            Locals = [new()
            {
                Type = "brush",
                Luminance = new() { Enabled = true, Lower = .2 },
                Strokes = [new() { Points = [new(8192, 8192)] }]
            }]
        });
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.SelectedLocal = vm.Locals[0];
        await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
        var shown = Assert.IsType<WriteableBitmap>(vm.LocalRangeMask);
        var original = vm.PreviewImage;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ImageService.Previews.RenderGateAsync = () => release.Task;

        try
        {
            vm.RotateLeftCommand.Execute(null);
            var provisional = vm.PreviewImage;
            Assert.NotSame(original, provisional);
            Assert.False(vm.IsLocalMaskVisible);
            Assert.Null(vm.LocalRangeMask);
            Assert.Empty(vm.BrushOverlayStrokes);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(vm.LocalRangeMask);
            release.TrySetResult();
            await vm.PendingHistoryCommitTask!.WaitAsync(TestWaits.Condition);
            await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
            Assert.NotSame(provisional, vm.PreviewImage);
            Assert.True(vm.IsLocalMaskVisible);
            Assert.NotNull(vm.LocalRangeMask);
            Assert.NotSame(shown, vm.LocalRangeMask);
            Assert.Equal(vm.PreviewImage!.PixelSize, vm.LocalRangeMask!.PixelSize);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [AvaloniaFact]
    public async Task BoundWaveformClearsDuringProvisionalAndReturnsWithRealPaint()
    {
        using var fixture = new CatalogVmFixture("rotate-waveform");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = CreateVm(fixture, catalog);
        vm.SelectedScope = ScopeView.Waveform;
        await SelectImage(fixture, catalog, vm, new EditSettings());
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Dispatcher.UIThread.RunJobs();
        var waveform = window.GetVisualDescendants().OfType<WaveformView>()
            .Single(view => view.Name == "DevelopWaveform");
        Assert.NotNull(waveform.Waveform);
        Assert.True(HasTrace(waveform));
        var histogram = vm.Histogram;
        var original = vm.PreviewImage;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ImageService.Previews.RenderGateAsync = () => release.Task;

        try
        {
            vm.RotateLeftCommand.Execute(null);
            Assert.NotSame(original, vm.PreviewImage);
            Assert.Same(histogram, vm.Histogram);
            Assert.Null(waveform.Waveform);
            Assert.False(HasTrace(waveform));
            release.TrySetResult();
            await vm.PendingHistoryCommitTask!.WaitAsync(TestWaits.Condition);
            Assert.NotNull(waveform.Waveform);
            Assert.True(HasTrace(waveform));
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [AvaloniaFact]
    public async Task StationarySpotHoverReturnsAfterProvisionalBecomesHitTestable()
    {
        using var fixture = new CatalogVmFixture("rotate-hover");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = CreateVm(fixture, catalog);
        await SelectImage(fixture, catalog, vm, new EditSettings());
        await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var overlay = window.GetVisualDescendants().OfType<SpotsOverlayControl>().Single();
        var position = overlay.TranslatePoint(new Point(overlay.Bounds.Width / 2, overlay.Bounds.Height / 2), window)!.Value;
        window.MouseMove(position);
        Assert.Equal("None", overlay.Cursor?.ToString());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ImageService.Previews.RenderGateAsync = () => release.Task;

        try
        {
            vm.RotateLeftCommand.Execute(null);
            Assert.False(vm.CanEditSpots);
            Assert.Null(overlay.Cursor);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            release.TrySetResult();
            await vm.PendingHistoryCommitTask!.WaitAsync(TestWaits.Condition);
            // History completion precedes compositor hit testing and stationary hover feedback.
            await TestWaits.UntilAsync(() =>
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();

                return vm.CanEditSpots && overlay.IsPointerOver && overlay.Cursor?.ToString() == "None";
            });
            Assert.True(vm.CanEditSpots);
            Assert.True(overlay.IsPointerOver);
            Assert.Equal("None", overlay.Cursor?.ToString());
        }
        finally
        {
            release.TrySetResult();
        }
    }

    private static bool HasTrace(WaveformView view)
    {
        var bitmap = Assert.IsType<WriteableBitmap>(view.BitmapForTesting);
        using var buffer = bitmap.Lock();
        var pixels = new int[buffer.RowBytes / 4 * buffer.Size.Height];
        Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);

        return pixels.Distinct().Skip(1).Any();
    }

    private static MainWindowViewModel CreateVm(CatalogVmFixture fixture, CatalogService catalog)
    {
        var vm = fixture.CreateViewModel(catalog, new LocalTestLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), timeProvider: new TestTimeProvider());
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsDevelopMode = true;

        return vm;
    }

    private static async Task SelectImage(CatalogVmFixture fixture, CatalogService catalog,
        MainWindowViewModel vm, EditSettings settings)
    {
        var image = new ImageFile(fixture.Path("source.jpg")) { EditSettings = settings };
        await image.EnsureCatalogIdAsync(catalog);
        await catalog.SaveEditSettingsAsync(image.CatalogId, settings);
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
    }
}
