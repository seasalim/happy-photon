using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class BrushMaskBaselineTests
{
    [AvaloniaTheory]
    [InlineData("profile")]
    [InlineData("white-balance")]
    [InlineData("source")]
    public async Task ResolvedBaseChangeRendersWithUnchangedRequestedSettingsAndRejectsSupersededResults(string outcome)
    {
        using var fixture = new CatalogVmFixture("mask-base-outcome");
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = fixture.CreateViewModel(catalog, new BaselineLoader(outcome),
            _ => Task.CompletedTask, new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            timeProvider: clock);
        var settings = new EditSettings
        {
            Locals = [new() { Type = "brush", Luminance = new() { Enabled = true, Lower = .1 },
                Strokes = [new() { Points = [new(8192, 8192)] }] }]
        };
        var image = new ImageFile(fixture.Path("synthetic.jpg")) { EditSettings = settings };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, settings);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
        var first = vm.LocalRangeMask;
        Assert.NotNull(first);
        var requested = BaseDecodeSettings.From(image.EditSettings).CacheKey;
        var settingsBefore = EditSettingsJson.Serialize(image.EditSettings);
        var interactive = vm.PreviewImage;

        if (outcome == "source")
        {
            using var large = vm.ImageService.Previews.AcquireLocalRangeBase(image, image.EditSettings, 2000);
            Assert.NotNull(large);
            large.Base.SourceWriteTime = (large.Base.SourceWriteTime ?? DateTime.UnixEpoch).AddSeconds(1);
        }

        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        vm.LocalMaskRenderGateAsync = () =>
        {
            requests++;
            entered.TrySetResult();

            return hold.Task;
        };

        try
        {
            vm.PublishRequiredDeviceLongEdge(2000);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            await TestWaits.UntilAsync(() => vm.PreviewImage!.PixelSize.Width == 2000);
            await entered.Task.WaitAsync(TestWaits.Condition);
            Assert.Same(first, vm.LocalRangeMask);
            Assert.True(vm.IsLocalRangeMaskUpdating);
            Assert.Equal(requested, BaseDecodeSettings.From(image.EditSettings).CacheKey);
            Assert.Equal(settingsBefore, EditSettingsJson.Serialize(image.EditSettings));
            Assert.Equal(1, requests);
            var resting = vm.PreviewImage;
            vm.PreviewImage = interactive;
            hold.TrySetResult();
            await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
            Assert.Equal(1600, vm.LocalRangeMask!.PixelSize.Width);
            Assert.False(vm.IsLocalRangeMaskUpdating);
            vm.PreviewImage = resting;
            await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
            Assert.Equal(2000, vm.LocalRangeMask!.PixelSize.Width);
            Assert.False(vm.IsLocalRangeMaskUpdating);
            Assert.Equal(3, requests);
        }
        finally
        {
            hold.TrySetResult();
        }
    }
}
