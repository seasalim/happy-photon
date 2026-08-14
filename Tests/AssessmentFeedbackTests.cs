using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AssessmentFeedbackTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-assessment-feedback-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task SingleImageActionsDescribeSetAndUnsetValues()
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = await CreateViewModelAsync(catalog);

        await vm.TogglePickedImageCommand.ExecuteAsync(null);
        Assert.Equal("Set flag: Picked", vm.AssessmentFeedback);
        await vm.TogglePickedImageCommand.ExecuteAsync(null);
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
        Assert.Equal("Set rating: ★★★", vm.AssessmentFeedback);
        await vm.SetRatingCommand.ExecuteAsync(0);
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

        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        Assert.True(vm.IsAssessmentFeedbackVisible);
        Assert.Equal("Set rating: ★", vm.AssessmentFeedback);

        await WaitUntilAsync(() => !vm.IsAssessmentFeedbackVisible);
        Assert.Equal("Set rating: ★", vm.AssessmentFeedback);
        await WaitUntilAsync(() => vm.AssessmentFeedback == null);

        await vm.SetRatingCommand.ExecuteAsync(2);
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        await vm.SetRatingCommand.ExecuteAsync(2);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.True(vm.IsAssessmentFeedbackVisible);
        Assert.Equal("Set rating: ★★", vm.AssessmentFeedback);
        await WaitUntilAsync(() => !vm.IsAssessmentFeedbackVisible);
        Assert.Equal("Set rating: ★★", vm.AssessmentFeedback);
        await WaitUntilAsync(() => vm.AssessmentFeedback == null);
    }

    [Fact]
    public async Task FeedbackCannotOutliveSelectionOrFullScreenEntry()
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = await CreateViewModelAsync(catalog);
        var first = vm.SelectedImage!;
        var second = await CreateImageAsync(catalog, "second.jpg");
        vm.Library.SetImages([first, second]);

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

    private async Task<CatalogService> CreateCatalogAsync()
    {
        var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        return catalog;
    }

    private async Task<MainWindowViewModel> CreateViewModelAsync(
        CatalogService catalog)
    {
        var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        var image = await CreateImageAsync(catalog, "first.jpg");
        vm.Library.SetImages([image]);
        vm.SelectedImage = image;
        return vm;
    }

    private async Task<ImageFile> CreateImageAsync(
        CatalogService catalog,
        string name)
    {
        var image = new ImageFile(Path.Combine(_root, name));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        return image;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }
}
