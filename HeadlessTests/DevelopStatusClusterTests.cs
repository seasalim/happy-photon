using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace HappyPhoton.Tests;

public sealed class DevelopStatusClusterTests
{
    [AvaloniaFact]
    public async Task StatusChangesKeepActionsFixedAndUpdateMarksAndTooltip()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-develop-status-{Guid.NewGuid():N}")).FullName;
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await using var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        var image = new ImageFile(Path.Combine(root, "photo.jpg"));
        vm.SelectedImage = image;
        vm.IsDevelopMode = true;
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = vm };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var controlBar = window.FindControl<Border>("DevelopControlBar")!;
            var flagSlot = window.FindControl<Panel>("FlagStatusSlot")!;
            var labelSlot = window.FindControl<Panel>("ColorLabelStatusSlot")!;
            var picked = window.FindControl<ShapePath>("PickedStatusMark")!;
            var rejected = window.FindControl<ShapePath>("RejectedStatusMark")!;
            var dot = window.FindControl<Border>("ColorLabelStatusDot")!;
            var rotateLeft = window.FindControl<Button>("RotateLeftButton")!;
            var baselineBarBounds = controlBar.Bounds;
            var baselineActionBounds = rotateLeft.Bounds;

            Assert.Equal(20, flagSlot.Bounds.Width);
            Assert.Equal(14, labelSlot.Bounds.Width);
            Assert.False(picked.IsVisible);
            Assert.False(rejected.IsVisible);
            Assert.False(dot.IsVisible);

            image.Flag = ImageFlag.Picked;
            AssertStableLayout();
            Assert.True(picked.IsVisible);
            Assert.False(rejected.IsVisible);
            Assert.Equal("Picked", ToolTip.GetTip(picked));
            Assert.Same(
                ThemeResourceTests.Brush("SelectionCheck", ThemeVariant.Dark),
                picked.Stroke);
            Assert.Same(picked.Stroke, picked.Fill);

            image.Flag = ImageFlag.Rejected;
            AssertStableLayout();
            Assert.False(picked.IsVisible);
            Assert.True(rejected.IsVisible);
            Assert.Equal("Rejected", ToolTip.GetTip(rejected));
            Assert.Same(
                ThemeResourceTests.Brush("RejectMark", ThemeVariant.Dark),
                rejected.Stroke);

            image.Flag = ImageFlag.Unflagged;
            AssertStableLayout();
            Assert.False(picked.IsVisible);
            Assert.False(rejected.IsVisible);

            image.ColorLabel = ColorLabel.Red;
            AssertStableLayout();
            Assert.True(dot.IsVisible);
            Assert.Equal(HappyPhotonColors.GetColorLabelBrush(ColorLabel.Red), dot.Background);
            Assert.Equal("Red", ToolTip.GetTip(dot));

            vm.SetColorLabelNames(new Dictionary<ColorLabel, string>(ColorLabelNames.Defaults)
            {
                [ColorLabel.Red] = "Select"
            });
            AssertStableLayout();
            Assert.Equal("Select", ToolTip.GetTip(dot));

            image.ColorLabel = ColorLabel.None;
            AssertStableLayout();
            Assert.False(dot.IsVisible);

            void AssertStableLayout()
            {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(baselineBarBounds, controlBar.Bounds);
                Assert.Equal(baselineActionBounds, rotateLeft.Bounds);
                Assert.Equal(20, flagSlot.Bounds.Width);
                Assert.Equal(14, labelSlot.Bounds.Width);
            }
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }
}
