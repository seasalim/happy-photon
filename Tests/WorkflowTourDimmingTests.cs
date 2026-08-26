using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace HappyPhoton.Tests;

public sealed class WorkflowTourDimmingTests
{
    [AvaloniaFact]
    public async Task TourSteps_DimUnrelatedRegionsOnly()
    {
        var fx = new CatalogVmFixture("tour-dimming");
        var catalog = fx.CreateCatalog("catalog");
        var vm = fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = vm };

        try
        {
            Dispatcher.UIThread.RunJobs();
            var startupGate = window.GetLogicalDescendants()
                .OfType<StartupGateView>()
                .Single();
            Assert.False(vm.IsStartupGateVisible);
            Assert.False(startupGate.IsVisible);

            var browse = window.FindControl<BrowseGridView>(
                "BrowseGridView")!;
            var emptyState = browse.FindControl<Border>("EmptyState")!;
            var developPane = window.FindControl<DevelopViewerPane>(
                "DevelopViewerPane")!;
            var developEmptyState = developPane.FindControl<Border>(
                "DevelopEmptyState")!;
            var leftPanel = window.FindControl<Border>("TourLeftPanel")!;
            var statusBar = window.FindControl<StatusBarView>(
                "TourStatusBar")!;
            var developEditPanel = window.FindControl<DevelopEditPanel>(
                "DevelopEditPanel")!;
            var browseReviewPane = window.FindControl<BrowseReviewPane>(
                "BrowseReviewPane")!;
            var exportSettingsPane = window.FindControl<ExportSettingsPane>(
                "ExportSettingsPane")!;
            var filterLabel = browse.FindControl<TextBlock>("FilterLabel")!;
            var filterScrollViewer = browse.FindControl<ScrollViewer>(
                "FilterScrollViewer")!;
            var actionsPanel = browse.FindControl<StackPanel>(
                "BrowseActionsPanel")!;
            var onlineOnlyMessage = browse.FindControl<TextBlock>(
                "OnlineOnlyMessage")!;
            var imageAssessment = browse.FindControl<ImageAssessmentControl>(
                "ImageAssessment")!;
            var thumbnailSizePanel = browse.FindControl<StackPanel>(
                "ThumbnailSizePanel")!;
            var burstsButton = browse.FindControl<Button>("BurstsButton")!;
            Assert.Contains(
                burstsButton,
                thumbnailSizePanel.GetLogicalDescendants().OfType<Button>());
            Assert.Contains("tour-region", thumbnailSizePanel.Classes);
            Control[] tourRegions =
            [
                leftPanel,
                statusBar,
                developEditPanel,
                browseReviewPane,
                exportSettingsPane,
                filterLabel,
                filterScrollViewer,
                actionsPanel,
                onlineOnlyMessage,
                imageAssessment,
                thumbnailSizePanel
            ];
            Control[] thumbnailSurface =
            [
                browse.FindControl<ScrollViewer>("ThumbnailScrollViewer")!,
                browse.FindControl<ItemsRepeater>("ThumbnailGrid")!
            ];

            Assert.True(Application.Current!.TryGetResource(
                "TourDimmedOpacity",
                window.ActualThemeVariant,
                out var opacityResource));
            var dimmedOpacity = Assert.IsType<double>(opacityResource);
            Assert.Equal(0.48, dimmedOpacity);

            var baselineOpacity = DimmedControls(window);
            var baselineInteraction = new Dictionary<
                Control,
                (bool IsEnabled, bool IsHitTestVisible)>(
                ReferenceEqualityComparer.Instance);
            foreach (var control in tourRegions)
            {
                baselineInteraction.Add(
                    control,
                    (control.IsEnabled, control.IsHitTestVisible));
            }

            AssertState(
                window,
                tourRegions,
                thumbnailSurface,
                baselineOpacity,
                baselineInteraction,
                dimmedOpacity);
            Assert.True(emptyState.IsVisible);
            Assert.True(developEmptyState.IsVisible);

            vm.StartWorkflowTour();
            Dispatcher.UIThread.RunJobs();
            Assert.False(emptyState.IsVisible);
            Assert.False(developEmptyState.IsVisible);
            AssertState(
                window,
                tourRegions,
                thumbnailSurface,
                baselineOpacity,
                baselineInteraction,
                dimmedOpacity,
                leftPanel,
                statusBar,
                developEditPanel,
                browseReviewPane,
                exportSettingsPane,
                filterLabel,
                filterScrollViewer,
                actionsPanel,
                onlineOnlyMessage,
                thumbnailSizePanel);

            vm.ShowDevelopTourStepCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(developEmptyState.IsVisible);
            AssertState(
                window,
                tourRegions,
                thumbnailSurface,
                baselineOpacity,
                baselineInteraction,
                dimmedOpacity,
                leftPanel,
                statusBar,
                browseReviewPane,
                exportSettingsPane,
                filterLabel,
                filterScrollViewer,
                actionsPanel,
                onlineOnlyMessage,
                imageAssessment,
                thumbnailSizePanel);

            vm.ShowExportTourStepCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(developEmptyState.IsVisible);
            AssertState(
                window,
                tourRegions,
                thumbnailSurface,
                baselineOpacity,
                baselineInteraction,
                dimmedOpacity,
                leftPanel,
                statusBar,
                developEditPanel,
                browseReviewPane,
                filterLabel,
                filterScrollViewer,
                onlineOnlyMessage,
                imageAssessment,
                thumbnailSizePanel);

            vm.FinishWorkflowTourCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(emptyState.IsVisible);
            Assert.True(developEmptyState.IsVisible);
            AssertState(
                window,
                tourRegions,
                thumbnailSurface,
                baselineOpacity,
                baselineInteraction,
                dimmedOpacity);

            vm.StartWorkflowTour();
            Dispatcher.UIThread.RunJobs();
            vm.EndWorkflowTourCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            AssertState(
                window,
                tourRegions,
                thumbnailSurface,
                baselineOpacity,
                baselineInteraction,
                dimmedOpacity);

            vm.StartWorkflowTour();
            Dispatcher.UIThread.RunJobs();
            vm.IsDevelopMode = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                WorkflowTourStep.ChooseWhatMatters,
                vm.WorkflowTourStep);
            Assert.False(vm.IsWorkflowTourPresented);
            AssertState(
                window,
                tourRegions,
                thumbnailSurface,
                baselineOpacity,
                baselineInteraction,
                dimmedOpacity);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            await vm.DisposeAsync();
            catalog.Dispose();
            fx.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task SelectedPhotograph_KeepsFocusedAssessmentBrightAndUsable()
    {
        var fx = new CatalogVmFixture("tour-focus");
        var catalog = fx.CreateCatalog("catalog");
        var vm = fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = vm };

        try
        {
            var browse = window.FindControl<BrowseGridView>(
                "BrowseGridView")!;
            var imageAssessment = browse.FindControl<ImageAssessmentControl>(
                "ImageAssessment")!;
            var thumbnailSizePanel = browse.FindControl<StackPanel>(
                "ThumbnailSizePanel")!;

            // Step 1 asks the user to curate, which is only possible once a
            // thumbnail is selected. The assessment control must therefore be
            // both bright and usable at that point, never dimmed with it.
            vm.Browse.SetImages(
                [new Models.ImageFile(fx.Path("photo.jpg"))]);
            vm.SelectedImage = vm.Browse.VisibleImages[0];
            vm.StartWorkflowTour();
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.IsChooseWhatMattersTourVisible);
            Assert.False(vm.IsDevelopMode);
            Assert.True(vm.HasSelectedImage);
            Assert.True(imageAssessment.IsEnabled);
            Assert.True(imageAssessment.IsHitTestVisible);
            Assert.Equal(1, imageAssessment.Opacity);
            Assert.Equal(0.48, thumbnailSizePanel.Opacity);

            // The focus glow is opt-in and marks the small target regions only.
            Assert.NotNull(imageAssessment.Effect);
            Assert.Null(thumbnailSizePanel.Effect);

            vm.EndWorkflowTourCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(imageAssessment.Effect);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            await vm.DisposeAsync();
            catalog.Dispose();
            fx.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task EachCoachmark_PointsAtItsOwnTarget()
    {
        var fx = new CatalogVmFixture("tour-pointer");
        var catalog = fx.CreateCatalog("catalog");
        var vm = fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = vm };

        try
        {
            Dispatcher.UIThread.RunJobs();
            var coachmarks = window.GetLogicalDescendants()
                .OfType<WorkflowCoachmark>()
                .ToDictionary(coachmark => coachmark.StepText);
            Assert.Equal(3, coachmarks.Count);

            // Step 1's assessment controls sit below, step 2's edit panel to the
            // right, and step 3 now lives with the Export workspace in the
            // right-hand pane rather than beneath the former Browse toolbar.
            Assert.Equal(CoachmarkPointer.Down, coachmarks["1 of 3"].Pointer);
            Assert.Equal(
                CoachmarkPointerAlignment.Center,
                coachmarks["1 of 3"].PointerAlignment);
            Assert.Equal(CoachmarkPointer.Right, coachmarks["2 of 3"].Pointer);
            Assert.Equal(
                CoachmarkPointerAlignment.Center,
                coachmarks["2 of 3"].PointerAlignment);
            Assert.Equal(CoachmarkPointer.Left, coachmarks["3 of 3"].Pointer);
            Assert.Equal(
                CoachmarkPointerAlignment.Center,
                coachmarks["3 of 3"].PointerAlignment);

            foreach (var (step, coachmark) in coachmarks)
            {
                var arrow = coachmark.FindControl<AvaloniaPath>("TourArrow")!;
                Assert.True(arrow.IsVisible, $"{step} trail should be visible.");
                Assert.False(
                    arrow.IsHitTestVisible,
                    $"{step} trail must never take input.");
                Assert.Equal(1, arrow.Opacity);
            }

            var step3Arrow = coachmarks["3 of 3"].FindControl<AvaloniaPath>("TourArrow")!;
            Assert.Equal(HorizontalAlignment.Right, step3Arrow.HorizontalAlignment);

            // No pointer means no trail, so the control stays usable elsewhere.
            coachmarks["1 of 3"].Pointer = CoachmarkPointer.None;
            Dispatcher.UIThread.RunJobs();

            Assert.False(
                coachmarks["1 of 3"].FindControl<AvaloniaPath>("TourArrow")!.IsVisible);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            await vm.DisposeAsync();
            catalog.Dispose();
            fx.Dispose();
        }
    }

    private static void AssertState(
        Window window,
        IReadOnlyList<Control> tourRegions,
        IReadOnlyList<Control> thumbnailSurface,
        IReadOnlyList<Control> baselineOpacity,
        IReadOnlyDictionary<Control, (bool IsEnabled, bool IsHitTestVisible)>
            baselineInteraction,
        double dimmedOpacity,
        params Control[] expectedDimmed)
    {
        foreach (var control in tourRegions)
        {
            var expected = expectedDimmed.Contains(
                control,
                ReferenceEqualityComparer.Instance)
                ? dimmedOpacity
                : 1;
            AssertOpacity(expected, control);
            Assert.True(control.IsHitTestVisible);
            Assert.Equal(
                baselineInteraction[control].IsEnabled,
                control.IsEnabled);
            Assert.Equal(
                baselineInteraction[control].IsHitTestVisible,
                control.IsHitTestVisible);
        }

        AssertOpacity(1, [.. thumbnailSurface]);

        var coachmarks = window.GetLogicalDescendants()
            .OfType<WorkflowCoachmark>()
            .ToArray();
        Assert.Equal(3, coachmarks.Length);
        AssertOpacity(1, coachmarks);

        var titleBars = window.GetLogicalDescendants()
            .OfType<HappyPhotonTitleBar>()
            .ToArray();
        Assert.Single(titleBars);
        AssertOpacity(1, titleBars);

        var zoomControls = window.GetLogicalDescendants()
            .OfType<ZoomPanControl>()
            .ToArray();
        Assert.Equal(2, zoomControls.Length);
        AssertOpacity(1, zoomControls);
        var assessmentElements = zoomControls.SelectMany(control => new Control[]
        {
            control.FindControl<Panel>("SurroundLayer")!,
            control.FindControl<Border>("AssessmentMat")!
        }).ToArray();
        Assert.Equal(4, assessmentElements.Length);
        AssertOpacity(1, assessmentElements);

        var expectedNonUnit = new HashSet<Control>(
            baselineOpacity,
            ReferenceEqualityComparer.Instance);
        expectedNonUnit.UnionWith(expectedDimmed);
        var actualNonUnit = new HashSet<Control>(
            DimmedControls(window),
            ReferenceEqualityComparer.Instance);
        Assert.Equal(expectedNonUnit.Count, actualNonUnit.Count);
        Assert.All(
            expectedNonUnit,
            control => Assert.Contains(control, actualNonUnit));
    }

    private static void AssertOpacity(
        double expected,
        params Control[] controls)
    {
        Assert.All(controls, control => Assert.Equal(expected, control.Opacity));
    }

    private static IReadOnlyList<Control> DimmedControls(Window window) =>
        window.GetLogicalDescendants()
            .OfType<Control>()
            .Where(control => control.Opacity != 1)
            .ToArray();
}

