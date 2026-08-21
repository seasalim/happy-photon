using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AssessmentTargetingTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-assessment-{Guid.NewGuid():N}")).FullName;

    [Theory]
    [InlineData(AssessmentAxis.Flag)]
    [InlineData(AssessmentAxis.Rating)]
    [InlineData(AssessmentAxis.ColorLabel)]
    public async Task LibrarySelection_WinsOverOutsideActiveAndReportsCount(
        AssessmentAxis axis)
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var active = await CreateImageAsync(catalog, "active.jpg", axis);
        var first = await CreateImageAsync(catalog, "first.jpg");
        var second = await CreateImageAsync(catalog, "second.jpg");
        vm.Library.SetImages([active, first, second]);
        vm.SelectedImage = active;
        vm.Library.ToggleSelection(first);
        vm.Library.ToggleSelection(second);

        await ExecuteAsync(vm, axis);

        AssertInitialValue(active, axis);
        AssertActionValue(first, axis);
        AssertActionValue(second, axis);
        Assert.Equal(ExpectedStatus(axis, 2), vm.TransientStatus);
    }

    [Theory]
    [InlineData(AssessmentAxis.Flag)]
    [InlineData(AssessmentAxis.Rating)]
    [InlineData(AssessmentAxis.ColorLabel)]
    public async Task EmptyLibrarySelection_FallsBackToActiveWithoutStatus(
        AssessmentAxis axis)
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var active = await CreateImageAsync(catalog, "active.jpg", axis);
        var other = await CreateImageAsync(catalog, "other.jpg");
        vm.Library.SetImages([active, other]);
        vm.SelectedImage = active;

        await ExecuteAsync(vm, axis);

        AssertActionValue(active, axis);
        AssertDefaultValue(other, axis);
        Assert.Null(vm.TransientStatus);
    }

    [Theory]
    [InlineData(AssessmentAxis.Flag)]
    [InlineData(AssessmentAxis.Rating)]
    [InlineData(AssessmentAxis.ColorLabel)]
    public async Task Develop_TargetsOnlyActivePhoto(AssessmentAxis axis)
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var active = await CreateImageAsync(catalog, "active.jpg", axis);
        var selected = await CreateImageAsync(catalog, "selected.jpg");
        vm.Library.SetImages([active, selected]);
        vm.SelectedImage = active;
        vm.Library.ToggleSelection(selected);
        vm.IsDevelopMode = true;

        await ExecuteAsync(vm, axis);

        AssertActionValue(active, axis);
        AssertDefaultValue(selected, axis);
        Assert.Null(vm.TransientStatus);
    }

    [Theory]
    [InlineData(AssessmentAxis.Flag)]
    [InlineData(AssessmentAxis.Rating)]
    [InlineData(AssessmentAxis.ColorLabel)]
    public async Task Fullscreen_IsNoOp(AssessmentAxis axis)
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var active = await CreateImageAsync(catalog, "active.jpg", axis);
        var selected = await CreateImageAsync(catalog, "selected.jpg");
        vm.Library.SetImages([active, selected]);
        vm.SelectedImage = active;
        vm.Library.ToggleSelection(selected);
        vm.IsFullScreenMode = true;

        await ExecuteAsync(vm, axis);

        AssertInitialValue(active, axis);
        AssertDefaultValue(selected, axis);
        Assert.Null(vm.TransientStatus);
    }

    [Fact]
    public async Task Navigation_MovesSelectionWithFocusOnlyInLibrary()
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var first = await CreateImageAsync(catalog, "first.jpg");
        var second = await CreateImageAsync(catalog, "second.jpg");
        var third = await CreateImageAsync(catalog, "third.jpg");
        vm.Library.SetImages([first, second, third]);
        vm.SelectedImage = first;
        vm.Library.ToggleSelection(first);

        vm.SelectNextImageCommand.Execute(null);
        await vm.SetRatingCommand.ExecuteAsync(4);

        Assert.Equal(0, first.Rating);
        Assert.Equal(4, second.Rating);
        Assert.Same(second, Assert.Single(vm.Library.GetSelectedImages()));

        vm.IsDevelopMode = true;
        vm.SelectNextImageCommand.Execute(null);

        Assert.Same(third, vm.SelectedImage);
        Assert.Same(second, Assert.Single(vm.Library.GetSelectedImages()));
    }

    [Fact]
    public async Task Pick_MixedSetAssignsAndUniformSetClears()
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var picked = await CreateImageAsync(catalog, "picked.jpg");
        picked.Flag = ImageFlag.Picked;
        await catalog.SaveFlagStateAsync(picked.CatalogId, picked.Flag);
        var unflagged = await CreateImageAsync(catalog, "unflagged.jpg");
        vm.Library.SetImages([picked, unflagged]);
        vm.SelectedImage = picked;
        vm.Library.ToggleSelection(picked);
        vm.Library.ToggleSelection(unflagged);

        await vm.TogglePickedImageCommand.ExecuteAsync(null);
        Assert.All([picked, unflagged], image =>
            Assert.Equal(ImageFlag.Picked, image.Flag));

        await vm.TogglePickedImageCommand.ExecuteAsync(null);
        Assert.All([picked, unflagged], image =>
            Assert.Equal(ImageFlag.Unflagged, image.Flag));
    }

    [Theory]
    [InlineData(AssessmentAxis.Flag)]
    [InlineData(AssessmentAxis.Rating)]
    [InlineData(AssessmentAxis.ColorLabel)]
    public async Task FilteredActive_ReselectsCapturedReplacement(
        AssessmentAxis axis)
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var previous = await CreateMatchingImageAsync(catalog, "previous.jpg", axis);
        var active = await CreateMatchingImageAsync(catalog, "active.jpg", axis);
        var next = await CreateMatchingImageAsync(catalog, "next.jpg", axis);
        var target = await CreateMatchingImageAsync(catalog, "target.jpg", axis);
        vm.Library.SetImages([previous, active, next, target]);
        ApplyFilter(vm, axis);
        vm.SelectedImage = active;
        vm.Library.ToggleSelection(active);
        vm.Library.ToggleSelection(target);

        await ExecuteRemovalAsync(vm, axis);

        Assert.Same(next, vm.SelectedImage);
        Assert.True(vm.Library.ContainsVisible(vm.SelectedImage));
    }

    [Theory]
    [InlineData(AssessmentAxis.Flag)]
    [InlineData(AssessmentAxis.Rating)]
    public async Task FailedBatch_LeavesModelsAndCatalogUntouched(
        AssessmentAxis axis)
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var valid = await CreateImageAsync(catalog, "valid.jpg");
        var missing = new ImageFile(Path.Combine(_root, "missing.jpg"))
        {
            CatalogId = long.MaxValue
        };
        vm.Library.SetImages([valid, missing]);
        vm.SelectedImage = valid;
        vm.Library.ToggleSelection(valid);
        vm.Library.ToggleSelection(missing);

        await ExecuteAsync(vm, axis);

        AssertDefaultValue(valid, axis);
        AssertDefaultValue(missing, axis);
        Assert.Equal(
            axis == AssessmentAxis.Flag
                ? "Unable to update flags"
                : "Unable to update ratings",
            vm.TransientStatus);
        var state = (await catalog.LoadImageStatesAsync([valid.FilePath]))[valid.FilePath];
        if (axis == AssessmentAxis.Flag)
            Assert.Equal(ImageFlag.Unflagged, state.Flag);
        else
            Assert.Equal(0, state.Rating);
    }

    [Theory]
    [InlineData(AssessmentAxis.Flag)]
    [InlineData(AssessmentAxis.Rating)]
    public async Task BatchWriter_UpdatesLargeSetAndRollsBackMissingRow(
        AssessmentAxis axis)
    {
        using var catalog = await CreateCatalogAsync();
        var paths = Enumerable.Range(0, 1500)
            .Select(index => Path.Combine(_root, $"batch-{index}.jpg"))
            .ToArray();
        var states = await catalog.LoadOrCreateImageStatesAsync(paths);
        var ids = paths.Select(path => states[path].CatalogId).ToArray();

        if (axis == AssessmentAxis.Flag)
            await catalog.SaveFlagStateAsync(ids, ImageFlag.Picked);
        else
            await catalog.SaveRatingAsync(ids, 4);

        var written = await catalog.LoadImageStatesAsync(paths);
        Assert.All(written.Values, state =>
        {
            if (axis == AssessmentAxis.Flag)
                Assert.Equal(ImageFlag.Picked, state.Flag);
            else
                Assert.Equal(4, state.Rating);
        });

        if (axis == AssessmentAxis.Flag)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                catalog.SaveFlagStateAsync([ids[0], long.MaxValue], ImageFlag.Rejected));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                catalog.SaveRatingAsync([ids[0], long.MaxValue], 1));
        }

        var first = (await catalog.LoadImageStatesAsync([paths[0]]))[paths[0]];
        if (axis == AssessmentAxis.Flag)
            Assert.Equal(ImageFlag.Picked, first.Flag);
        else
            Assert.Equal(4, first.Rating);
    }

    [Fact]
    public void ShortcutCatalogAndTooltips_DescribeSharedSelectionRule()
    {
        var organize = ShortcutCatalog.Groups.Single(group => group.Title == "Organize");
        foreach (var keys in new[] { "P", "U", "X", "1–5", "0", "6–9" })
        {
            var action = organize.Entries.Single(entry => entry.Keys == keys).Action;
            Assert.Contains("selection", action, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Develop", action, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("current image", action, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var keys in new[] { "Space", "Ctrl+A", "Ctrl+Click", "Shift+Click" })
        {
            var action = organize.Entries.Single(entry => entry.Keys == keys).Action;
            Assert.DoesNotContain("export", action, StringComparison.OrdinalIgnoreCase);
        }

        var xaml = File.ReadAllText(Path.Combine(
            GoldenTestPaths.RepositoryRoot, "Views", "ImageAssessmentControl.axaml"));
        Assert.Contains("selection when non-empty", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("otherwise the active photo", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("active photo only in Develop", xaml, StringComparison.OrdinalIgnoreCase);
        var labelTip = new ColorLabelChoice(ColorLabel.Red, "Red").ToolTip;
        Assert.Contains("selection when non-empty", labelTip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("otherwise the active photo", labelTip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Develop", labelTip, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<CatalogService> CreateCatalogAsync()
    {
        var catalog = new CatalogService(Path.Combine(_root, Guid.NewGuid().ToString("N")));
        await catalog.InitializeAsync();
        return catalog;
    }

    private static MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        new(catalog, new NullBaseLoader(), _ => Task.CompletedTask);

    private async Task<ImageFile> CreateImageAsync(
        CatalogService catalog,
        string name,
        AssessmentAxis? initialAxis = null)
    {
        var image = new ImageFile(Path.Combine(_root, name));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        if (initialAxis == null) return image;

        SetInitialValue(image, initialAxis.Value);
        await PersistAsync(catalog, image, initialAxis.Value);
        return image;
    }

    private async Task<ImageFile> CreateMatchingImageAsync(
        CatalogService catalog,
        string name,
        AssessmentAxis axis)
    {
        var image = await CreateImageAsync(catalog, name);
        switch (axis)
        {
            case AssessmentAxis.Flag:
                image.Flag = ImageFlag.Picked;
                break;
            case AssessmentAxis.Rating:
                image.Rating = 4;
                break;
            case AssessmentAxis.ColorLabel:
                image.ColorLabel = ColorLabel.Red;
                break;
        }
        await PersistAsync(catalog, image, axis);
        return image;
    }

    private static Task ExecuteAsync(MainWindowViewModel vm, AssessmentAxis axis) =>
        axis switch
        {
            AssessmentAxis.Flag => vm.TogglePickedImageCommand.ExecuteAsync(null),
            AssessmentAxis.Rating => vm.SetRatingCommand.ExecuteAsync(4),
            _ => vm.SetColorLabelCommand.ExecuteAsync(ColorLabel.Red)
        };

    private static Task ExecuteRemovalAsync(
        MainWindowViewModel vm,
        AssessmentAxis axis) => axis switch
        {
            AssessmentAxis.Flag => vm.TogglePickedImageCommand.ExecuteAsync(null),
            AssessmentAxis.Rating => vm.SetRatingCommand.ExecuteAsync(2),
            _ => vm.SetColorLabelCommand.ExecuteAsync(ColorLabel.Red)
        };

    private static Task PersistAsync(
        CatalogService catalog,
        ImageFile image,
        AssessmentAxis axis) => axis switch
        {
            AssessmentAxis.Flag => catalog.SaveFlagStateAsync(image.CatalogId, image.Flag),
            AssessmentAxis.Rating => catalog.SaveRatingAsync(image.CatalogId, image.Rating),
            _ => catalog.SaveColorLabelAsync([image.CatalogId], image.ColorLabel)
        };

    private static void ApplyFilter(MainWindowViewModel vm, AssessmentAxis axis)
    {
        switch (axis)
        {
            case AssessmentAxis.Flag:
                vm.Library.FlagFilter = FlagFilter.Picked;
                break;
            case AssessmentAxis.Rating:
                vm.Library.MinimumRating = 4;
                break;
            case AssessmentAxis.ColorLabel:
                vm.Library.ColorLabelFilter = ColorLabelFilter.Red;
                break;
        }
    }

    private static void SetInitialValue(ImageFile image, AssessmentAxis axis)
    {
        switch (axis)
        {
            case AssessmentAxis.Flag:
                image.Flag = ImageFlag.Rejected;
                break;
            case AssessmentAxis.Rating:
                image.Rating = 1;
                break;
            case AssessmentAxis.ColorLabel:
                image.ColorLabel = ColorLabel.Blue;
                break;
        }
    }

    private static void AssertInitialValue(ImageFile image, AssessmentAxis axis)
    {
        if (axis == AssessmentAxis.Flag) Assert.Equal(ImageFlag.Rejected, image.Flag);
        if (axis == AssessmentAxis.Rating) Assert.Equal(1, image.Rating);
        if (axis == AssessmentAxis.ColorLabel) Assert.Equal(ColorLabel.Blue, image.ColorLabel);
    }

    private static void AssertActionValue(ImageFile image, AssessmentAxis axis)
    {
        if (axis == AssessmentAxis.Flag) Assert.Equal(ImageFlag.Picked, image.Flag);
        if (axis == AssessmentAxis.Rating) Assert.Equal(4, image.Rating);
        if (axis == AssessmentAxis.ColorLabel) Assert.Equal(ColorLabel.Red, image.ColorLabel);
    }

    private static void AssertDefaultValue(ImageFile image, AssessmentAxis axis)
    {
        if (axis == AssessmentAxis.Flag) Assert.Equal(ImageFlag.Unflagged, image.Flag);
        if (axis == AssessmentAxis.Rating) Assert.Equal(0, image.Rating);
        if (axis == AssessmentAxis.ColorLabel) Assert.Equal(ColorLabel.None, image.ColorLabel);
    }

    private static string ExpectedStatus(AssessmentAxis axis, int count) =>
        axis switch
        {
            AssessmentAxis.Flag => $"Picked {count} photos",
            AssessmentAxis.Rating => $"Rated {count} photos",
            _ => $"Labeled {count} photos"
        };

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    public enum AssessmentAxis
    {
        Flag,
        Rating,
        ColorLabel
    }
}
