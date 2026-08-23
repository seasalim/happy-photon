using System.Collections.Concurrent;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class AdjacentPreviewWarmTests
{
    [WindowsFact]
    public async Task CompletedPreviewRetriesJpegAfterCancelledRawWorkerExits()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("replacement");
        var stale = await CreateCatalogImageAsync(catalog, "stale.raf");
        var current = await CreateCatalogImageAsync(catalog, "current.raf");
        var next = await CreateCatalogImageAsync(catalog, "next.jpg");
        var clock = new TestTimeProvider();
        var loader = new ReplacementLoader(stale.FileName);
        var vm = new MainWindowViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock)
        {
            IsDevelopMode = true
        };
        vm.Library.SetImages([current, next]);

        try
        {
            Assert.True(vm.ImageService.Previews.TryStartAdjacentWarm(stale));
            Assert.True(loader.BlockedStarted.Wait(TestWaits.Condition));

            vm.SelectedImage = current;
            await TestWaits.UntilAsync(() =>
                vm.InitialPreviewActivityCount == 0 && vm.PreviewImage != null);
            clock.Advance(TimeSpan.FromMilliseconds(75));
            // Absence has no signal; settling widens the window in which an
            // incorrectly concurrent JPEG warm would have appeared.
            await Task.Delay(50);
            Assert.Equal(0, loader.Count(next.FileName));

            loader.ReleaseBlocked.Set();
            await TestWaits.UntilAsync(() => loader.Count(next.FileName) == 1);
            await TestWaits.UntilAsync(() =>
                vm.ImageService.Previews.PreviewActivityCount == 0);
            using var cached = await vm.ImageService.Previews.LoadCachedPreviewAsync(
                next,
                next.EditSettings);

            Assert.NotNull(cached);
            Assert.True(cached!.SettingsMatch);
            Assert.Equal(1, loader.Count(next.FileName));
        }
        finally
        {
            loader.ReleaseBlocked.Set();
            await vm.DisposeAsync();
        }
    }

    private sealed class ReplacementLoader(string blockedFile) : RecordingLoader
    {
        private readonly ConcurrentDictionary<string, int> _counts =
            new(StringComparer.OrdinalIgnoreCase);

        internal ManualResetEventSlim BlockedStarted { get; } = new();
        internal ManualResetEventSlim ReleaseBlocked { get; } = new();

        internal int Count(string fileName) =>
            _counts.TryGetValue(fileName, out var count) ? count : 0;

        public override BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            _counts.AddOrUpdate(file.FileName, 1, (_, count) => count + 1);
            if (file.FileName == blockedFile)
            {
                BlockedStarted.Set();
                ReleaseBlocked.Wait(TestWaits.Condition);
                cancellationToken.ThrowIfCancellationRequested();
            }
            return BaseImageLoadOutcome.Loaded(CreateBase(file, decode));
        }
    }
}
