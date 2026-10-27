using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopEditDotBaselineTests
{
    internal static EditSettings AllGroupsEdited()
    {
        var settings = SyncTransferParityCorpus.CreateLook();
        settings.Texture = 12;
        settings.Clarity = 8;
        settings.Geometry = new GeometrySettings { Vertical = 17 };
        settings.RawProfile = new RawProfileSelection
        {
            Source = RawProfileSource.Embedded,
            ContentHash = new string('a', 64)
        };

        return settings;
    }

    internal static Task WithScene(EditSettings settings, Func<MainWindowViewModel, Window, Task> run) =>
        WithSceneScope(settings, (vm, scope) => run(vm, scope.Window!));

    internal static async Task WithSceneScope(EditSettings settings, Func<MainWindowViewModel, TestUiScope, Task> run)
    {
        using var fixture = new CatalogVmFixture("develop-dot-baseline");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("srgb-reference.jpg");
        Assert.Equal(0, (int)File.GetAttributes(path) & (0x1000 | 0x40000 | 0x400000));
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var expected = settings.Clone();
        var image = new ImageFile(path) { EditSettings = settings };
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(image.CatalogId, settings);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);

        if (vm.PendingPreviewDebounceTask is { } pending)
        {
            await pending.WaitAsync(TestWaits.Condition);
        }

        await TestWaits.UntilAsync(() => vm.CaptureBackgroundActivitySnapshot().PreviewCount == 0
            && !vm.IsBackgroundActivityStatusVisible);
        using var theme = new TestUiScope(theme: ThemeVariant.Dark);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        scope.Show();
        DevelopCollapseBaselineTests.Settle(window);
        Assert.True(image.EditSettings.HasSameEdits(expected));
        await run(vm, scope);
    }
}
