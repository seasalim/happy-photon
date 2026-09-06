using System.Diagnostics;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LocalsShowcaseTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData("develop-locals-empty", false, false, false)]
    [InlineData("develop-locals-linear-selected", true, false, false)]
    [InlineData("develop-locals-mask-overlay", true, true, false)]
    [InlineData("develop-locals-exited", true, false, true)]
    [InlineData("develop-locals-locked", true, false, false)]
    [InlineData("develop-locals-geometry", true, false, false)]
    public async Task RenderScene(string scene, bool hasLocal, bool mask, bool closed)
    {
        using var fixture = new CatalogVmFixture("locals-shots");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("srgb-reference.jpg");
        Assert.Equal(0, (int)File.GetAttributes(path) & (0x1000 | 0x40000 | 0x400000));
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(path)
        {
            EditSettings = new EditSettings { Locals = hasLocal ? [new() { Exposure = -1 }] : null }
        };
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.ShowLocalMask = mask;
        vm.IsLocalGeometryExpanded = scene == "develop-locals-geometry";
        if (closed) vm.CloseLocalsCommand.Execute(null);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1200, 700), ThemeVariant.Dark,
            window =>
            {
                window.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>()
                    .Single(control => control.Name == "DevelopControlsScrollViewer").Offset = default;
                var overlay = window.GetVisualDescendants().OfType<LocalsOverlayControl>().Single();
                Assert.Equal(!closed, overlay.IsVisible);
                if (!closed) Assert.True(overlay.Bounds.Width > 0 && overlay.Bounds.Height > 0);
            });
    }

    [AvaloniaFact]
    public void MaskBrushMeetsWeightAndBuildGates()
    {
        var frame = new LocalsFrame(1600, 1067, 0, 0, 1, 1);
        var local = new LocalAdjustment { Angle = 30 };
        var size = new Size(800, 533.5);
        var brush = LocalsOverlayControl.BuildMaskBrush(local, frame, size);
        var maxError = 0d;
        for (var i = 0; i <= 10000; i++)
        {
            var t = i / 10000d;
            var index = Math.Min(255, (int)(t * 256));
            var a = brush.GradientStops[index];
            var b = brush.GradientStops[index + 1];
            var alpha = (a.Color.A + (b.Color.A - a.Color.A) * (t * 256 - index)) / 255d;
            maxError = Math.Max(maxError, Math.Abs(alpha - (1 - t * t * (3 - 2 * t))));
        }
        Assert.InRange(maxError, 0, .002);
        var times = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            var start = Stopwatch.GetTimestamp();
            LocalsOverlayControl.BuildMaskBrush(local, frame, size);
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        var median = times.Order().ElementAt(10);
        output.WriteLine($"G10 median={median:F4} ms; pixel reads=0; weight error={maxError:F6}");
        Assert.True(median <= 5);
    }
}
