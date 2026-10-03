using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

internal static class ControlBarGateScene
{
    internal static async Task WithScene(Func<MainWindow, MainWindowViewModel, Task> measure)
    {
        using var files = new CatalogVmFixture("wp12");
        using var catalog = await files.CreateCatalogAsync();
        await using var vm = files.CreateViewModel(catalog, new StandardBaseLoader(), _ => Task.CompletedTask);
        var images = new List<ImageFile>();

        foreach (var name in new[] { "first.jpg", "second.jpg", "third.jpg" })
        {
            var path = files.Path(name);
            TestImages.WriteJpeg(path, width: 640, height: 400);
            var image = new ImageFile(path) { Thumbnail = new Bitmap(path) };
            image.CatalogId = await catalog.GetOrCreateImageAsync(path);
            images.Add(image);
        }

        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[0];
        vm.SelectedImage.Flag = ImageFlag.Rejected;
        vm.IsDevelopMode = true;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        var window = new MainWindow { Width = 1900, Height = 900 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Settle(window);
        await measure(window, vm);
    }

    internal static void Mode(MainWindow window, MainWindowViewModel vm, string view)
    {
        vm.IsDevelopMode = view == "develop";

        if (view != "develop" && vm.IsLoupeMode != (view == "browse-loupe"))
        {
            vm.ToggleLoupeCommand.Execute(null);
        }

        Settle(window);
    }

    internal static void ViewerWidth(MainWindow window, Control bar, double width)
    {
        window.MinWidth = 0;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            window.Width += width - bar.Bounds.Width;
            Settle(window);
        }

        Assert.Equal(width, bar.Bounds.Width, precision: 3);
    }

    internal static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    internal static T Named<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().Prepend(root).OfType<T>().Single(control => control.Name == name);

    internal static Border Bar(MainWindow window, bool develop) => develop
        ? Named<Border>(Named<DevelopViewerPane>(window, "DevelopViewerPane"), "DevelopControlBar")
        : Named<Border>(Named<BrowseGridView>(window, "BrowseGridView"), "BrowseFooterSurface");

    internal static ImageAssessmentControl Full(Control bar) =>
        bar.GetVisualDescendants().OfType<ImageAssessmentControl>().Single();

    internal static Rect Bounds(Control control, Control parent) =>
        new(control.TranslatePoint(default, parent)!.Value, control.Bounds.Size);

    internal static double Offset(Control cluster, Control bar)
    {
        var bounds = Bounds(cluster, bar);

        return Math.Abs(bounds.Center.X - bar.Bounds.Width / 2);
    }
}
