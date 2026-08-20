using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EffectsControlTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-effects-ui-{Guid.NewGuid():N}")).FullName;

    [AvaloniaFact]
    public async Task EffectsGroup_MatchesPanelOrderAndControlStates()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask);
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 250, Height = 820, Content = panel };
        window.Show();
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(Path.Combine(_root, "photo.jpg"));
        Dispatcher.UIThread.RunJobs();

        var detail = panel.FindControl<DetailEditGroup>("DetailEditGroup")!;
        var effects = panel.FindControl<EffectsEditGroup>("EffectsEditGroup")!;
        var stack = Assert.IsType<StackPanel>(detail.Parent);
        Assert.Equal(
            stack.Children.IndexOf(detail) + 1,
            stack.Children.IndexOf(effects));

        var vignette = effects.FindControl<CompactSlider>("VignetteSlider")!;
        var midpoint = effects.FindControl<CompactSlider>("MidpointSlider")!;
        var midpointRow = effects.FindControl<Grid>("MidpointRow")!;
        var grain = effects.FindControl<CompactSlider>("GrainSlider")!;
        var sizes = effects.FindControl<ListBox>("GrainSizeControl")!;
        Assert.Equal((-100, 100), (vignette.Minimum, vignette.Maximum));
        Assert.Equal((0, 100), (midpoint.Minimum, midpoint.Maximum));
        Assert.Equal((0, 100), (grain.Minimum, grain.Maximum));
        Assert.True(vignette.EnableDoubleClickReset);
        Assert.False(midpointRow.IsEnabled);
        Assert.Equal(0.32, midpointRow.Opacity);
        Assert.Equal(22, sizes.Height);
        Assert.Equal(new Thickness(2), sizes.Padding);
        Assert.Equal(new CornerRadius(4), sizes.CornerRadius);
        Assert.Equal(new Thickness(0), sizes.BorderThickness);
        Assert.Equal(3, sizes.ItemCount);
        Assert.Equal(GrainSize.Medium, sizes.SelectedItem);

        vm.Vignette = -1;
        Dispatcher.UIThread.RunJobs();
        Assert.True(midpointRow.IsEnabled);
        Assert.Equal(1, midpointRow.Opacity);

        await vm.ResetEditsCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, vm.Vignette);
        Assert.Equal(50, vm.Midpoint);
        Assert.Equal(0, vm.Grain);
        Assert.Equal(GrainSize.Medium, sizes.SelectedItem);
        Assert.True(effects.IsVisible);

        window.Close();
        panel.DataContext = null;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private sealed class NullBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.FromImage(
                null,
                BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;
    }
}
