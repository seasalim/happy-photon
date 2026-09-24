using Avalonia;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LoadingMessageGraceTests
{
    [WindowsTheory]
    [InlineData("paint")]
    [InlineData("end")]
    [InlineData("dispose")]
    public void PaneEndingWithinGraceNeverShows(string ending)
    {
        var clock = new TestTimeProvider();
        using var pane = new ComparePaneViewModel(new ImageFile("pane.jpg"), clock);
        using var bitmap = Bitmap();
        Assert.Equal(1, clock.TimerCount);
        var activations = 0;
        pane.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(pane.IsLoadingMessageVisible) && pane.IsLoadingMessageVisible)
                activations++;
        };
        clock.Advance(TimeSpan.FromMilliseconds(299));
        if (ending == "paint") pane.Preview = bitmap;
        else if (ending == "end") pane.IsLoading = false;
        else pane.Dispose();
        Assert.Equal(0, clock.TimerCount);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(pane.IsLoadingMessageVisible);
        Assert.Equal(0, activations);
        // Disposal cannot be undone by a late loader notification.
        if (ending == "dispose")
        {
            pane.IsLoading = false;
            pane.IsLoading = true;
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.False(pane.IsLoadingMessageVisible);
        }
    }

    [WindowsTheory]
    [InlineData("paint", 299)]
    [InlineData("paint", 300)]
    [InlineData("end", 299)]
    [InlineData("end", 300)]
    [InlineData("clear", 299)]
    [InlineData("clear", 300)]
    [InlineData("dispose", 299)]
    [InlineData("dispose", 300)]
    public async Task DevelopHidesImmediatelyAndCancelsPendingActivation(string ending, int elapsed)
    {
        using var fixture = new CatalogVmFixture("loading-grace");
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        var vm = fixture.CreateViewModel(catalog, timeProvider: clock);
        using var bitmap = Bitmap();
        var disposed = false;
        try
        {
            vm.HasSelectedImage = true;
            vm.IsDevelopPreviewLoading = true;
            var activations = 0;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.IsDevelopLoadingMessageVisible) && vm.IsDevelopLoadingMessageVisible)
                    activations++;
            };
            clock.Advance(TimeSpan.FromMilliseconds(elapsed));
            Assert.Equal(elapsed == 300, vm.IsDevelopLoadingMessageVisible);
            if (ending == "paint") vm.PreviewImage = bitmap;
            else if (ending == "end") vm.IsDevelopPreviewLoading = false;
            else if (ending == "clear") vm.HasSelectedImage = false;
            else
            {
                await vm.DisposeAsync();
                disposed = true;
                vm.IsDevelopPreviewLoading = false;
                vm.IsDevelopPreviewLoading = true;
            }
            Assert.False(vm.IsDevelopLoadingMessageVisible);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.False(vm.IsDevelopLoadingMessageVisible);
            Assert.Equal(elapsed == 300 ? 1 : 0, activations);
        }
        finally
        {
            vm.PreviewImage = null;
            if (!disposed) await vm.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task DevelopKeepsContinuousIntervalAcrossSelectionChangesAndRearmsAfterPaint()
    {
        using var fixture = new CatalogVmFixture("loading-selection");
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = fixture.CreateViewModel(catalog,
            loadMetadataAsync: _ => Task.CompletedTask, timeProvider: clock);
        var images = new[] { new ImageFile(fixture.Path("first.jpg")), new ImageFile(fixture.Path("second.jpg")) };
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[0];
        vm.IsDevelopPreviewLoading = true;
        clock.Advance(TimeSpan.FromMilliseconds(200));
        vm.SelectedImage = images[1];
        Assert.True(vm.ShowDevelopLoadingMessage);
        clock.Advance(TimeSpan.FromMilliseconds(99));
        Assert.False(vm.IsDevelopLoadingMessageVisible);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(vm.IsDevelopLoadingMessageVisible);
        vm.SelectedImage = images[0];
        Assert.True(vm.IsDevelopLoadingMessageVisible);
        using var bitmap = Bitmap();
        vm.PreviewImage = bitmap;
        Assert.False(vm.IsDevelopLoadingMessageVisible);
        vm.PreviewImage = null;
        clock.Advance(TimeSpan.FromMilliseconds(299));
        Assert.False(vm.IsDevelopLoadingMessageVisible);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(vm.IsDevelopLoadingMessageVisible);
        vm.SelectedImage = null;
        Assert.False(vm.IsDevelopLoadingMessageVisible);
    }

    [Theory]
    [InlineData(299)]
    [InlineData(300)]
    public async Task ClearingLoupeSelectionInvalidatesTheRetainedPane(int elapsed)
    {
        using var fixture = new CatalogVmFixture("loupe-clear");
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = fixture.CreateViewModel(catalog,
            loadMetadataAsync: _ => Task.CompletedTask, timeProvider: clock);
        vm.SelectedImage = new ImageFile(fixture.Path("first.jpg"));
        using var pane = new ComparePaneViewModel(vm.SelectedImage, clock);
        vm.LoupePane = pane;
        vm.IsLoupeMode = true;
        clock.Advance(TimeSpan.FromMilliseconds(elapsed));
        Assert.Equal(elapsed == 300, pane.IsLoadingMessageVisible);
        vm.SelectedImage = null;
        Assert.Same(pane, vm.LoupePane);
        Assert.False(pane.IsLoadingMessageVisible);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(pane.IsLoadingMessageVisible);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedExpiryCannotPublishForANewIntervalOrDisposedOwner(bool dispose)
    {
        var clock = new TestTimeProvider();
        var context = new QueuedContext();
        var previous = SynchronizationContext.Current;
        var visible = false;
        using var grace = new LoadingMessageGrace(clock, value => visible = value);
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            grace.Update(true);
            SynchronizationContext.SetSynchronizationContext(null);
            clock.Advance(TimeSpan.FromMilliseconds(300));
            Assert.Single(context.Pending);
            SynchronizationContext.SetSynchronizationContext(context);
            grace.Update(false);
            grace.Update(true);
            if (dispose) grace.Dispose();
            context.Drain();
            Assert.False(visible);
            clock.Advance(TimeSpan.FromMilliseconds(299));
            Assert.False(visible);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            context.Drain();
            Assert.Equal(!dispose, visible);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static WriteableBitmap Bitmap() => new(new PixelSize(2, 2), new Vector(96, 96),
        Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);

    private sealed class QueuedContext : SynchronizationContext
    {
        internal Queue<(SendOrPostCallback Callback, object? State)> Pending { get; } = new();
        public override void Post(SendOrPostCallback d, object? state) => Pending.Enqueue((d, state));
        internal void Drain()
        {
            while (Pending.TryDequeue(out var work)) work.Callback(work.State);
        }
    }
}
