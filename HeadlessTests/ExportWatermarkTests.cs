using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportWatermarkTests
{
    [AvaloniaFact]
    public async Task InstalledFamilyWithDifferentResolvedFaceSurvivesSnapshot()
    {
        var manager = FontManager.Current;
        var family = manager.SystemFonts.FirstOrDefault(candidate =>
            manager.TryGetGlyphTypeface(new Typeface(candidate.Name), out var face) &&
            !string.Equals(candidate.Name, face.FamilyName, StringComparison.OrdinalIgnoreCase));
        Assert.SkipWhen(family == null, "No installed family resolves to a differently named face on this host.");
        using var fixture = new CatalogVmFixture("watermark-font-alias");
        using var catalog = fixture.CreateCatalog();
        await using var vm = fixture.CreateViewModel(catalog);
        vm.WorkspaceMode = WorkspaceMode.Export;
        var mark = vm.ExportSettings.Watermark;
        mark.Enabled = true;
        mark.Text = "© Jane Doe 2026 WMW iii";
        mark.Size = 15;
        foreach (var expanded in new[] { false, true })
        {
            vm.IsWatermarkExpanded = expanded;
            foreach (var requested in new[] { family!.Name, family.Name.ToUpperInvariant() })
            {
                mark.FontFamily = requested;
                var snapshot = vm.ExportSettings.SnapshotOutput().Watermark!;
                Assert.Equal(requested, snapshot.FontFamily);
                if (expanded) Assert.True(vm.SelectedWatermarkFont!.Installed);
                using var actual = new MagickImage(MagickColors.Black, 600, 400);
                using var fallback = new MagickImage(MagickColors.Black, 600, 400);
                WatermarkRenderer.Apply(actual, snapshot);
                WatermarkRenderer.Apply(fallback, snapshot with { FontFamily = manager.DefaultFontFamily.Name });
                using var pixels = actual.GetPixelsUnsafe();
                using var defaultPixels = fallback.GetPixelsUnsafe();
                var rgb = pixels.ToShortArray(PixelMapping.RGB)!;
                Assert.Contains(rgb, value => value > 0);
                Assert.NotEqual(defaultPixels.ToShortArray(PixelMapping.RGB), rgb);
            }
        }
    }

    [AvaloniaFact]
    public async Task ControlsFollowEnabledEdgeAndFontState()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(catalog);
        vm.WorkspaceMode = WorkspaceMode.Export;
        Assert.Empty(vm.WatermarkFonts);
        var pane = new ExportSettingsPane { DataContext = vm };
        using var scope = new TestUiScope(new Window { Width = 280, Height = 1100, Content = pane });
        var expander = pane.FindControl<Expander>("ExportWatermarkExpander")!;
        expander.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        var options = Assert.Single(pane.GetVisualDescendants().OfType<ExportWatermarkOptions>());
        var body = options.FindControl<StackPanel>("WatermarkBody")!;
        var checkbox = options.FindControl<CheckBox>("WatermarkEnabled")!;
        Assert.True(checkbox.IsEffectivelyEnabled);
        Assert.False(body.IsEffectivelyEnabled);
        Assert.Equal(ThemeResourceTests.Resource<double>("DisabledOpacity", Avalonia.Styling.ThemeVariant.Dark), body.Opacity);
        Assert.All(body.GetVisualDescendants().OfType<Control>(), control => Assert.False(control.IsEffectivelyEnabled));
        Assert.Equal("Off", vm.WatermarkSummary);
        Assert.NotEmpty(vm.WatermarkFonts);
        Assert.Equal(vm.WatermarkFonts.Select(f => f.Name).Order(StringComparer.CurrentCultureIgnoreCase),
            vm.WatermarkFonts.Select(f => f.Name));
        var fonts = vm.WatermarkFonts;
        vm.IsWatermarkExpanded = false;
        vm.IsWatermarkExpanded = true;
        Assert.Equal(fonts, vm.WatermarkFonts);
        vm.ExportSettings.Watermark.Text = "© Jane Doe";
        checkbox.IsChecked = true;
        Assert.True(body.IsEffectivelyEnabled);
        Assert.Equal(1, body.Opacity);
        Assert.Equal("\"© Jane Doe\" · Bottom right", vm.WatermarkSummary);
        foreach (var edge in Enum.GetValues<WatermarkEdge>())
        {
            vm.ExportSettings.Watermark.Edge = edge;
            Dispatcher.UIThread.RunJobs();
            var side = edge is WatermarkEdge.Left or WatermarkEdge.Right;
            Assert.Equal(side, options.FindControl<CheckBox>("WatermarkRotate")!.IsVisible);
            Assert.Equal(edge != WatermarkEdge.Center, options.FindControl<StackPanel>("WatermarkAlignmentRow")!.IsVisible);
            Assert.Equal(side ? new[] { "Top", "Middle", "Bottom" } : ["Left", "Center", "Right"], vm.WatermarkAlignmentLabels);
            Assert.Equal(WatermarkAlignment.End, vm.ExportSettings.Watermark.Alignment);
        }
        var mark = vm.ExportSettings.Watermark;
        foreach (var edge in new[] { WatermarkEdge.Left, WatermarkEdge.Right })
        foreach (var alignment in Enum.GetValues<WatermarkAlignment>())
        foreach (var rotated in new[] { false, true })
        {
            mark.Edge = edge;
            mark.Alignment = alignment;
            mark.RotateAlongEdge = rotated;
            Assert.Equal($"\"© Jane Doe\" · {edge} edge · {vm.WatermarkAlignmentLabels[(int)alignment]}" +
                (rotated ? " · Rotated" : ""), vm.WatermarkSummary);
        }
        mark.Edge = WatermarkEdge.Center;
        Assert.Equal("\"© Jane Doe\" · Center", vm.WatermarkSummary);
        var installed = vm.WatermarkFonts.First(font => font.Installed);
        mark.FontFamily = installed.Name.ToUpperInvariant();
        Assert.Equal(installed.Name, vm.SelectedWatermarkFont!.Name);
        Assert.True(vm.SelectedWatermarkFont.Installed);
        vm.ExportSettings.Watermark.FontFamily = "No Such Family 269";
        Assert.Equal("No Such Family 269 (not installed)", vm.SelectedWatermarkFont!.Label);
        Assert.Equal(FontManager.Current.DefaultFontFamily.Name, vm.ExportSettings.SnapshotOutput().Watermark!.FontFamily);
        vm.ExportSettings.Watermark.Text = " ";
        Assert.Equal("Enter watermark text.", vm.ExportSettings.ValidationReason);
        Assert.False(vm.CanRunExport);
    }

    [AvaloniaFact]
    public async Task EveryWatermarkPropertyRefreshesOpenProof()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(catalog, new StandardBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask);
        var source = Path.Combine(root.Path, "photo.jpg");
        File.Copy(GoldenTestPaths.Asset("srgb-reference.jpg"), source);
        vm.Browse.SetImages([new ImageFile(source)]);
        vm.Browse.SelectAllVisible();
        vm.ExportSettings.OutputFolder = root.Path;
        vm.WorkspaceMode = WorkspaceMode.Export;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);
        vm.ExportSettings.ShowProof = true;
        await TestWaits.UntilAsync(() => vm.ExportProofCaption == "PROOF · Full size · No resizing · sRGB");
        var mark = vm.ExportSettings.Watermark;
        Action[] changes = [() => mark.Text = "© Jane Doe", () => mark.Enabled = true,
            () => mark.FontFamily = "Arial", () => mark.Bold = true, () => mark.Italic = true,
            () => mark.Size = 5, () => mark.Color = WatermarkColor.Black, () => mark.Opacity = 75,
            () => mark.Edge = WatermarkEdge.Right, () => mark.Alignment = WatermarkAlignment.Start,
            () => mark.RotateAlongEdge = false, () => mark.Margin = 5];
        foreach (var change in changes)
        {
            var previous = vm.PreviewImage;
            change();
            await TestWaits.UntilAsync(() => vm.PreviewImage != previous &&
                !vm.ExportProofCaption.Contains("UPDATING"));
        }
        mark.Text = " ";
        Assert.Equal("Enter watermark text.", vm.ExportValidationReason);
        Assert.False(vm.CanRunExport);
    }
}
