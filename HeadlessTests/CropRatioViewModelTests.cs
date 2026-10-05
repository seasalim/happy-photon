using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CropRatioViewModelTests
{
    [AvaloniaTheory]
    [InlineData(1, Key.None)]
    [InlineData(1.004, Key.None)]
    [InlineData(1.004, Key.Enter)]
    [InlineData(1.004, Key.Space)]
    public async Task ExplicitReselectionLocksAndRefitsDisplayedRatio(double drift, Key activation)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "ratio.jpg");
        using (var source = new MagickImage(MagickColors.Gray, 400, 300)) source.Write(path);

        await DevelopToolsBaselineTests.WithScene("crop", 1200, 700, async (vm, _) =>
        {
            var section = new CropEditSection { DataContext = vm };
            using var scope = new TestUiScope(new Window { Width = 260, Height = 320, Content = section });
            Dispatcher.UIThread.RunJobs();
            var picker = section.FindControl<ComboBox>("CropRatioPicker")!;
            vm.IsCropAspectLocked = false;
            vm.CurrentCrop = CropGeometry.Fit(new CropRegion { Left = .1, Top = .1, Right = .9, Bottom = .9 },
                1.5 * drift / (4d / 3));
            var draft = vm.CurrentCrop;
            Assert.Equal("3:2", picker.SelectedItem);
            Assert.False(vm.IsCropAspectLocked);

            ClickRatio(picker, "3:2", activation);

            Assert.True(vm.IsCropAspectLocked);
            Assert.Equal("3:2", picker.SelectedItem);
            Assert.Equal(1.5, CropGeometry.DraftPixelRatio(vm.CurrentCrop!, 4d / 3), 12);
            Assert.Equal(draft!.Left + draft.Right, vm.CurrentCrop!.Left + vm.CurrentCrop.Right, 12);
            Assert.Equal(draft.Top + draft.Bottom, vm.CurrentCrop.Top + vm.CurrentCrop.Bottom, 12);
            vm.IsCropAspectLocked = false;
            draft = vm.CurrentCrop;
            ClickRatio(picker, "Custom");
            Assert.False(vm.IsCropAspectLocked);
            Assert.Same(draft, vm.CurrentCrop);
            Assert.Equal("3:2", picker.SelectedItem);
            ClickRatio(picker, "1:1");
            Assert.True(vm.IsCropAspectLocked);
            Assert.Equal("1:1", picker.SelectedItem);
            section.DataContext = null;
            await Task.CompletedTask;
        }, path);
    }

    private static void ClickRatio(ComboBox picker, string ratio, Key activation = Key.None)
    {
        Click(picker);
        Dispatcher.UIThread.RunJobs();
        Assert.True(picker.IsDropDownOpen);

        picker.ScrollIntoView(ratio);
        Dispatcher.UIThread.RunJobs();
        var item = Assert.IsType<ComboBoxItem>(picker.ContainerFromIndex(picker.Items.IndexOf(ratio)));

        if (activation == Key.None)
        {
            Click(item);
        }
        else
        {
            item.Focus();
            var root = TopLevel.GetTopLevel(item)!;
            root.KeyPress(activation, RawInputModifiers.None, PhysicalKey.None, null);
            root.KeyRelease(activation, RawInputModifiers.None, PhysicalKey.None, null);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.False(picker.IsDropDownOpen);
    }

    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        root.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;

        var hit = root.InputHitTest(point);
        Assert.True(ReferenceEquals(hit, control) || hit is Visual visual && visual.GetVisualAncestors().Contains(control),
            $"Expected {control} at {point}, hit {hit}; bounds {control.Bounds}");

        root.MouseMove(point);
        root.MouseDown(point, MouseButton.Left);
        root.MouseUp(point, MouseButton.Left);
    }

    [AvaloniaTheory]
    [InlineData(6000, 1000)]
    [InlineData(1000, 6000)]
    public async Task PanoramaRejectsInfeasibleOriginalChoiceAndSwap(int width, int height)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "panorama.jpg");
        using (var source = new MagickImage(MagickColors.Gray, (uint)width, (uint)height)) source.Write(path);

        await DevelopToolsBaselineTests.WithScene("crop", 1200, 700, async (vm, scope) =>
        {
            // test-teardown-policy: allow - WithScene owns this MainWindow scope.
            scope.Show();
            scope.Window!.Focus();
            Assert.Equal("Original", vm.CropRatio);
            Assert.False(vm.CanSwapCropRatio);
            Assert.False(vm.SwapCropRatioCommand.CanExecute(null));
            var draft = vm.CurrentCrop;
            var flag = vm.SelectedImage!.Flag;
            vm.SwapCropRatioCommand.Execute(null);
            scope.Window.KeyPress(Key.X, RawInputModifiers.None, PhysicalKey.X, "x");
            scope.Window.KeyRelease(Key.X, RawInputModifiers.None, PhysicalKey.X, "x");
            Assert.Same(draft, vm.CurrentCrop);
            Assert.Equal(flag, vm.SelectedImage.Flag);

            vm.ChooseCropRatio("3:2");
            Assert.True(vm.CanSwapCropRatio);
            vm.SwapCropRatioCommand.Execute(null);

            draft = vm.CurrentCrop;
            vm.IsCropAspectLocked = false;
            vm.ChooseCropRatio("Original");
            Assert.Same(draft, vm.CurrentCrop);
            Assert.False(vm.IsCropAspectLocked);
            await Task.CompletedTask;
        }, path);
    }

    [AvaloniaFact]
    public async Task FrameCacheFollowsGeometryAndIdentityButNotDraftChanges()
    {
        await DevelopToolsBaselineTests.WithScene("crop", 1200, 700, async (vm, scope) =>
        {
            var preview = vm.PreviewImage!;
            var identity = vm.ImageService.Previews.TryGetPreviewRenderIdentity(preview)!;
            var frameNotifications = 0;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.CropFrameSize)) frameNotifications++;
            };
            var original = vm.CropFrameSize;

            for (var i = 0; i < 20; i++)
            {
                vm.CurrentCrop = new CropRegion { Left = i * .001 };
                _ = vm.CropRatio;
                _ = vm.CanSwapCropRatio;
                Assert.Equal(original, vm.CropFrameSize);
            }

            Assert.Equal(0, frameNotifications);
            var changes = new Action[]
            {
                () => vm.Rotation = 90,
                () => vm.HorizonRotation = 2,
                () => vm.GeometryVertical = 10,
                () => vm.GeometryHorizontal = -10,
                () => vm.GeometryAspect = 10,
                () => vm.GeometryDistortion = 10
            };

            foreach (var change in changes)
            {
                var before = frameNotifications;
                change();
                Assert.Equal(before + 1, frameNotifications);
                var expected = RenderGeometry.CalculateOriginalViewSize(identity.OriginalImageSize.Width,
                    identity.OriginalImageSize.Height, new EditSettings
                    {
                        Rotation = vm.Rotation, HorizonRotation = vm.HorizonRotation,
                        Geometry = new GeometrySettings
                        {
                            Vertical = vm.GeometryVertical, Horizontal = vm.GeometryHorizontal,
                            Aspect = vm.GeometryAspect, Distortion = vm.GeometryDistortion
                        }
                    });
                Assert.Equal(expected, vm.CropFrameSize);
            }

            var corrected = vm.CropFrameSize;
            vm.PreviewImage = null;
            Assert.Equal(default, vm.CropFrameSize);
            vm.PreviewImage = preview;
            Assert.Equal(corrected, vm.CropFrameSize);
            vm.SelectedImage = null;
            Assert.Equal(default, vm.CropFrameSize);
            Assert.False(vm.CanChooseCropRatio);
            await Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task PickerLockResetAndCancelFollowDraft()
    {
        await DevelopToolsBaselineTests.WithScene("crop", 1200, 700, async (vm, scope) =>
        {
            // test-teardown-policy: allow - WithScene owns this MainWindow scope.
            scope.Show();
            Dispatcher.UIThread.RunJobs();
            var picker = scope.Window!.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "CropRatioPicker");
            Assert.Equal("Original", vm.CropRatio);
            vm.IsCropAspectLocked = false;
            Assert.Equal("Original", vm.CropRatio);
            picker.SelectedItem = "1:1";
            Assert.True(vm.IsCropAspectLocked);
            Assert.Equal("1:1", vm.CropRatio);
            Assert.False(vm.CanSwapCropRatio);
            vm.IsCropAspectLocked = false;
            Assert.Equal("1:1", vm.CropRatio);
            var square = vm.CurrentCrop;
            picker.SelectedItem = "Custom";
            Assert.Same(square, vm.CurrentCrop);
            Assert.Equal("1:1", picker.SelectedItem);
            vm.CurrentCrop = new CropRegion { Left = .1, Right = .71, Top = .2, Bottom = .9 };
            Assert.Equal("Custom", vm.CropRatio);
            Assert.Equal("Custom", picker.SelectedItem);
            Assert.False(vm.CanSwapCropRatio);
            vm.ResetCropCommand.Execute(null);
            Assert.True(vm.CurrentCrop!.IsFullImage);
            Assert.Equal("Original", picker.SelectedItem);
            vm.ChooseCropRatio("1:1");
            await vm.ApplyCropCommand.ExecuteAsync(null);
            await vm.ToggleCropModeCommand.ExecuteAsync(null);
            Assert.Equal("1:1", vm.CropRatio);
            vm.ChooseCropRatio("16:9");
            await vm.CancelCropCommand.ExecuteAsync(null);
            await vm.ToggleCropModeCommand.ExecuteAsync(null);
            Assert.Equal("1:1", vm.CropRatio);
        });
    }

    [AvaloniaTheory]
    [InlineData("3:2")]
    [InlineData("Original")]
    [InlineData("1:1")]
    [InlineData("Custom")]
    public async Task RealXSwapsOnlyInCropAndNeverRejectsDisabledSwap(string ratio)
    {
        await DevelopToolsBaselineTests.WithScene("crop", 1200, 700, async (vm, scope) =>
        {
            // test-teardown-policy: allow - WithScene owns this MainWindow scope.
            scope.Show();
            var window = scope.Window!;
            window.Focus();
            vm.ChooseCropRatio(ratio);

            if (ratio == "Custom")
            {
                vm.CurrentCrop = new CropRegion { Left = .1, Right = .71, Top = .2, Bottom = .9 };
            }

            var draft = vm.CurrentCrop!;
            var frameRatio = vm.CropFrameSize.Width / (double)vm.CropFrameSize.Height;
            var before = CropGeometry.DraftPixelRatio(draft, frameRatio);
            var flag = vm.SelectedImage!.Flag;
            window.KeyPress(Key.X, RawInputModifiers.None, PhysicalKey.X, "x");
            window.KeyRelease(Key.X, RawInputModifiers.None, PhysicalKey.X, "x");
            Assert.Equal(flag, vm.SelectedImage.Flag);
            Assert.Equal(ratio is "Custom" or "1:1" ? before : 1 / before,
                CropGeometry.DraftPixelRatio(vm.CurrentCrop!, frameRatio), 12);

            if (ratio is "Custom" or "1:1") Assert.Same(draft, vm.CurrentCrop);

            await vm.CancelCropCommand.ExecuteAsync(null);
            window.Focus();
            window.KeyPress(Key.X, RawInputModifiers.None, PhysicalKey.X, "x");
            window.KeyRelease(Key.X, RawInputModifiers.None, PhysicalKey.X, "x");
            Assert.Equal(ImageFlag.Rejected, vm.SelectedImage.Flag);
        });
    }

    [AvaloniaFact]
    public async Task MissingFrameDisablesPickerAndSquareOriginalSwapDoesNothing()
    {
        await DevelopToolsBaselineTests.WithScene("crop", 1200, 700, async (vm, _) =>
        {
            vm.PreviewImage = null;
            Assert.False(vm.CanChooseCropRatio);
            Assert.False(vm.SwapCropRatioCommand.CanExecute(null));
            var draft = vm.CurrentCrop;
            vm.ChooseCropRatio("3:2");
            Assert.Same(draft, vm.CurrentCrop);
            await Task.CompletedTask;
        });
        var square = new CropRegion();
        var swapped = CropGeometry.Swap(square, 1);
        Assert.True(swapped.IsFullImage);
    }
}
