using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EditAutoSaveRefreshRaceTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("autosave-refresh-race");

    [AvaloniaFact]
    public async Task DecodeChangingEditPersistsWhenRefreshOutpacesInteractiveRender()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var loader = new RecordingLoader();
        var vm = _fixture.CreateViewModel(
            catalog,
            loader,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        var image = new ImageFile(_fixture.Path("race.dng"));
        vm.SelectedImage = image;
        var interactiveStarted = NewSignal();
        var releaseInteractive = NewSignal();

        try
        {
            await TestWaits.UntilAsync(() =>
                vm.IsWhiteBalanceReady && vm.PreviewImage != null);
            var beforeEdit = vm.PreviewImage;

            // Hold the interactive (stale-base) render at its gate so the
            // queued base refresh — started by the same edit — decodes,
            // renders, and paints first.
            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                interactiveStarted.TrySetResult();
                return releaseInteractive.Task;
            };

            vm.HlReconstruction = HlReconstructionMode.Blend;

            await interactiveStarted.Task.WaitAsync(TestWaits.Condition);
            await TestWaits.UntilAsync(() =>
                loader.DecodeRequests.Any(request =>
                    request.HlReconstruction == HlReconstructionMode.Blend));
            await TestWaits.UntilAsync(() =>
                !ReferenceEquals(vm.PreviewImage, beforeEdit));
            releaseInteractive.TrySetResult();

            await TestWaits.UntilAsync(() =>
                image.EditSettings.HlReconstruction ==
                    HlReconstructionMode.Blend);
            Assert.Equal(HlReconstructionMode.Blend, vm.HlReconstruction);

            // The in-memory mutation precedes the catalog write; poll the
            // catalog so the persistence claim is durable.
            var deadline = DateTime.UtcNow + TestWaits.Condition;
            while (true)
            {
                var state = (await catalog.LoadOrCreateImageStatesAsync(
                    [image.FilePath]))[image.FilePath].Single();
                if (state.EditSettings.HlReconstruction ==
                    HlReconstructionMode.Blend)
                {
                    break;
                }
                Assert.True(
                    DateTime.UtcNow < deadline,
                    "The catalog never persisted the Blend edit.");
                await Task.Delay(10);
            }
        }
        finally
        {
            releaseInteractive.TrySetResult();
            vm.ImageService.Previews.RenderGateAsync = null;
            await vm.DisposeAsync();
        }
    }

    public void Dispose() => _fixture.Dispose();

    private static TaskCompletionSource NewSignal() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RecordingLoader : IBaseImageLoader
    {
        private readonly ConcurrentQueue<BaseDecodeSettings> _decodeRequests = [];

        public IReadOnlyList<BaseDecodeSettings> DecodeRequests =>
            _decodeRequests.ToArray();

        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            _decodeRequests.Enqueue(decode);
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
                    BaseSourceKind.RawLibRaw,
                    true,
                    decode,
                    null,
                    null,
                    5500,
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
