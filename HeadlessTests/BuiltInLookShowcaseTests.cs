using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BuiltInLookShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("presets-builtin-groups", false, false)]
    [InlineData("presets-builtin-groups-midgray", false, true)]
    [InlineData("presets-collapsed", true, false)]
    public async Task PresetGroupsRenderShowcase(string scene, bool collapsed, bool midGray)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var asset = GoldenTestPaths.Asset("srgb-reference.jpg");
        var bitmap = new Bitmap(asset);
        var image = new ImageFile(asset);
        image.CatalogId = await catalog.GetOrCreateImageAsync(asset);
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        vm.IsDevelopMode = true;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);
        vm.PreviewImage = bitmap;
        vm.RestorePresetGroups(new Dictionary<string, bool>
        {
            ["My Presets"] = !collapsed, ["Natural"] = !collapsed, ["Portrait"] = false,
            ["Landscape"] = false, ["Black & White"] = false, ["Creative"] = false
        });
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1200, 700),
            midGray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, staged =>
        {
            var panel = staged.FindControl<PresetsPanel>("PresetsPanel")!;
            var expanders = panel.GetVisualDescendants().OfType<Expander>().ToArray();
            Assert.Equal(6, expanders.Length);
            Assert.Equal(collapsed ? 0 : 2, expanders.Count(expander => expander.IsExpanded));
            ShowcaseTestHelper.SettleExpanderChevrons(panel);

            if (collapsed)
            {
                expanders[0].IsExpanded = true;
                Assert.True(vm.PresetGroups["My Presets"]);
                expanders[0].IsExpanded = false;
                Assert.False(vm.PresetGroups["My Presets"]);
                panel.ShowBuiltIns = false;
                panel.ShowBuiltIns = true;
                staged.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var rebuilt = panel.GetVisualDescendants().OfType<Expander>().ToArray();
                Assert.Equal(6, rebuilt.Length);
                Assert.All(rebuilt, expander => Assert.False(expander.IsExpanded));
                ShowcaseTestHelper.SettleExpanderChevrons(panel);
            }

            if (!collapsed)
            {
                var button = panel.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Tag as string == "builtin_fresh_start");
                Assert.Null(button.ContextMenu);
                Assert.Equal(vm.PresetService.GetById("builtin_fresh_start")!.Description, ToolTip.GetTip(button));
                var point = button.TranslatePoint(new Point(20, 12), staged)!.Value;
                staged.MouseMove(point);
                ToolTip.SetIsOpen(button, true);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var tip = staged.GetVisualDescendants().OfType<ToolTip>().Single();
                ShowcaseTestHelper.Settle(() => tip.Opacity == 1, "Tooltip fade-in");
                Assert.Equal(255, Assert.IsAssignableFrom<ISolidColorBrush>(tip.Background).Color.A);
                Assert.InRange(tip.Bounds.Width, 1, 320);
            }
        });
    }

    [AvaloniaFact]
    public async Task StartupPanelDoesNotReadLooksAndPersonalRowsStayEditable()
    {
        using var root = new TemporaryDirectory();
        var service = new PresetService(root.Path);
        await service.InitializeAsync();
        var personal = await service.SaveUserPresetAsync("Personal", new EditSettings());
        var panel = new PresetsPanel();
        panel.SetPresetSource(service);
        Assert.False(service.AreBuiltInsLoaded);
        var window = new Window { Content = panel, Width = 240, Height = 700 };
        using var scope = new TestUiScope(window);
        Assert.False(service.AreBuiltInsLoaded);
        panel.ShowBuiltIns = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var buttons = panel.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Tag is string && button.Classes.Contains("preset")).ToArray();
        Assert.Equal(27, buttons.Length);

        foreach (var expander in panel.GetVisualDescendants().OfType<Expander>())
        {
            var header = Assert.IsType<TextBlock>(expander.Header);
            var headingX = header.TranslatePoint(default, panel)!.Value.X;
            var rows = Assert.IsType<StackPanel>(expander.Content).Children.OfType<Button>();

            foreach (var button in rows)
            {
                var textX = button.TranslatePoint(new Point(button.Padding.Left, 0), panel)!.Value.X;
                Assert.Equal(12, textX - headingX, precision: 3);
            }
        }

        var row = buttons.Single(button => button.Tag as string == personal.Id);
        Assert.NotNull(row.ContextMenu);
        Assert.Null(ToolTip.GetTip(row));
        Assert.All(buttons.Where(button => button != row), button =>
        {
            Assert.Null(button.ContextMenu);
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(button) as string));
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CollapsePersistenceFailureDoesNotEscapeUiHandler(bool deferred)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask);
        var panel = new PresetsPanel { DataContext = vm };
        var expander = (Expander)panel.FindControl<StackPanel>("PresetsContainer")!.Children[0];
        var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PersistAppSettingsAsync = () => deferred ? write.Task
            : throw new IOException("Injected preference write failure");
        var previous = SynchronizationContext.Current;
        var errors = new CapturedErrors();

        try
        {
            SynchronizationContext.SetSynchronizationContext(errors);
            expander.IsExpanded = false;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        if (deferred)
        {
            write.SetException(new IOException("Injected preference write failure"));
        }

        await errors.Completed.Task.WaitAsync(TestWaits.Condition);
        Assert.Empty(errors.Exceptions);
        Assert.False(expander.IsExpanded);
        Assert.False(vm.PresetGroups["My Presets"]);
        vm.PersistAppSettingsAsync = () => Task.CompletedTask;
        expander.IsExpanded = true;
        Assert.True(vm.PresetGroups["My Presets"]);
    }

    private sealed class CapturedErrors : SynchronizationContext
    {
        internal List<Exception> Exceptions { get; } = [];

        internal TaskCompletionSource Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void OperationCompleted() => Completed.TrySetResult();

        public override void Post(SendOrPostCallback callback, object? state)
        {
            var error = Record.Exception(() => callback(state));

            if (error != null)
            {
                Exceptions.Add(error);
            }
        }
    }
}
