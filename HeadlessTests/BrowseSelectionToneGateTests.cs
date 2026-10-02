using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// VISUALS-WP10 G1 and G3: Browse selection is shown by tile tone alone, so each
// step of the tone ladder must read on its own in both themes.
public sealed class BrowseSelectionToneGateTests(ITestOutputHelper output)
{
    public static TheoryData<string> Themes => new() { "Dark", "MidGray" };

    [AvaloniaTheory]
    [MemberData(nameof(Themes))]
    public async Task G1_TileToneLadderStepsReadInBothThemes(string themeName)
    {
        var theme = themeName == "Dark" ? ThemeVariant.Dark : HappyPhotonThemes.MidGray;
        using var fixture = new CatalogVmFixture("wp10-tone");
        using var catalog = fixture.CreateCatalog();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(HappyPhoton.ViewModels.MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(fixture.Path("tone.jpg")) { MetadataLoaded = true };
        var other = new ImageFile(fixture.Path("active.jpg")) { MetadataLoaded = true };
        vm.Browse.SetImages([image, other]);
        vm.SelectedImage = other;
        var control = new BrowseGridView { DataContext = vm, Images = vm.Browse.VisibleImages };
        var window = new Window { Width = 900, Height = 600, Content = control };
        using var scope = new TestUiScope(window, theme);
        Dispatcher.UIThread.RunJobs();

        var tile = Assert.Single(
            control.GetVisualDescendants().OfType<Border>(),
            border => border.Name == "ThumbnailTile" && ReferenceEquals(border.DataContext, image));
        Assert.True(tile.Bounds.Width > 0, "The tile must be realized.");

        // The 0.13 s brush transition would sample a blend; the gate reads the settled state.
        tile.Transitions = null;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var away = new Point(window.Width - 1, window.Height - 1);
        var centre = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), window)!.Value;

        var unselected = Settle(window, tile, away);
        var hovered = Settle(window, tile, centre);
        image.IsSelected = true;
        var selectedHovered = Settle(window, tile, centre);
        var selected = Settle(window, tile, away);
        var hoverStep = Lightness(hovered) - Lightness(unselected);
        var selectedStep = Lightness(selected) - Lightness(hovered);

        output.WriteLine(
            $"VISUALS-WP10 G1 {themeName}: unselected {unselected} L*{Lightness(unselected):F2}, " +
            $"hover {hovered} L*{Lightness(hovered):F2}, selected {selected} L*{Lightness(selected):F2}, " +
            $"selected+hover {selectedHovered}; dL* {hoverStep:F2} and {selectedStep:F2}");

        var activeTile = Assert.Single(
            control.GetVisualDescendants().OfType<Border>(),
            border => border.Name == "ThumbnailTile" && ReferenceEquals(border.DataContext, other));
        Assert.True(activeTile.Classes.Contains("active"), "The active photo must carry the ring.");
        Assert.Equal(selected, selectedHovered);
        Assert.True(hoverStep >= 8, $"{themeName} unselected→hover ΔL* {hoverStep:F2}.");
        Assert.True(selectedStep >= 8, $"{themeName} hover→selected ΔL* {selectedStep:F2}.");
        Assert.True(Lightness(selected) < 50, $"{themeName} selected L* {Lightness(selected):F2}.");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Themes))]
    public void G3_SelectionContrastRatios(string themeName)
    {
        var theme = themeName == "Dark" ? ThemeVariant.Dark : HappyPhotonThemes.MidGray;
        var surface = ThemeResourceTests.Brush("SelectionSurface", theme).Color;
        var text = ThemeResourceTests.Contrast(ThemeResourceTests.Brush("TextPrimary", theme).Color, surface);
        var ring = ThemeResourceTests.Contrast(ThemeResourceTests.Brush("ActiveImageRing", theme).Color, surface);

        output.WriteLine($"VISUALS-WP10 G3 {themeName}: TextPrimary {text:F2}:1, ActiveImageRing {ring:F2}:1 on {surface}");

        Assert.True(text >= 4.5);
        Assert.True(ring >= 3);
    }

    private static Color Settle(Window window, Border tile, Point pointer)
    {
        window.MouseMove(pointer);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        return Assert.IsAssignableFrom<ISolidColorBrush>(tile.Background).Color;
    }

    private static double Lightness(Color color)
    {
        var y = 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

        return y > 216d / 24389 ? 116 * Math.Cbrt(y) - 16 : y * 24389d / 27;
    }

    private static double Linear(byte channel)
    {
        var value = channel / 255d;

        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
