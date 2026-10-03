using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

internal sealed class TipsTestScene : IAsyncDisposable
{
    private readonly CatalogVmFixture _files = new("tips");

    private readonly TestUiScope _scope;

    public CatalogService Catalog { get; }

    public MainWindowViewModel Vm { get; }

    public MainWindow Window { get; }

    public TipsTestScene(int width = 1200, int height = 700, bool applySettings = true)
    {
        Catalog = _files.CreateCatalog();
        Vm = new MainWindowViewModel(Catalog);
        // test-teardown-policy: allow - ForMainWindow owns binding; DisposeAsync releases the scope before the VM.
        Window = new MainWindow { Width = width, Height = height };
        _scope = TestUiScope.ForMainWindow(Window, Vm, afterShow: () =>
        {
            if (applySettings)
            {
                Vm.RestoreTipsSettings(new AppSettings());
            }

            Vm.ShowWorkspaceReady(1);
            Dispatcher.UIThread.RunJobs();
        });
    }

    public static Control Card(Control root, WorkspaceMode mode)
    {
        Dispatcher.UIThread.RunJobs();
        var card = root.GetVisualDescendants().OfType<Control>()
            .SingleOrDefault(control => AutomationProperties.GetName(control) == $"{mode} tips");
        Assert.True(card != null, $"{mode} tips card not found");

        return card!;
    }

    public static Button Action(Control card, string text) =>
        card.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, text));

    public static void Click(Button button)
    {
        var window = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(button));
        window.UpdateLayout();
        var point = button.TranslatePoint(new Avalonia.Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    public void Capture(string name, ThemeVariant theme) =>
        ShowcaseTestHelper.Capture(name, _scope,
            new PixelSize((int)Window.Width, (int)Window.Height), theme);

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await Vm.DisposeAsync();
        Catalog.Dispose();
        _files.Dispose();
    }
}
