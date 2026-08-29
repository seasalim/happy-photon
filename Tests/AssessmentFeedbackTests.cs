using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AssessmentFeedbackTests : IDisposable
{
    private readonly TestTimeProvider _clock = new();
    private readonly CatalogVmFixture _fx = new("assessment-feedback");

    [Fact]
    public async Task SingleImageActionsDescribeSetAndUnsetValues()
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = await CreateViewModelAsync(catalog);

        await vm.TogglePickedImageCommand.ExecuteAsync(null);
        Assert.Equal("Set flag: Picked", vm.AssessmentFeedback);
        await vm.TogglePickedImageCommand.ExecuteAsync(null);
        Assert.Equal("Set flag: Picked", vm.AssessmentFeedback);
        await vm.ToggleFlagCommand.ExecuteAsync(null);
        Assert.Equal("Unset flag: Picked", vm.AssessmentFeedback);
        await vm.ToggleRejectedImageCommand.ExecuteAsync(null);
        Assert.Equal("Set flag: Rejected", vm.AssessmentFeedback);
        await vm.RejectImageCommand.ExecuteAsync(null);
        Assert.Equal("Set flag: Rejected", vm.AssessmentFeedback);
        await vm.UnpickImageCommand.ExecuteAsync(null);
        Assert.Equal("Unset flag: Rejected", vm.AssessmentFeedback);
        await vm.UnpickImageCommand.ExecuteAsync(null);
        Assert.Equal("Unset flag", vm.AssessmentFeedback);

        await vm.SetRatingCommand.ExecuteAsync(3);
        Assert.Equal("Set rating: ★★★", vm.AssessmentFeedback);
        await vm.SetRatingCommand.ExecuteAsync(3);
        Assert.Equal("Unset rating: ★★★", vm.AssessmentFeedback);
        await vm.SetRatingCommand.ExecuteAsync(0);
        Assert.Equal("Unset rating", vm.AssessmentFeedback);

        vm.SetColorLabelNames(new Dictionary<ColorLabel, string>(
            ColorLabelNames.Defaults)
        {
            [ColorLabel.Red] = "Select"
        });
        await vm.SetColorLabelCommand.ExecuteAsync(ColorLabel.Red);
        Assert.Equal("Set color: Select", vm.AssessmentFeedback);
        await vm.SetColorLabelCommand.ExecuteAsync(ColorLabel.Red);
        Assert.Equal("Unset color: Select", vm.AssessmentFeedback);
        await vm.SetColorLabelCommand.ExecuteAsync(ColorLabel.None);
        Assert.Equal("Unset color", vm.AssessmentFeedback);
    }

    [Fact]
    public async Task FeedbackHoldsThenFadesAndRepeatRestartsHold()
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = await CreateViewModelAsync(catalog);

        await vm.SetRatingCommand.ExecuteAsync(1);
        Assert.Equal("Set rating: ★", vm.AssessmentFeedback);
        Assert.True(vm.IsAssessmentFeedbackVisible);

        // Short of the 1.5 s hold nothing is due, so the toast cannot start
        // fading however long the machine takes to reach the assertion.
        _clock.Advance(TimeSpan.FromMilliseconds(1400));
        Assert.True(vm.IsAssessmentFeedbackVisible);
        Assert.Equal("Set rating: ★", vm.AssessmentFeedback);

        await WaitUntilAsync(() => !vm.IsAssessmentFeedbackVisible);
        Assert.Equal("Set rating: ★", vm.AssessmentFeedback);
        await WaitUntilAsync(() => vm.AssessmentFeedback == null);

        await vm.SetRatingCommand.ExecuteAsync(2);
        _clock.Advance(TimeSpan.FromMilliseconds(1400));
        await vm.SetRatingCommand.ExecuteAsync(2);
        _clock.Advance(TimeSpan.FromMilliseconds(1400));

        Assert.True(vm.IsAssessmentFeedbackVisible);
        Assert.Equal("Unset rating: ★★", vm.AssessmentFeedback);
        await WaitUntilAsync(() => !vm.IsAssessmentFeedbackVisible);
        Assert.Equal("Unset rating: ★★", vm.AssessmentFeedback);
        await WaitUntilAsync(() => vm.AssessmentFeedback == null);
    }

    [Fact]
    public async Task FeedbackCannotOutliveSelectionOrFullScreenEntry()
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = await CreateViewModelAsync(catalog);
        var first = vm.SelectedImage!;
        var second = await CreateImageAsync(catalog, "second.jpg");
        vm.Browse.SetImages([first, second]);

        await vm.SetRatingCommand.ExecuteAsync(2);
        vm.SelectedImage = second;
        Assert.Null(vm.AssessmentFeedback);
        Assert.False(vm.IsAssessmentFeedbackVisible);

        await vm.SetRatingCommand.ExecuteAsync(3);
        Assert.Equal("Set rating: ★★★", vm.AssessmentFeedback);
        vm.IsFullScreenMode = true;
        Assert.Null(vm.AssessmentFeedback);
        Assert.False(vm.IsAssessmentFeedbackVisible);
    }

    private Task<CatalogService> CreateCatalogAsync() =>
        _fx.CreateCatalogAsync("catalog");

    private async Task<MainWindowViewModel> CreateViewModelAsync(
        CatalogService catalog)
    {
        var vm = _fx.CreateViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask,
            timeProvider: _clock);
        var image = await CreateImageAsync(catalog, "first.jpg");
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        return vm;
    }

    private async Task<ImageFile> CreateImageAsync(
        CatalogService catalog,
        string name)
    {
        var image = new ImageFile(_fx.Path(name));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        return image;
    }

    // Drives the injected clock rather than wall time: each turn releases at
    // most one scheduled step, so the wait ends on the state change itself and
    // the real-time ceiling only bounds a hang.
    private async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TestWaits.Condition;
        while (!condition())
        {
            Assert.True(
                DateTime.UtcNow < deadline,
                "The assessment toast never reached the expected state.");
            _clock.Advance(TimeSpan.FromMilliseconds(50));
            await Task.Delay(5);
        }
    }

    public void Dispose() => _fx.Dispose();
}
