using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ColorAssessmentScreenshotTests
{
    [AvaloniaFact]
    public async Task DevelopPair_RendersInBothThemes()
    {
        var application = Application.Current!;
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-assessment-shot-{Guid.NewGuid():N}")).FullName;
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await using var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        using var bitmap = new Bitmap(Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "srgb-reference.jpg"));
        var image = new ImageFile(Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "srgb-reference.jpg"));
        vm.Library.SetImages([image]);
        vm.SelectedImage = image;
        vm.IsDevelopMode = true;
        vm.PreviewImage = bitmap;
        vm.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow
        {
            Width = 1200,
            Height = 700,
            DataContext = vm
        };

        try
        {
            application.RequestedThemeVariant = ThemeVariant.Dark;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            vm.PreviewImage = bitmap;
            Capture(window, "Screenshot_Develop_Assessment_Dark_Off.png");

            vm.ToggleColorAssessmentModeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Capture(window, "Screenshot_Develop_Assessment_Dark_On.png");

            vm.ToggleColorAssessmentModeCommand.Execute(null);
            vm.TransientStatus = null;
            application.RequestedThemeVariant = HappyPhotonThemes.MidGrey;
            Dispatcher.UIThread.RunJobs();
            Capture(window, "Screenshot_Develop_Assessment_MidGrey_Off.png");

            vm.ToggleColorAssessmentModeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Capture(window, "Screenshot_Develop_Assessment_MidGrey_On.png");
        }
        finally
        {
            application.RequestedThemeVariant = ThemeVariant.Dark;
            vm.PreviewImage = null;
            window.DataContext = null;
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Capture(Window window, string fileName)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width > 0);
        Assert.True(frame.PixelSize.Height > 0);
        if (Environment.GetEnvironmentVariable(
                "HAPPY_PHOTON_UPDATE_SCREENSHOTS") != "1")
        {
            return;
        }

        frame.Save(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "docs",
            "screenshots",
            fileName));
    }
}
