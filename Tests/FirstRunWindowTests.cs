using Avalonia.Controls;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class FirstRunWindowTests
{
    private readonly AvaloniaTestFixture _fixture;

    public FirstRunWindowTests(AvaloniaTestFixture fixture)
    {
        _fixture = fixture;
    }

    [WindowsFact]
    public async Task StartupGate_SuspendsAndRestoresWorkspaceKeyBindings()
    {
        _fixture.RequireWindows();
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-window-{Guid.NewGuid():N}"));
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow
        {
            DataContext = vm
        };

        Assert.False(window.WorkspaceKeyboardEnabled);
        Assert.Empty(window.KeyBindings);
        Assert.False(
            window.FindControl<FolderTreePanel>("FolderTreePanel")!.IsEffectivelyEnabled);

        vm.ShowWorkspaceReady(1);

        Assert.True(window.WorkspaceKeyboardEnabled);
        Assert.NotEmpty(window.KeyBindings);
        Assert.True(
            window.FindControl<FolderTreePanel>("FolderTreePanel")!.IsEffectivelyEnabled);

        window.DataContext = null;
        window.Close();
        await vm.DisposeAsync();
    }

}
