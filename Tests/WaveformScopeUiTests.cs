using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WaveformScopeUiTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [AvaloniaFact]
    public async Task ScopeSelector_UsesCompactDevelopActionPresentation()
    {
        using var catalog = new CatalogService(Path.Combine(_root.Path, "presentation"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(catalog)
        {
            IsDevelopMode = true
        };
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 250, Height = 660, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var scopeSelector = panel.FindControl<ScopeSelectorRow>("ScopeSelector")!;
        var buttons = new[]
        {
            scopeSelector.FindControl<ToggleButton>("HistogramScopeButton")!,
            scopeSelector.FindControl<ToggleButton>("RawHistogramScopeButton")!,
            scopeSelector.FindControl<ToggleButton>("WaveformScopeButton")!
        };
        var observed = string.Join(", ", buttons.Select(button =>
            $"{button.Name}: property={button.Width}x{button.Height}, " +
            $"bounds={button.Bounds.Width}x{button.Bounds.Height}, " +
            $"padding={button.Padding}, border={button.BorderThickness}"));

        Assert.True(
            buttons.All(button =>
                button.Width == 24 &&
                button.Height == 24 &&
                button.Padding == new Thickness(5) &&
                button.BorderThickness == new Thickness(0)),
            observed);

        window.DataContext = null;
        window.Close();
    }

    [AvaloniaFact]
    public async Task ScopeSelector_HasStableEntriesAndExactlyOneBody()
    {
        using var catalog = new CatalogService(Path.Combine(_root.Path, "catalog"));
        await catalog.InitializeAsync();
        var loader = new CountingLoader();
        var vm = new MainWindowViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 250, Height = 660, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var scopeSelector = panel.FindControl<ScopeSelectorRow>("ScopeSelector")!;
        var title = scopeSelector.FindControl<TextBlock>("ScopeTitle")!;
        var histogramButton = scopeSelector.FindControl<ToggleButton>("HistogramScopeButton")!;
        var waveformButton = scopeSelector.FindControl<ToggleButton>("WaveformScopeButton")!;
        var rawButton = scopeSelector.FindControl<ToggleButton>("RawHistogramScopeButton")!;
        var histogram = panel.FindControl<HistogramView>("DevelopHistogram")!;
        var waveform = panel.FindControl<WaveformView>("DevelopWaveform")!;
        var options = vm.ScopeOptions;

        Assert.Equal(
            ["Histogram", "Waveform", "RAW Histogram"],
            options.Select(option => option.DisplayName));
        Assert.Equal("Histogram", title.Text);
        Assert.True(histogramButton.IsChecked);
        Assert.False(waveformButton.IsChecked);
        Assert.False(rawButton.IsChecked);
        Assert.True(histogram.IsVisible);
        Assert.False(waveform.IsVisible);
        Assert.Equal(80, histogram.FindControl<Canvas>("HistogramCanvas")!.Height);
        Assert.Equal(80, waveform.FindControl<Image>("WaveformImage")!.Height);
        Assert.False(rawButton.IsEnabled);
        Assert.Equal("Select a RAW photograph.", ToolTip.GetTip(rawButton));

        var loadCount = loader.LoadCount;
        var activityEpoch = vm.BackgroundActivityEpoch;
        waveformButton.Command!.Execute(waveformButton.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(options, vm.ScopeOptions);
        Assert.Equal("Waveform", title.Text);
        Assert.False(histogramButton.IsChecked);
        Assert.True(waveformButton.IsChecked);
        Assert.False(histogram.IsVisible);
        Assert.True(waveform.IsVisible);
        Assert.Equal(loadCount, loader.LoadCount);
        Assert.Equal(activityEpoch, vm.BackgroundActivityEpoch);

        vm.SelectedScope = ScopeView.RawHistogram;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ScopeView.RawHistogram, vm.SelectedScope);
        Assert.Equal(ScopeView.Histogram, vm.EffectiveScope);
        Assert.Equal("Histogram", title.Text);
        Assert.True(histogramButton.IsChecked);
        Assert.False(rawButton.IsChecked);
        Assert.True(histogram.IsVisible);
        Assert.False(waveform.IsVisible);

        window.DataContext = null;
        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task BrowseRetainsFixedChromeWithoutScopeSelector()
    {
        using var catalog = new CatalogService(Path.Combine(_root.Path, "browse"));
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(catalog);
        var pane = new BrowseReviewPane { DataContext = vm };
        var window = new Window { Width = 250, Height = 660, Content = pane };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            "Histogram",
            pane.FindControl<TextBlock>("BrowseHistogramHeader")!.Text);
        Assert.NotNull(pane.FindControl<Border>("BrowseHistogramBox"));
        Assert.NotNull(pane.FindControl<HistogramView>("BrowseHistogram"));
        Assert.Empty(pane.GetLogicalDescendants().OfType<ComboBox>());

        window.DataContext = null;
        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task CloudOnlySourcesKeepBothModesEmptyWithoutLoading()
    {
        using var catalog = new CatalogService(Path.Combine(_root.Path, "cloud"));
        await catalog.InitializeAsync();
        var loader = new CountingLoader();
        var vm = new MainWindowViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration));
        var cloud = new ImageFile(
            Path.Combine(_root.Path, "cloud.jpg"),
            SourceAvailability.RequiresHydration);

        vm.Browse.SetImages([cloud]);
        vm.SelectedImage = cloud;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(vm.Histogram);
        Assert.Null(vm.EffectiveWaveform);
        Assert.Equal(0, loader.LoadCount);

        vm.IsDevelopMode = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(vm.Histogram);
        Assert.Null(vm.EffectiveWaveform);
        Assert.Equal(0, loader.LoadCount);

        await vm.DisposeAsync();
    }

    public void Dispose() => _root.Dispose();

    private sealed class CountingLoader : IBaseImageLoader
    {
        private int _loadCount;

        public int LoadCount => Volatile.Read(ref _loadCount);
        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _loadCount);
            return BaseImageLoadOutcome.FromImage(
                null,
                BaseImageLoadFailure.DecodeFailed);
        }

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _loadCount);
            return null;
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _loadCount);
            return null;
        }
    }
}
