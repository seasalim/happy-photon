using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class CompactSliderEntryTests
{
    [AvaloniaTheory]
    [InlineData("drag", false)]
    [InlineData("drag", true)]
    [InlineData("wheel", false)]
    [InlineData("wheel", true)]
    [InlineData("entry", false)]
    [InlineData("entry", true)]
    public async Task CropHorizonPreviewsDraftAndOnlyApplyCommits(string input, bool apply)
    {
        var original = new EditSettings
        {
            HorizonRotation = -.5,
            Crop = new CropRegion { Left = .1, Top = .1, Right = .9, Bottom = .9 }
        };
        await using var s = await Session.Create(original.Clone());
        await s.Vm.ToggleCropModeCommand.ExecuteAsync(null);
        await s.Drain();
        var image = s.Vm.SelectedImage!;
        var slider = s.Slider("Horizon");
        s.Reveal(slider);
        var point = slider.TranslatePoint(new Point(100, 10), s.Window)!.Value;
        var previewBefore = s.Vm.PreviewImage;
        var ends = 0;
        slider.DragCompleted += (_, _) => ends++;

        switch (input)
        {
            case "drag":
                s.Window.MouseDown(point, MouseButton.Left);
                s.Window.MouseMove(point + new Vector(12, 0), RawInputModifiers.LeftMouseButton);
                break;

            case "wheel":
                slider.WheelTimeProvider = new TestTimeProvider();
                s.Window.MouseMove(point);
                s.Window.MouseWheel(point, new Vector(0, 12), RawInputModifiers.Shift);
                break;

            case "entry":
                s.Open(slider);
                s.Window.KeyTextInput("1.2°");
                s.Key(Key.Enter);
                break;
        }

        var draftHorizon = slider.Value;
        Assert.NotEqual(original.HorizonRotation, draftHorizon);
        var draftPreview = original.Clone();
        draftPreview.HorizonRotation = draftHorizon;
        draftPreview.Crop = new CropRegion();
        await s.Drain();
        Assert.NotSame(previewBefore, s.Vm.PreviewImage);
        AssertPreview(draftPreview);
        Assert.Equal(input == "entry" ? 1 : 0, ends);
        Assert.Equal(0, s.Steps);

        if (input == "drag")
        {
            s.Window.MouseUp(point + new Vector(12, 0), MouseButton.Left);
        }
        else if (input == "wheel")
        {
            s.Window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.None, null);
        }

        await s.Drain();
        Assert.Equal(1, ends);
        AssertPreview(draftPreview);
        Assert.True(s.Vm.IsCropMode);
        Assert.Equal(draftHorizon, s.Vm.HorizonRotation);
        Assert.Equal(RenderSettingsHash.Compute(original), RenderSettingsHash.Compute(image.EditSettings));
        Assert.Equal(0, s.Steps);
        var beforeApply = await s.Catalog.LoadEditHistoryAsync(image.CatalogId);
        Assert.DoesNotContain(beforeApply.Entries, entry => entry.Label != "Original");

        var buttonName = apply ? "ApplyCropButton" : "CancelCropButton";
        var button = s.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == buttonName);
        s.Click(s.Center(button));
        var command = apply ? s.Vm.ApplyCropCommand : s.Vm.CancelCropCommand;
        await command.ExecutionTask!.WaitAsync(TestWaits.Condition);
        await s.Drain();

        var expected = original.Clone();
        if (apply) expected.HorizonRotation = draftHorizon;

        Assert.False(s.Vm.IsCropMode);
        Assert.Equal(expected.HorizonRotation, s.Vm.HorizonRotation);
        Assert.Equal(RenderSettingsHash.Compute(expected), RenderSettingsHash.Compute(image.EditSettings));
        AssertPreview(expected);
        Assert.Equal(apply ? 1 : 0, s.Steps);
        var saved = await s.Catalog.LoadEditHistoryAsync(image.CatalogId);
        Assert.Equal(apply ? 1 : 0, saved.Entries.Count(entry => entry.Label != "Original"));

        void AssertPreview(EditSettings settings)
        {
            Assert.NotNull(s.Vm.PreviewImage);
            var identity = s.Vm.ImageService.Previews.TryGetPreviewRenderIdentity(s.Vm.PreviewImage);
            Assert.NotNull(identity);
            Assert.Equal(RenderSettingsHash.Compute(settings), identity.SettingsHash);
        }
    }
}
