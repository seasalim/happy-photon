using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LensControlTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [AvaloniaFact]
    public async Task OpticsGroupStaysAtPanelTailAndDimsByCapability()
    {
        using var catalog = new CatalogService(Path.Combine(_root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask);
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 260, Height = 820, Content = panel };
        window.Show();
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(Path.Combine(_root.Path, "photo.jpg"));
        Dispatcher.UIThread.RunJobs();

        var effects = panel.FindControl<EffectsEditGroup>("EffectsEditGroup")!;
        var optics = panel.FindControl<LensEditGroup>("LensEditGroup")!;
        var stack = Assert.IsType<StackPanel>(effects.Parent);
        Assert.Equal(stack.Children.Count - 1, stack.Children.IndexOf(optics));
        Assert.False(optics.FindControl<StackPanel>("OpticsGroup")!.IsEnabled);
        Assert.Equal("NO CORRECTION DATA FOR THIS LENS",
            optics.FindControl<TextBlock>("LensSourceText")!.Text);

        vm.SelectedImage = new ImageFile(Path.Combine(_root.Path, "photo.dng"));
        vm.ApplyLensPrescription(true, new LensPrescriptionSummary(
            "Test 24mm", "DNG OPCODES", true, true, false));
        Dispatcher.UIThread.RunJobs();

        Assert.True(optics.FindControl<StackPanel>("OpticsGroup")!.IsEnabled);
        Assert.True(optics.FindControl<Grid>("DistortionRow")!.IsEnabled);
        Assert.True(optics.FindControl<Grid>("ChromaticAberrationRow")!.IsEnabled);
        Assert.False(optics.FindControl<Grid>("VignettingRow")!.IsEnabled);
        Assert.Equal("Test 24mm · DNG OPCODES",
            optics.FindControl<TextBlock>("LensSourceText")!.Text);
        Assert.Equal(28, optics.FindControl<Grid>("DistortionRow")!.Height);
        Assert.Equal(28, optics.FindControl<Grid>("VignettingRow")!.Height);

        window.Close();
        panel.DataContext = null;
    }

    public void Dispose()
    {
        _root.Dispose();
    }
}
