using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HighlightHandlingControlTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [AvaloniaFact]
    public async Task EnabledStateFollowsProvisionalAndLoadedRawCapability()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        var loader = new ControlledLoader(blockFirstDecode: true);
        var vm = CreateViewModel(catalog, loader);
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = ShowPanel(panel);

        try
        {
            var row = panel.FindControl<Grid>("HighlightHandlingRow")!;
            var control = panel.FindControl<ListBox>("HighlightHandlingControl")!;
            var raw = new ImageFile(Path.Combine(_root.Path, "raw.dng"));

            vm.SelectedImage = raw;

            Assert.True(vm.IsHighlightHandlingEnabled);
            Assert.True(row.IsEnabled);
            Assert.True(row.IsVisible);
            Assert.Equal(HlReconstructionMode.Clip, control.SelectedItem);
            Assert.True(loader.FirstDecodeStarted.Wait(TestWaits.Condition));

            loader.ReleaseFirstDecode.Set();
            await TestWaits.UntilAsync(() => vm.IsWhiteBalanceReady);

            Assert.True(vm.IsHighlightHandlingEnabled);
            Assert.True(row.IsEnabled);
            Assert.True(row.IsVisible);

            vm.SelectedImage = new ImageFile(Path.Combine(
                _root.Path,
                "standard.jpg"));
            Assert.False(vm.IsHighlightHandlingEnabled);
            Assert.False(row.IsEnabled);
            Assert.True(row.IsVisible);
            Assert.Equal(0.32, row.Opacity);
            await TestWaits.UntilAsync(() => vm.IsWhiteBalanceReady);
            Assert.False(row.IsEnabled);
            Assert.True(row.IsVisible);

            vm.SelectedImage = new ImageFile(Path.Combine(
                _root.Path,
                "guard.dng"));
            Assert.True(vm.IsHighlightHandlingEnabled);
            Assert.True(row.IsEnabled);
            Assert.True(row.IsVisible);
            await TestWaits.UntilAsync(() => vm.IsWhiteBalanceReady);

            Assert.False(vm.IsHighlightHandlingEnabled);
            Assert.False(row.IsEnabled);
            Assert.True(row.IsVisible);
            Assert.Equal(0.32, row.Opacity);
            Assert.Equal(
                "Decoded via fallback — RAW controls unavailable",
                vm.StatusMessage);
        }
        finally
        {
            loader.ReleaseFirstDecode.Set();
            window.Close();
            panel.DataContext = null;
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task SelectingBlendUpdatesSettingsPreviewAndSingleStepHistory()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        var loader = new ControlledLoader();
        var vm = CreateViewModel(catalog, loader);
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = ShowPanel(panel);

        try
        {
            var image = new ImageFile(Path.Combine(_root.Path, "toggle.dng"));
            vm.SelectedImage = image;
            await TestWaits.UntilAsync(() =>
                vm.IsWhiteBalanceReady && vm.PreviewImage != null);

            var control = panel.FindControl<ListBox>("HighlightHandlingControl")!;
            Assert.Equal(HlReconstructionMode.Clip, control.SelectedItem);
            Assert.False(vm.CanUndo);

            control.SelectedItem = HlReconstructionMode.Blend;

            await TestWaits.UntilAsync(() =>
                image.EditSettings.HlReconstruction ==
                    HlReconstructionMode.Blend &&
                loader.DecodeRequests.Any(request =>
                    request.HlReconstruction == HlReconstructionMode.Blend));
            Assert.Equal(HlReconstructionMode.Blend, vm.HlReconstruction);
            Assert.True(vm.CanUndo);

            await vm.UndoCommand.ExecuteAsync(null);
            await TestWaits.UntilAsync(() =>
                loader.DecodeRequests.Count(request =>
                    request.HlReconstruction == HlReconstructionMode.Clip) >= 2);

            Assert.Equal(
                HlReconstructionMode.Clip,
                image.EditSettings.HlReconstruction);
            Assert.Equal(HlReconstructionMode.Clip, vm.HlReconstruction);
            Assert.False(vm.CanUndo);
            Assert.True(vm.CanRedo);

            await vm.RedoCommand.ExecuteAsync(null);
            await TestWaits.UntilAsync(() =>
                loader.DecodeRequests.Count(request =>
                    request.HlReconstruction == HlReconstructionMode.Blend) >= 2);

            Assert.Equal(
                HlReconstructionMode.Blend,
                image.EditSettings.HlReconstruction);
            Assert.Equal(HlReconstructionMode.Blend, vm.HlReconstruction);
            Assert.True(vm.CanUndo);
            Assert.False(vm.CanRedo);
        }
        finally
        {
            window.Close();
            panel.DataContext = null;
            await vm.DisposeAsync();
        }
    }

    public void Dispose() => _root.Dispose();

    private static MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        IBaseImageLoader loader) =>
        new(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };

    private static Window ShowPanel(DevelopEditPanel panel)
    {
        var window = new Window
        {
            Width = 250,
            Height = 660,
            Content = panel
        };
        window.Show();
        return window;
    }

    private sealed class ControlledLoader(bool blockFirstDecode) : IBaseImageLoader
    {
        private readonly ConcurrentQueue<BaseDecodeSettings> _decodeRequests = [];
        private int _decodeCount;

        public ControlledLoader() : this(blockFirstDecode: false)
        {
        }

        public ManualResetEventSlim FirstDecodeStarted { get; } = new();
        public ManualResetEventSlim ReleaseFirstDecode { get; } = new();
        public IReadOnlyList<BaseDecodeSettings> DecodeRequests =>
            _decodeRequests.ToArray();

        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            _decodeRequests.Enqueue(decode);
            if (blockFirstDecode && Interlocked.Increment(ref _decodeCount) == 1)
            {
                FirstDecodeStarted.Set();
                ReleaseFirstDecode.Wait(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var isRawSource = file.IsRaw &&
                              !file.FileName.Equals(
                                  "guard.dng",
                                  StringComparison.OrdinalIgnoreCase);
            var pixels = new MagickImage(
                decode.HlReconstruction == HlReconstructionMode.Blend
                    ? MagickColors.Cyan
                    : MagickColors.Gray,
                32,
                24)
            {
                ColorSpace = ColorSpace.RGB
            };
            return new BaseImage(
                pixels,
                new BaseImageInfo(
                    isRawSource
                        ? BaseSourceKind.RawLibRaw
                        : BaseSourceKind.Standard,
                    isRawSource,
                    decode,
                    null,
                    null,
                    isRawSource ? 5500 : 6504,
                    0,
                    false,
                    null,
                    1,
                    32,
                    24));
        }

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(
                LoadPreviewBase(file, decode, cancellationToken));

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
