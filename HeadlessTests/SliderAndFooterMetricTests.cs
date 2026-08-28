using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
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
}
