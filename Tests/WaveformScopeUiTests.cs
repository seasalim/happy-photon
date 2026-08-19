using Avalonia.Controls;
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
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-waveform-ui-{Guid.NewGuid():N}")).FullName;

    [AvaloniaFact]
    public async Task ScopeSelector_HasStableEntriesAndExactlyOneBody()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        var loader = new CountingLoader();
        var vm = new MainWindowViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 250, Height = 660, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var selector = panel.FindControl<ComboBox>("ScopeSelector")!;
        var histogram = panel.FindControl<HistogramView>("DevelopHistogram")!;
        var waveform = panel.FindControl<WaveformView>("DevelopWaveform")!;
        var options = vm.ScopeOptions;

        Assert.Equal(3, selector.ItemCount);
        Assert.Equal(
            ["HISTOGRAM", "WAVEFORM", "RAW HISTOGRAM"],
            options.Select(option => option.DisplayName));
        Assert.True(histogram.IsVisible);
        Assert.False(waveform.IsVisible);
        Assert.Equal(80, histogram.FindControl<Canvas>("HistogramCanvas")!.Height);
        Assert.Equal(80, waveform.FindControl<Image>("WaveformImage")!.Height);
        Assert.False(options[2].IsEnabled);
        Assert.Equal("Select a RAW photograph.", options[2].Hint);
        selector.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(Assert.IsType<ComboBoxItem>(
            selector.ContainerFromIndex(2)).IsEnabled);
        selector.IsDropDownOpen = false;

        var loadCount = loader.LoadCount;
        var activityEpoch = vm.BackgroundActivityEpoch;
        vm.SelectedScope = ScopeView.Waveform;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(options, vm.ScopeOptions);
        Assert.False(histogram.IsVisible);
        Assert.True(waveform.IsVisible);
        Assert.Equal(loadCount, loader.LoadCount);
        Assert.Equal(activityEpoch, vm.BackgroundActivityEpoch);

        vm.SelectedScope = ScopeView.RawHistogram;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ScopeView.RawHistogram, vm.SelectedScope);
        Assert.Equal(ScopeView.Histogram, vm.EffectiveScope);
        Assert.Equal("HISTOGRAM", vm.EffectiveScopeTitle);
        Assert.Same(options[0], selector.SelectedItem);
        Assert.True(histogram.IsVisible);
        Assert.False(waveform.IsVisible);

        window.DataContext = null;
        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task LibraryRetainsFixedChromeWithoutScopeSelector()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "library"));
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(catalog);
        var pane = new LibraryReviewPane { DataContext = vm };
        var window = new Window { Width = 250, Height = 660, Content = pane };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            "HISTOGRAM",
            pane.FindControl<TextBlock>("LibraryHistogramHeader")!.Text);
        Assert.NotNull(pane.FindControl<Border>("LibraryHistogramBox"));
        Assert.NotNull(pane.FindControl<HistogramView>("LibraryHistogram"));
        Assert.Empty(pane.GetLogicalDescendants().OfType<ComboBox>());

        window.DataContext = null;
        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task CloudOnlySourcesKeepBothModesEmptyWithoutLoading()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "cloud"));
        await catalog.InitializeAsync();
        var loader = new CountingLoader();
        var vm = new MainWindowViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration));
        var cloud = new ImageFile(
            Path.Combine(_root, "cloud.jpg"),
            SourceAvailability.RequiresHydration);

        vm.Library.SetImages([cloud]);
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

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

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
