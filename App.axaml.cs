using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;

namespace HappyPhoton;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var catalogService = new CatalogService();
            var viewModel = new MainWindowViewModel(catalogService);
            var window = new MainWindow
            {
                DataContext = viewModel,
            };

            desktop.MainWindow = window;
            window.Show();

            Dispatcher.UIThread.Post(
                () => _ = CompleteStartupAsync(
                    window,
                    viewModel,
                    catalogService),
                DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task CompleteStartupAsync(
        MainWindow window,
        MainWindowViewModel viewModel,
        CatalogService catalogService)
    {
        var locationService = new AppDataLocationService();
        var locationMigrator = new CatalogLocationMigrator(locationService);
        var picturesPath = viewModel.GetAvailablePicturesPath();
        await window.InitializeApplicationAsync(
            viewModel,
            catalogService,
            locationService,
            locationMigrator,
            picturesPath);
    }
}
