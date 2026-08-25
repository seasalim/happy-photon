using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;
using Shape = Avalonia.Controls.Shapes.Shape;

namespace HappyPhoton.Tests;

public sealed class DevelopAssessmentFeedbackTests
{
    [AvaloniaFact]
    public async Task DevelopBarContainsActionsOnlyAndHostsSingleCenteredToast()
    {
        await WithWindowAsync((window, _) =>
        {
            var controlBar = window.FindControl<Border>("DevelopControlBar")!;
            var actions = Assert.IsType<StackPanel>(controlBar.Child);
            var previous = window.FindControl<Button>("PreviousImageButton")!;
            var next = window.FindControl<Button>("NextImageButton")!;
            var rotateLeft = window.FindControl<Button>("RotateLeftButton")!;

            // Actions lead the bar; the first passive child divides navigation
            // from the editing actions.
            Assert.Same(previous, actions.Children[0]);
            Assert.Same(next, actions.Children[1]);
            Assert.IsType<Avalonia.Controls.Shapes.Rectangle>(actions.Children[2]);
            Assert.Same(rotateLeft, actions.Children[3]);

            var overlay = Assert.Single(
                window.GetLogicalDescendants().OfType<AssessmentFeedbackOverlay>());
            Assert.Same(
                window.FindControl<AssessmentFeedbackOverlay>(
                    "DevelopAssessmentFeedbackOverlay"),
                overlay);
            Assert.False(overlay.IsHitTestVisible);
            Assert.Equal(HorizontalAlignment.Center, overlay.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Bottom, overlay.VerticalAlignment);
            Assert.True(overlay.Margin.Bottom >= 24);
        });
    }

    [AvaloniaFact]
    public async Task ToastHasNoRestingAssessmentAndRemainsOverImageTools()
    {
        await WithWindowAsync((window, vm) =>
        {
            var image = vm.SelectedImage!;
            var overlay = window.FindControl<AssessmentFeedbackOverlay>(
                "DevelopAssessmentFeedbackOverlay")!;
            var content = overlay.FindControl<Grid>(
                "AssessmentFeedbackOverlayContent")!;
            var presenter = overlay.FindControl<Border>(
                "AssessmentFeedbackPresenter")!;

            image.Flag = ImageFlag.Picked;
            image.ColorLabel = ColorLabel.Red;
            image.Rating = 3;
            Dispatcher.UIThread.RunJobs();

            // A fully assessed photograph draws nothing: the toast is the whole
            // overlay, so its only content is the one confirmation TextBlock —
            // no marks, dots, or stars may creep back in beside it.
            Assert.False(content.IsVisible);
            Assert.Single(overlay.GetLogicalDescendants().OfType<TextBlock>());
            Assert.Same(
                presenter,
                Assert.Single(overlay.GetLogicalDescendants().OfType<Border>()));
            Assert.Empty(overlay.GetLogicalDescendants().OfType<Shape>());
            Assert.Empty(overlay.GetLogicalDescendants().OfType<Button>());
            Assert.NotNull(presenter.Transitions);

            vm.IsCropMode = true;
            vm.IsWhiteBalancePicking = true;
            vm.AssessmentFeedback = "Set rating: ★★★";
            vm.IsAssessmentFeedbackVisible = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(content.IsVisible);
            Assert.Contains("visible", presenter.Classes);

            vm.IsAssessmentFeedbackVisible = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(content.IsVisible);
            Assert.DoesNotContain("visible", presenter.Classes);

            vm.AssessmentFeedback = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(content.IsVisible);
        });
    }

    private static async Task WithWindowAsync(
        Action<MainWindow, MainWindowViewModel> assertion)
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-develop-overlay-{Guid.NewGuid():N}")).FullName;
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await using var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.SelectedImage = new ImageFile(Path.Combine(root, "photo.jpg"));
        vm.IsDevelopMode = true;
        vm.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = vm };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            assertion(window, vm);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }
}
