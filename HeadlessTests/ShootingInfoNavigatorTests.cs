using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ShootingInfoNavigatorTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShootingInfoPreservesColumnsAndPlotAcrossMetadataAndScopes(bool gray)
    {
        await DevelopToolsBaselineTests.WithScene("normal", 1440, 900, (vm, scope) =>
        {
            using var theme = new TestUiScope(theme: gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
            // test-teardown-policy: allow - WithScene owns the MainWindow scope.
            scope.Show();
            var window = scope.Window!;
            ShellPaneLimitsTests.Settle(window);
            var panel = window.FindControl<DevelopEditPanel>("DevelopEditPanel")!;
            var box = panel.FindControl<Border>("DevelopScopeBox")!;
            var row = panel.FindControl<Grid>("ShootingInfoRow")!;
            var cells = row.Children.Cast<TextBlock>().ToArray();
            var histogram = panel.FindControl<HistogramView>("DevelopHistogram")!;
            var plot = histogram.Bounds;
            var baseHeight = box.Bounds.Height;
            Assert.False(row.IsVisible);
            // Compact scope: 20px selector + 2px margin + 5px gap + 80px plot + 10px padding.
            Assert.Equal(117, baseHeight);
            SetExif(vm.SelectedImage!);
            ShellPaneLimitsTests.Settle(window);
            Assert.True(row.IsVisible);
            Assert.Equal(new[] { "ISO 200", "50 mm", "f/3.2", "1/200 s" }, cells.Select(c => c.Text));
            Assert.Equal(plot, histogram.Bounds);
            Assert.Equal(baseHeight + row.Bounds.Height + 5, box.Bounds.Height);
            Assert.Equal(vm.SelectedImage!.ExposureTooltip, ToolTip.GetTip(cells[1]));
            Assert.All(cells, cell => Assert.Equal(10, cell.FontSize));
            var bounds = cells.Select(c => c.Bounds).ToArray();
            var rowBounds = row.Bounds;
            vm.SelectedImage.FNumber = null;
            ShellPaneLimitsTests.Settle(window);
            Assert.True(string.IsNullOrEmpty(cells[2].Text));
            Assert.Equal(bounds, cells.Select(c => c.Bounds));
            vm.SelectedImage.FocalLength = 7.1;
            Assert.Equal("7.1 mm", cells[1].Text);

            foreach (var selectedScope in new[] { ScopeView.Waveform, ScopeView.Histogram })
            {
                vm.SelectedScope = selectedScope;

                if (!vm.IsClippingOverlayLatched)
                {
                    vm.ToggleClippingOverlayCommand.Execute(null);
                }

                Assert.True(vm.IsClippingOverlayLatched);
                ShellPaneLimitsTests.Settle(window);
                Assert.Equal(rowBounds, row.Bounds);
            }

            vm.SelectedImage.ApplyMetadata(new ImageMetadata());
            ShellPaneLimitsTests.Settle(window);
            Assert.False(row.IsVisible);
            Assert.Equal(baseHeight, box.Bounds.Height);
            SetExif(vm.SelectedImage);
            Assert.Equal("ISO 200", cells[0].Text);
            var replacement = new ImageFile(vm.SelectedImage.FilePath) { Iso = 800 };
            vm.SelectedImage = replacement;
            ShellPaneLimitsTests.Settle(window);
            Assert.Equal("ISO 800", cells[0].Text);
            Assert.True(string.IsNullOrEmpty(cells[1].Text));
            vm.SelectedImage = null;
            ShellPaneLimitsTests.Settle(window);
            Assert.False(row.IsVisible);

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task NavigatorTracksZoomModesSelectionAndNarrowBar()
    {
        await DevelopToolsBaselineTests.WithScene("normal", 1440, 900, (vm, scope) =>
        {
            // test-teardown-policy: allow - WithScene owns the MainWindow scope.
            scope.Show();
            var window = scope.Window!;
            ShellPaneLimitsTests.Settle(window);
            var readout = window.FindControl<TextBlock>("NavigatorZoomReadout")!;
            var header = window.FindControl<Grid>("NavigatorHeader")!;
            var label = (TextBlock)header.Children[0];
            var headerBounds = header.Bounds;
            var labelBounds = label.Bounds;
            var slider = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!
                .FindControl<CompactSlider>("DevelopZoomSlider")!;

            foreach (var loupe in new[] { false, true })
            {
                vm.IsDevelopMode = !loupe;
                vm.IsLoupeMode = loupe;
                vm.ApplyFitZoom(.5);
                Assert.Equal("Fit", readout.Text);
                vm.ManualZoomLevel = 1.5;
                ShellPaneLimitsTests.Settle(window);
                Assert.Equal(string.Format("{0:P0}", 1.5), readout.Text);
                Assert.Equal(slider.FindControl<TextBlock>("ValueText")!.Text, readout.Text);
                vm.ApplyFitZoom(.5);
                Assert.Equal("Fit", readout.Text);
            }

            vm.IsLoupeMode = false;
            ShellPaneLimitsTests.Settle(window);
            Assert.Equal(string.Empty, readout.Text);
            Assert.Equal(headerBounds, header.Bounds);
            // The label keeps its original position and height; the first column also
            // retains the full header width when the readout is blank.
            Assert.Equal(labelBounds.Position, label.Bounds.Position);
            Assert.Equal(labelBounds.Height, label.Bounds.Height);
            Assert.Equal(header.Bounds.Width, label.Bounds.Width);
            Assert.Equal((header.Bounds.Height - label.Bounds.Height) / 2, label.Bounds.Y);
            vm.IsDevelopMode = true;
            window.Width = 1000;
            vm.ManualZoomLevel = 1.5;
            ShellPaneLimitsTests.Settle(window);
            Assert.False(slider.IsVisible);
            Assert.Equal(string.Format("{0:P0}", 1.5), readout.Text);
            vm.SelectedImage = null;
            ShellPaneLimitsTests.Settle(window);
            Assert.Equal(string.Empty, readout.Text);
            vm.ApplyFitZoom(.5);
            Assert.Equal(string.Empty, readout.Text);
            vm.IsDevelopMode = false;
            vm.IsDevelopMode = true;
            Assert.Equal(string.Empty, readout.Text);

            return Task.CompletedTask;
        });
    }

    internal static void SetExif(ImageFile image) => image.ApplyMetadata(new ImageMetadata
    {
        Iso = 200,
        FocalLength = 50,
        FocalLengthIn35mmFilm = 75,
        FNumber = 3.2,
        ExposureTime = "1/200"
    });
}
