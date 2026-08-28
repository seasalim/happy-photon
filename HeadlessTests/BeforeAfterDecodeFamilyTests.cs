using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BeforeAfterDecodeFamilyTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [AvaloniaFact]
    public async Task BeforeAfterKeepsDecodeFamilyAndDoesNotQueueReplacementRefresh()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        var loader = new CountingLoader();
        await using var vm = new MainWindowViewModel(
            catalog,
            loader,
            _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };
        vm.SelectedImage = new ImageFile(Path.Combine(_root.Path, "original.png"))
        {
            EditSettings = new EditSettings
            {
                Exposure = 1,
                HlReconstruction = HlReconstructionMode.Blend
            },
            HasEdits = true
        };

        await TestWaits.UntilAsync(() => vm.PreviewImage != null);
        await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);

        Assert.True(vm.IsShowingOriginal);
        Assert.Equal(1, loader.LoadCount);
    }

    public void Dispose() => _root.Dispose();

    private sealed class CountingLoader : IBaseImageLoader
    {
        private int _loadCount;
        public int LoadCount => Volatile.Read(ref _loadCount);
        public bool CanLoad(ImageFile file) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _loadCount);
            return BaseImageLoadOutcome.Loaded(new PreviewBasePair(
                CreateBase(decode),
                large: null));
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static BaseImage CreateBase(BaseDecodeSettings decode) =>
            new(
                new MagickImage(MagickColors.Gray, 64, 48),
                new BaseImageInfo(
                    BaseSourceKind.Standard,
                    false,
                    decode,
                    null,
                    null,
                    6504,
                    0,
                    false,
                    null,
                    1,
                    64,
                    48));
    }
}
