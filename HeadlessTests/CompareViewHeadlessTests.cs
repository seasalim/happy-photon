using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class CompareViewHeadlessTests
{
    [AvaloniaFact]
    public async Task ToggleTeachesGateAndGridAndEscapeRestoreAssessedSelection()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var images = new[]
        {
            new ImageFile(Path.Combine(root.Path, "first.jpg")),
            new ImageFile(Path.Combine(root.Path, "second.jpg"))
        };
        var states = await catalog.LoadOrCreateImageStatesAsync(
            images.Select(image => image.FilePath).ToArray());
        foreach (var image in images)
            image.CatalogId = states[image.FilePath].CatalogId;
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[0];
        vm.ToggleImageSelection(images[0]);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Drain();

        try
        {
            var browse = Descendant<BrowseGridView>(window, "BrowseGridView");
            var compareButton = Descendant<ToggleButton>(
                browse,
                "CompareViewButton");
            Assert.IsType<ToggleButton>(compareButton);
            var compareGlyph = Assert.IsType<TextBlock>(compareButton.Content);
            Assert.Equal("X|Y", compareGlyph.Text);
            Assert.Contains("JetBrains Mono", compareGlyph.FontFamily.ToString());
            Assert.Equal(10, compareGlyph.FontSize);
            Assert.Equal(Avalonia.Media.FontWeight.Bold, compareGlyph.FontWeight);
            Assert.Equal(0.5, compareGlyph.LetterSpacing);
            Assert.Contains("develop-action", compareButton.Classes);
            // Wider than the square icon buttons on purpose: three mono glyphs do
            // not fit the 24px square, and at 8px they were clipped outright.
            var burstsWidth = Descendant<Button>(browse, "BurstsButton").Bounds.Width;
            Assert.True(compareButton.Bounds.Width > burstsWidth,
                $"Compare toggle {compareButton.Bounds.Width} should exceed the " +
                $"square icon buttons at {burstsWidth}.");
            Assert.True(compareGlyph.Bounds.Width <= compareButton.Bounds.Width,
                $"Glyph {compareGlyph.Bounds.Width} is clipped by the " +
                $"{compareButton.Bounds.Width}px toggle.");
            Assert.True(compareButton.IsEffectivelyVisible);
            Assert.False(compareButton.IsEffectivelyEnabled);
            Assert.False(compareButton.IsChecked);
            Assert.Equal(
                "Select 2–4 images to compare",
                ToolTip.GetTip(compareButton));
            Assert.True(ToolTip.GetShowOnDisabled(compareButton));
            var disabledOpacity = compareButton.Opacity;
            Assert.InRange(disabledOpacity, 0.01, 0.99);

            vm.ToggleImageSelection(images[1]);
            Drain();
            Assert.True(compareButton.IsEffectivelyEnabled);
            Assert.True(compareButton.Opacity > disabledOpacity);
            Assert.Equal("Compare (2–4 images)", ToolTip.GetTip(compareButton));
            compareButton.Command!.Execute(compareButton.CommandParameter);
            Drain();

            Assert.True(vm.IsCompareMode);
            var compare = Descendant<CompareView>(browse, "CompareView");
            Assert.True(compare.IsEffectivelyVisible);
            Assert.True(compareButton.IsEffectivelyVisible);
            Assert.True(compareButton.IsEffectivelyEnabled);
            Assert.True(compareButton.IsChecked);
            Assert.Equal("Return to grid", ToolTip.GetTip(compareButton));
            Assert.False(Descendant<Border>(browse, "SelectionBar").IsEffectivelyVisible);
            Assert.False(Descendant<ScrollViewer>(browse, "ThumbnailScrollViewer")
                .IsEffectivelyVisible);
            Assert.False(Descendant<Button>(browse, "BurstsButton")
                .IsEffectivelyEnabled);
            Assert.False(Descendant<RadioButton>(browse, "SmallThumbnailButton")
                .IsEffectivelyEnabled);
            Assert.False(Descendant<RadioButton>(browse, "MediumThumbnailButton")
                .IsEffectivelyEnabled);
            Assert.False(Descendant<RadioButton>(browse, "LargeThumbnailButton")
                .IsEffectivelyEnabled);
            Assert.True(Descendant<ImageAssessmentControl>(browse, "ImageAssessment")
                .IsEffectivelyEnabled);
            Assert.Single(
                browse.GetVisualDescendants().OfType<ImageAssessmentControl>(),
                control => control.IsEffectivelyVisible);
            Assert.Equal(2, compare
                .GetVisualDescendants().OfType<ZoomPanControl>().Count());
            Assert.Single(images, image => image.IsActive);

            await vm.SetRatingCommand.ExecuteAsync(5);
            compareButton.Command!.Execute(compareButton.CommandParameter);
            Drain();

            Assert.False(vm.IsCompareMode);
            Assert.True(compareButton.IsEffectivelyVisible);
            Assert.True(compareButton.IsEffectivelyEnabled);
            Assert.False(compareButton.IsChecked);
            Assert.Equal("Compare (2–4 images)", ToolTip.GetTip(compareButton));
            Assert.All(images, image => Assert.True(image.IsSelected));
            Assert.Same(images[0], vm.SelectedImage);
            Assert.Equal(5, images[0].Rating);

            compareButton.Command!.Execute(compareButton.CommandParameter);
            Drain();
            Drain();

            // Escape must travel the real input path: key events originate at
            // the focused element, so compare entry has to hand focus to the
            // view (the grid tile that held it collapsed). Raising on the
            // compare control directly would pass even with dead focus.
            Assert.True(compare.IsKeyboardFocusWithin);
            window.KeyPress(
                Key.Escape,
                RawInputModifiers.None,
                PhysicalKey.Escape,
                null);
            Drain();
            Drain();

            Assert.False(vm.IsCompareMode);
            Assert.True(browse.IsKeyboardFocusWithin);
            Assert.True(compareButton.IsEffectivelyVisible);
            Assert.False(compareButton.IsChecked);
            Assert.All(images, image => Assert.True(image.IsSelected));
            Assert.Same(images[0], vm.SelectedImage);
            Assert.Equal(5, images[0].Rating);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CompareToDevelopRoundTripRestoresTheGrid()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var images = new[]
        {
            new ImageFile(Path.Combine(root.Path, "first.jpg")),
            new ImageFile(Path.Combine(root.Path, "second.jpg"))
        };
        var states = await catalog.LoadOrCreateImageStatesAsync(
            images.Select(image => image.FilePath).ToArray());
        foreach (var image in images)
            image.CatalogId = states[image.FilePath].CatalogId;
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[0];
        foreach (var image in images) vm.ToggleImageSelection(image);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Drain();

        try
        {
            var browse = window.GetVisualDescendants()
                .OfType<BrowseGridView>().First();
            var scroller = Descendant<ScrollViewer>(
                browse, "ThumbnailScrollViewer");
            var bar = Descendant<Border>(browse, "SelectionBar");
            var toggle = Descendant<ToggleButton>(browse, "CompareViewButton");

            vm.ToggleCompareCommand.Execute(null);
            Drain();
            Drain();
            Assert.True(vm.IsCompareMode);

            // Enter switches to Develop; compare closes as part of leaving.
            window.KeyPress(Key.Enter, RawInputModifiers.None,
                PhysicalKey.Enter, null);
            Drain();
            Drain();
            Assert.True(vm.IsDevelopMode);
            Assert.False(vm.IsCompareMode);
            Assert.True(Descendant<DevelopViewerPane>(
                window, "DevelopViewerPane").IsEffectivelyVisible);

            // G returns to Browse. The assertions target the CONTROLS, not the
            // view-model property: the blank-screen defect was a stale binding,
            // with IsBrowseGridVisible true while the scroller stayed hidden.
            window.KeyPress(Key.G, RawInputModifiers.None, PhysicalKey.G, "g");
            Drain();
            Drain();
            Assert.True(vm.IsBrowseGridVisible);
            Assert.True(scroller.IsEffectivelyVisible,
                "The thumbnail grid stayed hidden after compare -> Develop -> Browse.");
            Assert.True(bar.IsEffectivelyVisible,
                "The filter bar stayed hidden after compare -> Develop -> Browse.");
            Assert.True(toggle.IsEffectivelyEnabled,
                "The compare toggle came back disabled after the round trip.");
            Assert.True(vm.ToggleCompareCommand.CanExecute(null));

            // And the toggle still actually works.
            vm.ToggleCompareCommand.Execute(null);
            Drain();
            Drain();
            Assert.True(vm.IsCompareMode);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    private static T Descendant<T>(Control root, string name)
        where T : Control =>
        root.GetVisualDescendants().Prepend(root)
            .OfType<T>()
            .Single(control => control.Name == name);

    private static void Drain() => Dispatcher.UIThread.RunJobs();
}
