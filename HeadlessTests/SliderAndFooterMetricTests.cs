using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SliderAndFooterMetricTests
{
    [AvaloniaFact]
    public async Task ControlBars_SplitActionsFromViewStateWithoutGrowingFootprint()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask);
        vm.SelectedImage = new ImageFile(Path.Combine(root.Path, "image.jpg"));
        var browse = new BrowseGridFooter { DataContext = vm };
        var browseHost = new StackPanel { Children = { browse } };
        var browseWindow = new Window { Width = 800, Height = 200, Content = browseHost };
        browseWindow.Show();
        Dispatcher.UIThread.RunJobs();
        var develop = new DevelopViewerPane { DataContext = vm };
        var developWindow = new Window { Width = 800, Height = 600, Content = develop };
        developWindow.Show();
        Dispatcher.UIThread.RunJobs();

        var browseSurface = browse.FindControl<Border>("BrowseFooterSurface")!;
        var browseActions = browse.FindControl<ImageAssessmentControl>("ImageAssessment")!;
        var browseState = browse.FindControl<StackPanel>("ThumbnailSizePanel")!;
        var developBar = develop.FindControl<Border>("DevelopControlBar")!;
        var developActions = develop.FindControl<StackPanel>(
            "DevelopImageActionsPanel")!;
        var developState = develop.FindControl<StackPanel>(
            "DevelopViewStatePanel")!;

        Assert.True(
            browseSurface.Bounds.Height <= 38,
            $"The shortened Browse footer measured {browseSurface.Bounds.Height}px; " +
            $"padding={browseSurface.Padding}; actions={browseActions.Bounds}; " +
            $"state={browseState.Bounds}.");
        AssertAnchoredAtOppositeEnds(browseSurface, browseActions, browseState);
        AssertAnchoredAtOppositeEnds(developBar, developActions, developState);
        Assert.True(
            developActions.Bounds.Width + developState.Bounds.Width +
            developBar.Padding.Left + developBar.Padding.Right <= 683,
            "The split Develop groups exceed the measured 683px baseline.");

        browseWindow.Close();
        developWindow.Close();
    }

    [AvaloniaFact]
    public async Task RawChipsShareBodyFaceAndSize()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask);
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(Path.Combine(root.Path, "image.dng"));
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 260, Height = 700, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var chips = panel.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("raw-chip"))
            .ToArray();
        Assert.Equal(2, chips.Length);
        Assert.All(chips, chip =>
        {
            var text = Assert.IsType<TextBlock>(chip.Child);
            Assert.Equal(10, text.FontSize);
            Assert.Equal(
                ThemeResourceTests.Resource<FontFamily>("FontBody", ThemeVariant.Dark),
                text.FontFamily);
            Assert.Equal(new Thickness(5, 1), chip.Padding);
            Assert.Equal(new Thickness(1), chip.BorderThickness);
        });
        Assert.Equal(
            ["Clip", "Blend"],
            panel.FindControl<ListBox>("HighlightHandlingControl")!
                .GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible)
                .Select(text => text.Text!)
                .ToArray());
        Assert.Equal(
            ["Fine", "Med", "Coarse"],
            panel.FindControl<EffectsEditGroup>("EffectsEditGroup")!
                .FindControl<ListBox>("GrainSizeControl")!
                .GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible)
                .Select(text => text.Text!)
                .ToArray());

        window.Close();
    }

    [AvaloniaFact]
    public async Task ProductionSliderLabels_FitTheSharedLabelColumn()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask);
        vm.IsCropMode = true;
        var content = new StackPanel
        {
            Children =
            {
                new DevelopEditPanel { DataContext = vm },
                new DevelopViewerPane { DataContext = vm },
                new ExportSettingsPane { DataContext = vm }
            }
        };
        var window = new Window { Width = 900, Height = 2200, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var sliders = content.GetLogicalDescendants()
                .OfType<CompactSlider>().ToArray();
            Assert.Equal(25, sliders.Length);
            Assert.Contains(sliders, slider => slider.Label == "Luma NR");
            foreach (var slider in sliders)
            {
                var label = slider.FindControl<TextBlock>("LabelText")!;
                var labelColumn = slider.FindControl<Grid>("LayoutGrid")!
                    .ColumnDefinitions[0].ActualWidth;
                var intrinsic = IntrinsicWidth(label);
                Assert.True(intrinsic <= labelColumn,
                    $"{slider.Label} measures {intrinsic:F2}px against " +
                    $"the {labelColumn:F2}px label column.");
                Assert.Equal(slider.Label, ToolTip.GetTip(label));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OverlongSliderLabel_UsesEllipsisAndFullLabelTooltip()
    {
        const string fullLabel = "A deliberately over-long slider label";
        var slider = new CompactSlider { Label = fullLabel, Width = 260 };
        var window = new Window { Content = slider };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var label = slider.FindControl<TextBlock>("LabelText")!;
        Assert.True(IntrinsicWidth(label) > label.Bounds.Width);
        Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
        Assert.Equal(fullLabel, ToolTip.GetTip(label));
        window.Close();
    }

    [AvaloniaFact]
    public void FooterPairGlyph_MatchesCompareAndFitsWhileTileChipDiffers()
    {
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(), $"happy-photon-footer-{Guid.NewGuid():N}"));
        var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        var paired = new ImageFile(Path.Combine(catalog.CatalogPath, "pair.jpg"))
        {
            IsRawJpegPair = true
        };
        vm.Browse.SetImages([paired]);
        var browse = new BrowseGridView
        {
            DataContext = vm,
            Images = vm.Browse.VisibleImages
        };
        var window = new Window { Width = 900, Height = 700, Content = browse };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var pairsButton = browse.FindControl<Button>("PairsButton")!;
        var pairGlyph = Assert.IsType<TextBlock>(pairsButton.Content);
        var compareGlyph = Assert.IsType<TextBlock>(
            browse.FindControl<ToggleButton>("CompareViewButton")!.Content);
        var tileGlyph = Assert.IsType<TextBlock>(Assert.Single(
            browse.GetVisualDescendants().OfType<Border>(),
            border => border.Name == "RawJpegPairChip" &&
                      border.IsEffectivelyVisible).Child);
        Assert.Equal(compareGlyph.FontSize, pairGlyph.FontSize);
        Assert.NotEqual(pairGlyph.FontSize, tileGlyph.FontSize);
        var origin = pairGlyph.TranslatePoint(default, pairsButton);
        Assert.True(origin.HasValue);
        Assert.True(origin.Value.X >= pairsButton.Padding.Left &&
                    origin.Value.X + pairGlyph.Bounds.Width <=
                    pairsButton.Bounds.Width - pairsButton.Padding.Right,
            $"J+R glyph bounds {pairGlyph.Bounds} at {origin} exceed " +
            $"the {pairsButton.Bounds.Width}px button content box.");
        window.Close();
        vm.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [AvaloniaFact]
    public void PairStyledPropertiesAndFreshFooter_DefaultOff()
    {
        Assert.False(new BrowseGridView().ShowPairs);
        var footer = new BrowseGridFooter();
        Assert.False(footer.ShowPairs);
        Assert.DoesNotContain(
            "active",
            footer.FindControl<Button>("PairsButton")!.Classes);
    }

    private static double IntrinsicWidth(TextBlock source)
    {
        var probe = new TextBlock
        {
            Text = source.Text,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            FontStyle = source.FontStyle,
            FontWeight = source.FontWeight,
            FontStretch = source.FontStretch,
            LetterSpacing = source.LetterSpacing
        };
        probe.Measure(Size.Infinity);
        return probe.DesiredSize.Width;
    }

    private static void AssertAnchoredAtOppositeEnds(
        Border surface,
        Control actions,
        Control state)
    {
        var actionOrigin = actions.TranslatePoint(default, surface)!.Value;
        var stateOrigin = state.TranslatePoint(default, surface)!.Value;
        Assert.Equal(surface.Padding.Left, actionOrigin.X, precision: 3);
        Assert.Equal(
            surface.Bounds.Width - surface.Padding.Right,
            stateOrigin.X + state.Bounds.Width,
            precision: 3);
        Assert.True(actionOrigin.X + actions.Bounds.Width <= stateOrigin.X);
    }
}
