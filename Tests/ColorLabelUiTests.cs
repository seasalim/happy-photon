using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

/// <summary>
/// Library controls that need a live dispatcher or a realized visual tree.
/// </summary>
public sealed class ColorLabelUiTests
{
    [AvaloniaFact]
    public async Task SetColorLabel_ThroughAgentService_NormalizesDuplicatesAndReportsMissing()
    {
        var root = NewRoot();
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await catalog.InitializeAsync();
        var vm = NewViewModel(catalog);
        await using var imageService = new ImageService(catalog);
        var service = new AgentToolService(vm, imageService, catalog);

        var image = new ImageFile(Path.Combine(root, "first.jpg"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.Library.SetImages([image]);
        var refreshes = 0;
        vm.Library.FilterChanged += (_, _) => refreshes++;

        var result = await service.SetColorLabelAsync(
            [image.FilePath, image.FilePath, "missing.jpg"],
            "purple");

        Assert.Equal([image.FilePath], result.Succeeded);
        Assert.Equal("missing.jpg", Assert.Single(result.Failed).Id);
        Assert.Equal(ColorLabel.Purple, image.ColorLabel);
        Assert.Equal(
            ColorLabel.Purple,
            (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath]
                .ColorLabel);
        Assert.Equal(1, refreshes);
    }

    [AvaloniaFact]
    public async Task SetColorLabel_ThroughAgentService_RejectsUnknownToken()
    {
        var root = NewRoot();
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await catalog.InitializeAsync();
        var vm = NewViewModel(catalog);
        await using var imageService = new ImageService(catalog);
        var service = new AgentToolService(vm, imageService, catalog);

        var image = new ImageFile(Path.Combine(root, "first.jpg"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.Library.SetImages([image]);

        await Assert.ThrowsAsync<AgentToolException>(() =>
            service.SetColorLabelAsync([image.FilePath], "chartreuse"));
        Assert.Equal(ColorLabel.None, image.ColorLabel);
    }

    [AvaloniaFact]
    public void AssessmentSwatches_MaterializeOneButtonPerEnumSlot()
    {
        using var catalog = new CatalogService(NewRoot());
        var vm = NewViewModel(catalog);
        var control = new ImageAssessmentControl { DataContext = vm };
        var window = new Window { Content = control };
        window.Show();

        var swatches = SwatchButtons(control);
        Assert.Equal(vm.ColorLabelChoices.Count, swatches.Count);
        Assert.Equal(
            vm.ColorLabelChoices.Select(choice => choice.Value),
            swatches.Select(button => Assert.IsType<ColorLabel>(button.CommandParameter)));
    }

    [AvaloniaFact]
    public void AssessmentSwatches_FollowRenamedSlotsIntoAccessibilityText()
    {
        using var catalog = new CatalogService(NewRoot());
        var vm = NewViewModel(catalog);
        vm.SetColorLabelNames(new Dictionary<ColorLabel, string>(ColorLabelNames.Defaults)
        {
            [ColorLabel.Red] = "Select"
        });
        var control = new ImageAssessmentControl { DataContext = vm };
        var window = new Window { Content = control };
        window.Show();

        var red = Assert.Single(
            SwatchButtons(control),
            button => Equals(button.CommandParameter, ColorLabel.Red));
        Assert.Contains(
            "select",
            Avalonia.Automation.AutomationProperties.GetName(red) ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void LibraryFilterBar_GroupControlsToggleAndExposeAccessibleMetadata()
    {
        using var catalog = new CatalogService(NewRoot());
        var vm = NewViewModel(catalog);
        var control = new LibraryGridView { DataContext = vm };
        var window = new Window { Content = control };
        window.Show();

        var picked = control.FindControl<Button>("FlagFilterPickedButton")!;
        var rejected = control.FindControl<Button>("FlagFilterRejectedButton")!;
        var raw = control.FindControl<Button>("FilterRawButton")!;
        var jpeg = control.FindControl<Button>("FilterJpegButton")!;
        var bursts = control.FindControl<Button>("BurstsButton")!;
        var filterLabel = control.FindControl<TextBlock>("FilterLabel")!;
        Assert.IsType<PathIcon>(bursts.Content);
        Assert.Contains("view-toggle", bursts.Classes);
        Assert.DoesNotContain("filter", bursts.Classes);
        Assert.Contains(
            bursts,
            control.FindControl<StackPanel>("ThumbnailSizePanel")!
                .GetLogicalDescendants().OfType<Button>());
        Assert.Equal("Filter", filterLabel.Text);
        Assert.Equal("Picked", picked.Content);
        Assert.Equal("Rejected", rejected.Content);
        Assert.All([picked, rejected], button =>
            Assert.Contains("filter", button.Classes));
        Assert.Equal("Group bursts", ToolTip.GetTip(bursts));
        Assert.Equal("Group bursts", AutomationProperties.GetName(bursts));
        Assert.Null(control.FindControl<Button>("FilterAllButton"));
        Assert.Null(control.FindControl<Button>("FlagFilterAllButton"));
        Assert.Null(control.FindControl<Button>("RatingFilterAllButton"));

        Click(raw);
        Assert.Equal(ImageFileTypeFilter.Raw, control.FileTypeFilter);
        Assert.Contains("active", raw.Classes);
        Click(raw);
        Assert.Equal(ImageFileTypeFilter.All, control.FileTypeFilter);
        Assert.DoesNotContain("active", raw.Classes);
        Click(jpeg);
        Click(jpeg);
        Assert.Equal(ImageFileTypeFilter.All, control.FileTypeFilter);

        Click(picked);
        Assert.Equal(FlagFilter.Picked, control.FlagFilter);
        Assert.Contains("active", picked.Classes);
        Click(picked);
        Assert.Equal(FlagFilter.All, control.FlagFilter);
        Assert.DoesNotContain("active", picked.Classes);

        Click(rejected);
        Assert.Equal(FlagFilter.Rejected, control.FlagFilter);
        Click(rejected);
        Assert.Equal(FlagFilter.All, control.FlagFilter);

        var rating = control.FindControl<LibraryRatingFilter>("RatingFilter")!;
        var thirdStar = rating.FindControl<Button>("RatingFilter3Button")!;
        Assert.Null(rating.FindControl<Border>("RatingFilterGroup"));
        Assert.Equal(18, thirdStar.Width);
        Assert.Equal(0, thirdStar.BorderThickness.Left);
        Click(thirdStar);
        Assert.Equal(3, control.MinimumRating);
        Click(thirdStar);
        Assert.Equal(0, control.MinimumRating);

        Click(bursts);
        Assert.True(control.ShowBursts);
        Click(bursts);
        Assert.False(control.ShowBursts);

        var filterControls = control.FindControl<ScrollViewer>("FilterScrollViewer")!;
        var captions = filterControls.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Select(text => text.Text)
            .ToArray();
        Assert.Contains("Flag", captions);
        Assert.Contains("Rating", captions);
        Assert.Contains("Labels", captions);
        Assert.DoesNotContain("≥", captions);
        Assert.Equal("All", control.FindControl<Button>("SelectAllButton")!.Content);
        Assert.Equal("None", control.FindControl<Button>("SelectNoneButton")!.Content);
        Assert.Contains(
            "Select",
            control.FindControl<StackPanel>("LibraryActionsPanel")!
                .GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text));

        window.Close();
    }

    [AvaloniaFact]
    public void RatingFilter_ThresholdStarsFillAndClearOnReclick()
    {
        var control = new LibraryRatingFilter();
        var window = new Window { Content = control };
        window.Show();

        var buttons = Enumerable.Range(1, 5)
            .Select(rating => control.FindControl<Button>(
                $"RatingFilter{rating}Button")!)
            .ToArray();
        Assert.All(buttons, button => Assert.False(string.IsNullOrWhiteSpace(
            AutomationProperties.GetName(button))));

        Click(buttons[2]);
        Assert.Equal(3, control.MinimumRating);
        for (var rating = 1; rating <= 5; rating++)
        {
            Assert.Equal(
                rating <= 3,
                control.FindControl<TextBlock>(
                    $"RatingFilter{rating}Filled")!.IsVisible);
        }

        Click(buttons[2]);
        Assert.Equal(0, control.MinimumRating);
        Assert.All(
            Enumerable.Range(1, 5),
            rating => Assert.False(control.FindControl<TextBlock>(
                $"RatingFilter{rating}Filled")!.IsVisible));

        window.Close();
    }

    [AvaloniaFact]
    public void LabelFilter_SwatchesResetTheGroupOnReclick()
    {
        using var catalog = new CatalogService(NewRoot());
        var vm = NewViewModel(catalog);
        vm.SetColorLabelNames(new Dictionary<ColorLabel, string>(ColorLabelNames.Defaults)
        {
            [ColorLabel.Red] = "Select"
        });
        var control = new LibraryColorLabelFilter
        {
            Choices = vm.ColorLabelFilterChoices
        };
        var window = new Window { Content = control };
        window.Show();

        Assert.DoesNotContain(
            control.Choices,
            choice => choice.Value == ColorLabelFilter.All);
        var buttons = control.GetLogicalDescendants()
            .OfType<Button>()
            .Where(button => button.Tag is ColorLabelFilter)
            .ToArray();
        Assert.Equal(control.Choices.Count, buttons.Length);
        Assert.All(buttons, button => Assert.False(string.IsNullOrWhiteSpace(
            AutomationProperties.GetName(button))));

        var none = Assert.Single(
            buttons,
            button => Equals(button.Tag, ColorLabelFilter.None));
        Assert.Contains("swatch", none.Classes);
        Assert.DoesNotContain("filter", none.Classes);
        Assert.Equal(0, none.BorderThickness.Left);
        Assert.Equal(13, Assert.Single(
            none.GetLogicalDescendants().OfType<Border>(),
            border => border.IsVisible).Width);
        Assert.NotEmpty(none.GetLogicalDescendants()
            .OfType<Avalonia.Controls.Shapes.Path>());
        Assert.Equal(
            "Show photos with no color label",
            AutomationProperties.GetName(none));
        Assert.Equal(
            "Show photos with no color label",
            ToolTip.GetTip(none));

        var red = Assert.Single(
            buttons,
            button => Equals(button.Tag, ColorLabelFilter.Red));
        Assert.Contains("swatch", red.Classes);
        Assert.DoesNotContain("filter", red.Classes);
        var redDot = Assert.Single(red.GetLogicalDescendants()
            .OfType<Border>(), border => border.IsVisible);
        Assert.Equal(13, redDot.Width);
        Assert.Equal("Show select label only", AutomationProperties.GetName(red));
        Assert.Equal("Show select label only", ToolTip.GetTip(red));
        Click(red);
        Assert.Equal(ColorLabelFilter.Red, control.Filter);
        Assert.Equal(HappyPhotonColors.Primary, redDot.BorderBrush);
        Click(red);
        Assert.Equal(ColorLabelFilter.All, control.Filter);

        window.Close();
    }

    [AvaloniaFact]
    public async Task ClearAction_ResetsAllFiltersAndUsesFinalResultForSelection()
    {
        var root = NewRoot();
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        var vm = NewViewModel(catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var first = new ImageFile(Path.Combine(root, "first.jpg"));
        var second = new ImageFile(Path.Combine(root, "second.cr2"))
        {
            Flag = ImageFlag.Picked,
            Rating = 5,
            ColorLabel = ColorLabel.Red
        };
        vm.Library.SetImages([first, second]);
        var window = new MainWindow { DataContext = vm };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var control = window.FindControl<LibraryGridView>("LibraryGridView")!;
            vm.Library.FileTypeFilter = ImageFileTypeFilter.Raw;
            vm.Library.FlagFilter = FlagFilter.Picked;
            vm.Library.MinimumRating = 5;
            vm.Library.ColorLabelFilter = ColorLabelFilter.Purple;
            vm.SelectedImage = null;
            Dispatcher.UIThread.RunJobs();

            var filteredEmpty = control.FindControl<StackPanel>("FilteredEmptyState")!;
            Assert.True(filteredEmpty.IsVisible);
            var filterChanges = 0;
            var stateChanges = 0;
            vm.Library.FilterChanged += (_, _) => filterChanges++;
            vm.Library.StateChanged += (_, _) => stateChanges++;

            Click(control.FindControl<Button>("FilteredEmptyClearButton")!);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ImageFileTypeFilter.All, vm.Library.FileTypeFilter);
            Assert.Equal(FlagFilter.All, vm.Library.FlagFilter);
            Assert.Equal(0, vm.Library.MinimumRating);
            Assert.Equal(ColorLabelFilter.All, vm.Library.ColorLabelFilter);
            Assert.Equal(ImageFileTypeFilter.All, control.FileTypeFilter);
            Assert.Equal(FlagFilter.All, control.FlagFilter);
            Assert.Equal(0, control.MinimumRating);
            Assert.Equal(ColorLabelFilter.All, control.ColorLabelFilter);
            Assert.DoesNotContain(
                "active",
                control.FindControl<Button>("FilterRawButton")!.Classes);
            Assert.DoesNotContain(
                "active",
                control.FindControl<Button>("FlagFilterPickedButton")!.Classes);
            Assert.Equal(1, filterChanges);
            Assert.Equal(1, stateChanges);
            Assert.Same(first, vm.SelectedImage);
            Assert.False(filteredEmpty.IsVisible);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task FilteredEmptyState_ClearsFiltersWithoutReplacingFolderEmptyPanel()
    {
        var root = NewRoot();
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        var vm = NewViewModel(catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Library.SetImages(
            [new ImageFile(Path.Combine(root, "first.jpg"))]);
        var window = new MainWindow { DataContext = vm };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var control = window.FindControl<LibraryGridView>("LibraryGridView")!;
            vm.Library.ColorLabelFilter = ColorLabelFilter.Purple;
            Dispatcher.UIThread.RunJobs();

            Assert.True(control.FindControl<StackPanel>("FilteredEmptyState")!.IsVisible);
            Assert.Contains(
                control.FindControl<StackPanel>("FilteredEmptyState")!
                    .GetLogicalDescendants().OfType<TextBlock>(),
                text => text.Text == "No images match the current filters");
            Assert.False(control.FindControl<Border>("EmptyState")!.IsVisible);
            Assert.False(control.FindControl<ItemsRepeater>("ThumbnailGrid")!.IsVisible);

            Click(control.FindControl<Button>("FilteredEmptyClearButton")!);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ColorLabelFilter.All, vm.Library.ColorLabelFilter);
            Assert.Single(vm.Library.VisibleImages);
            Assert.False(control.FindControl<StackPanel>("FilteredEmptyState")!.IsVisible);

            vm.Library.SetImages([]);
            Dispatcher.UIThread.RunJobs();

            Assert.True(control.FindControl<Border>("EmptyState")!.IsVisible);
            Assert.False(control.FindControl<StackPanel>("FilteredEmptyState")!.IsVisible);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public void FilterOverflowFades_TrackBothEdgesAndResizeBackToFit()
    {
        using var catalog = new CatalogService(NewRoot());
        var control = new LibraryGridView { DataContext = NewViewModel(catalog) };
        var window = new Window
        {
            Width = 1600,
            Height = 500,
            Content = control
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var scroll = control.FindControl<ScrollViewer>("FilterScrollViewer")!;
        var left = control.FindControl<Border>("FilterLeftFade")!;
        var right = control.FindControl<Border>("FilterRightFade")!;
        Assert.False(left.IsHitTestVisible);
        Assert.False(right.IsHitTestVisible);
        Assert.False(left.IsVisible);
        Assert.False(right.IsVisible);

        window.Width = 650;
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
        Assert.False(left.IsVisible);
        Assert.True(right.IsVisible);

        var maximumOffset = scroll.Extent.Width - scroll.Viewport.Width;
        scroll.Offset = new Vector(maximumOffset / 2, 0);
        Dispatcher.UIThread.RunJobs();
        Assert.True(left.IsVisible);
        Assert.True(right.IsVisible);

        scroll.Offset = new Vector(maximumOffset, 0);
        Dispatcher.UIThread.RunJobs();
        Assert.True(left.IsVisible);
        Assert.False(right.IsVisible);

        window.Width = 1600;
        Dispatcher.UIThread.RunJobs();
        Assert.False(left.IsVisible);
        Assert.False(right.IsVisible);
        window.Close();
    }

    private static List<Button> SwatchButtons(ImageAssessmentControl control) =>
        control.GetLogicalDescendants()
            .OfType<Button>()
            .Where(button => button.CommandParameter is ColorLabel)
            .ToList();

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string NewRoot() =>
        Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-label-ui-{Guid.NewGuid():N}")).FullName;

    private static MainWindowViewModel NewViewModel(CatalogService catalog) =>
        new(catalog, baseLoader: null, loadMetadataAsync: _ => Task.CompletedTask);
}
