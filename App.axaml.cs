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
    public static ICatalogService? CatalogService { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            CatalogService = new CatalogService();
            var viewModel = new MainWindowViewModel(CatalogService);
            var window = new MainWindow
            {
                DataContext = viewModel,
            };

            desktop.MainWindow = window;
            window.Show();

            Dispatcher.UIThread.Post(
                () => _ = CompleteStartupAsync(window, viewModel, CatalogService),
                DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task CompleteStartupAsync(
        MainWindow window,
        MainWindowViewModel viewModel,
        ICatalogService catalogService)
    {
        try
        {
            await Task.WhenAll(
                Task.Run(catalogService.InitializeAsync),
                Task.Run(viewModel.InitializeAsync));
            await window.RestoreSessionAsync(viewModel);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Startup initialization failed: {exception}");
        }
    }
}
