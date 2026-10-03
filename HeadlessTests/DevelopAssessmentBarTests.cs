using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
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

public sealed partial class DevelopAssessmentBarTests
{
    [AvaloniaFact]
    public async Task AllStatesMatchFooterAndCompactCluster()
    {
        await WithWindow(async (window, vm, other) =>
        {
            var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
            var full = pane.FindControl<ImageAssessmentControl>("DevelopImageAssessment")!;
            var footer = window.FindControl<BrowseGridView>("BrowseGridView")!
                .FindControl<ImageAssessmentControl>("ImageAssessment")!;

            foreach (var flag in Enum.GetValues<ImageFlag>())
            foreach (var rating in Enumerable.Range(0, 6))
            foreach (var label in Enum.GetValues<ColorLabel>())
            {
                vm.SelectedImage!.Flag = flag;
                vm.SelectedImage.Rating = rating;
                vm.SelectedImage.ColorLabel = label;
                Settle(window);
                AssertState(pane, footer, vm.SelectedImage);
            }

            Assert.Empty(full.Classes);

            vm.SelectedImage = other;
            Settle(window);
            AssertState(pane, footer, other);

            await Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task ShortcutsClicksUndoAndPhotoChangesKeepActiveState()
    {
        await WithWindow(async (window, vm, other) =>
        {
            var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
            var full = pane.FindControl<ImageAssessmentControl>("DevelopImageAssessment")!;
            var footer = window.FindControl<BrowseGridView>("BrowseGridView")!
                .FindControl<ImageAssessmentControl>("ImageAssessment")!;
            var active = vm.SelectedImage!;

            vm.Browse.ToggleSelection(other);
            pane.Focus();
            Press(window, Key.P);
            await TestWaits.UntilAsync(() => !vm.TogglePickedImageCommand.IsRunning);
            Assert.Equal(ImageFlag.Picked, active.Flag);
            AssertState(pane, footer, active);

            foreach (var (name, flag, rating) in new[]
                     {
                         ("RejectImageButton", ImageFlag.Rejected, 0),
                         ("UnflagImageButton", ImageFlag.Unflagged, 0),
                         ("PickImageButton", ImageFlag.Picked, 0),
                         ("Rating5Button", ImageFlag.Picked, 5)
                     })
            {
                await Click(window, full.FindControl<Button>(name)!);
                Assert.Equal(flag, active.Flag);
                Assert.Equal(rating, active.Rating);
                AssertState(pane, footer, active);
                Assert.Equal(ImageFlag.Unflagged, other.Flag);
                Assert.Equal(0, other.Rating);
            }

            var swatch = full.GetLogicalDescendants().OfType<Button>()
                .Single(button => Equals(button.CommandParameter, ColorLabel.Blue));

            await Click(window, swatch);
            Assert.Equal(ColorLabel.Blue, active.ColorLabel);
            Assert.Equal(ColorLabel.None, other.ColorLabel);
            AssertState(pane, footer, active);
            Assert.Same(pane, window.FocusManager!.GetFocusedElement());

            var fit = vm.IsZoomFitMode;
            Press(window, Key.Space);
            Assert.Equal(!fit, vm.IsZoomFitMode);
            Press(window, Key.Z);
            Assert.Equal(fit, vm.IsZoomFitMode);

            Press(window, Key.Right);
            Assert.Same(other, vm.SelectedImage);
            Settle(window);
            AssertState(pane, footer, other);
        });

        await DevelopToolsBaselineTests.WithScene("normal", 1600, 900, async (vm, scope) =>
        {
            vm.IsDevelopMode = false;

            // test-teardown-policy: allow - WithScene owns the supplied MainWindow scope.
            scope.Show();
            var window = (MainWindow)scope.Window!;
            Settle(window);

            vm.IsDevelopMode = true;
            await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
            Settle(window);

            var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
            var footer = window.FindControl<BrowseGridView>("BrowseGridView")!
                .FindControl<ImageAssessmentControl>("ImageAssessment")!;

            await vm.SetRatingCommand.ExecuteAsync(3);
            var rotation = vm.Rotation;
            vm.RotateLeftCommand.Execute(null);
            await TestWaits.UntilAsync(() => vm.CanUndo);

            if (vm.PendingHistoryCommitTask is { } pending)
            {
                await pending.WaitAsync(TestWaits.Condition);
            }

            Assert.NotEqual(rotation, vm.Rotation);
            await vm.UndoCommand.ExecuteAsync(null);
            Assert.Equal(rotation, vm.Rotation);
            Settle(window);
            AssertState(pane, footer, vm.SelectedImage!);
        });
    }

    private static async Task Click(MainWindow window, Button button)
    {
        // Layout changes must reach the compositor before pointer hit-testing.
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        var hit = window.InputHitTest(point) as Visual;
        Assert.True(ReferenceEquals(hit, button) || hit?.GetVisualAncestors().Contains(button) == true,
            $"Expected to click {button.Name} at {point}; hit {hit}.");
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        if (button.Command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand command)
        {
            await TestWaits.UntilAsync(() => !command.IsRunning);
        }

        Settle(window);
    }

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
    }

    private static void AssertState(DevelopViewerPane pane, ImageAssessmentControl footer, ImageFile image)
    {
        var full = pane.FindControl<ImageAssessmentControl>("DevelopImageAssessment")!;

        foreach (var control in new[] { full, footer })
        {
            Assert.Equal(image.IsPicked, control.FindControl<Button>("PickImageButton")!.Classes.Contains("active"));
            Assert.Equal(image.IsRejected, control.FindControl<Button>("RejectImageButton")!.Classes.Contains("active"));

            foreach (var rating in Enumerable.Range(1, 5))
            {
                var star = control.FindControl<Button>($"Rating{rating}Button")!;
                Assert.Equal(image.Rating >= rating, Assert.IsType<Grid>(star.Content).Children[1].IsVisible);
            }

            var swatches = control.GetLogicalDescendants().OfType<Button>()
                .Where(button => button.CommandParameter is ColorLabel).ToArray();
            Assert.Equal(5, swatches.Length);

            foreach (var button in swatches)
            {
                var border = Assert.IsType<Border>(button.Content);
                var expected = Equals(image.ColorLabel, button.CommandParameter)
                    ? HappyPhotonColors.ControlActive : Brushes.Transparent;
                Assert.Equal(expected, border.BorderBrush);
            }
        }

        var cluster = pane.FindControl<DevelopAssessmentCluster>("DevelopCompactAssessment")!;
        Assert.Equal(image.IsPicked, cluster.FindControl<Avalonia.Controls.Shapes.Path>("CompactPickedFlag")!.IsVisible);
        Assert.Equal(image.IsRejected, cluster.FindControl<TextBlock>("CompactRejectedFlag")!.IsVisible);
        Assert.Equal(image.RatingStars, cluster.FindControl<TextBlock>("CompactRating")!.Text);

        var label = cluster.FindControl<Border>("CompactColorLabel")!;
        Assert.Equal(image.HasColorLabel, label.IsVisible);
        Assert.Equal(HappyPhotonColors.GetColorLabelBrush(image.ColorLabel), label.Background);
        Assert.Equal(image.ColorLabel, ToolTip.GetTip(label));
        Assert.Empty(cluster.GetLogicalDescendants().OfType<Button>());
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WithWindow(Func<MainWindow, MainWindowViewModel, ImageFile, Task> assertion)
    {
        using var fixture = new CatalogVmFixture("develop-assessment-bar");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, loadMetadataAsync: _ => Task.CompletedTask);

        var first = new ImageFile(fixture.Path("first.jpg"));
        var other = new ImageFile(fixture.Path("other.jpg"));
        first.CatalogId = await catalog.GetOrCreateImageAsync(first.FilePath);
        other.CatalogId = await catalog.GetOrCreateImageAsync(other.FilePath);

        vm.Browse.SetImages([first, other]);
        vm.SelectedImage = first;
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);

        var window = new MainWindow { Width = 1800, Height = 900 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Settle(window);

        vm.IsDevelopMode = true;
        Settle(window);

        await assertion(window, vm, other);
    }
}
