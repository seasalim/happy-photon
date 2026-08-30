using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Reflection;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class GeometryControlTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [AvaloniaFact]
    public async Task GeometryGroupBindsFourAlwaysEnabledResettableSliders()
    {
        using var catalog = new CatalogService(Path.Combine(_root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask);
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 260, Height = 1_200, Content = panel };
        window.Show();
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(Path.Combine(_root.Path, "photo.jpg"));
        Dispatcher.UIThread.RunJobs();

        var effects = panel.FindControl<EffectsEditGroup>("EffectsEditGroup")!;
        var geometry = panel.FindControl<GeometryEditGroup>("GeometryEditGroup")!;
        var optics = panel.FindControl<LensEditGroup>("LensEditGroup")!;
        var stack = Assert.IsType<StackPanel>(geometry.Parent);
        Assert.Equal(stack.Children.IndexOf(effects) + 1,
            stack.Children.IndexOf(geometry));
        Assert.Equal(stack.Children.IndexOf(geometry) + 1,
            stack.Children.IndexOf(optics));

        var sliders = new[]
        {
            geometry.FindControl<CompactSlider>("GeometryVerticalSlider")!,
            geometry.FindControl<CompactSlider>("GeometryHorizontalSlider")!,
            geometry.FindControl<CompactSlider>("GeometryAspectSlider")!,
            geometry.FindControl<CompactSlider>("GeometryDistortionSlider")!
        };
        Assert.All(sliders, slider =>
        {
            Assert.Equal((-100, 100), (slider.Minimum, slider.Maximum));
            Assert.Equal(1, slider.SmallChange);
            Assert.True(slider.EnableDoubleClickReset);
            Assert.True(slider.IsEnabled);
        });

        vm.GeometryVertical = -18;
        vm.GeometryHorizontal = 27;
        vm.GeometryAspect = -36;
        vm.GeometryDistortion = 45;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([-18d, 27d, -36d, 45d],
            sliders.Select(slider => slider.Value));

        DoubleClickReset(window, sliders[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, vm.GeometryVertical);

        window.Close();
        panel.DataContext = null;
    }

    [AvaloniaFact]
    public async Task HistoryRestorationAppliesManualGeometryOutsideTransferPolicy()
    {
        using var catalog = new CatalogService(Path.Combine(_root.Path, "history-catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask);
        var image = new ImageFile(Path.Combine(_root.Path, "history.jpg"))
        {
            EditSettings = new EditSettings
            {
                Geometry = new GeometrySettings { Vertical = 60 }
            }
        };
        vm.SelectedImage = image;
        vm.IsDevelopMode = true;
        var restored = new EditSettings
        {
            Exposure = 1,
            Geometry = new GeometrySettings
            {
                Vertical = -18,
                Horizontal = 27,
                Aspect = -36,
                Distortion = 45
            }
        };
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyHistoryStateAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var generation = (long)typeof(MainWindowViewModel).GetField(
            "_historySubjectGeneration",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;

        // Bind by parameter name so optional additions to the apply signature
        // do not break this test again.
        var args = method.GetParameters().Select(parameter => parameter.Name switch
        {
            "image" => (object?)image,
            "historyGeneration" => generation,
            "state" => restored,
            "position" => 0,
            _ => parameter.HasDefaultValue ? parameter.DefaultValue : null
        }).ToArray();
        await (Task)method.Invoke(vm, args)!;

        Assert.Equal(-18, image.EditSettings.Geometry?.Vertical);
        Assert.Equal(27, vm.GeometryHorizontal);
        Assert.Equal(-36, vm.GeometryAspect);
        Assert.Equal(45, vm.GeometryDistortion);
    }

    private static void DoubleClickReset(Window window, CompactSlider slider)
    {
        var layout = slider.FindControl<Grid>("LayoutGrid")!;
        PointerPressedEventArgs? sample = null;
        window.AddHandler(
            InputElement.PointerPressedEvent,
            (_, args) => sample = args,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        window.MouseDown(new Point(10, 10), MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(new Point(10, 10), MouseButton.Left, RawInputModifiers.None);
        Assert.NotNull(sample);
        layout.RaiseEvent(new PointerPressedEventArgs(
            layout,
            sample!.Pointer,
            layout,
            new Point(110, 11),
            sample.Timestamp + 1,
            sample.Properties,
            sample.KeyModifiers,
            clickCount: 2)
        {
            RoutedEvent = InputElement.PointerPressedEvent
        });
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _root.Dispose();
    }
}
