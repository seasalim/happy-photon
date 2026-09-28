using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

internal sealed class PasteGateLoader(IBaseImageLoader? inner = null) : IBaseImageLoader
{
    private int _starts;

    internal int Starts => Volatile.Read(ref _starts);

    public bool CanLoad(ImageFile file) => inner?.CanLoad(file) ?? true;

    public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file,
        BaseDecodeSettings decode, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _starts);

        return inner?.LoadPreviewBaseWithOutcome(file, decode, cancellationToken)
            ?? BaseImageLoadOutcome.Loaded(Create(file, decode));
    }

    public BaseImage? LoadFullBase(ImageFile file, BaseDecodeSettings decode,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _starts);

        return inner is null ? Create(file, decode)
            : inner.LoadFullBase(file, decode, cancellationToken);
    }

    private static BaseImage Create(ImageFile file, BaseDecodeSettings decode) => new(
        new MagickImage(MagickColors.Gray, 64, 48) { ColorSpace = ColorSpace.RGB },
        new BaseImageInfo(file.IsRaw ? BaseSourceKind.RawLibRaw : BaseSourceKind.Standard,
            file.IsRaw, decode, null, null, 6504, 0, false, null, 1, 64, 48));
}

internal static class PasteGateSupport
{
    internal static void RequirePerformance()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 under the exclusive measurement lock.");
#if DEBUG
        Assert.Fail("Build the paste gates in Release.");
#endif
        Assert.True(Environment.ProcessorCount > 2,
            "Set HAPPY_PHOTON_FULL_CPU=1 for timing runs.");
    }

    // Freeze the look for both formats; Recovery and Optics match the target.
    internal static EditSettings SourceLook(bool whiteBalanceOnly = false) => new()
    {
        Exposure = whiteBalanceOnly ? 0 : .75,
        Contrast = whiteBalanceOnly ? 0 : 23,
        Saturation = whiteBalanceOnly ? 0 : 19,
        Wb = new WhiteBalanceSettings { Mode = WbMode.Custom, Kelvin = 7200, Tint = -12 }
    };

    internal static async Task<ImageFile> StoreAsync(CatalogService catalog,
        string path, EditSettings settings)
    {
        var image = new ImageFile(path) { EditSettings = settings.Clone() };
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);

        return image;
    }

    internal static async Task SelectLoadedAsync(MainWindowViewModel vm, ImageFile image)
    {
        vm.IsDevelopMode = true;
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await Assert.IsAssignableFrom<Task>(vm.PendingHistoryLoadTask).WaitAsync(TestWaits.Condition);
        await TestWaits.UntilAsync(() => vm.PreviewImage is Bitmap &&
            vm.InitialPreviewActivityCount == 0 && vm.ImageService.Previews.PreviewActivityCount == 0);
        Assert.True(vm.IsHistoryLoaded);
        Assert.True(vm.IsZoomFitMode);
        Assert.Same(image, Identity(vm).ImageFile);
    }

    internal static PreviewRenderIdentity Identity(MainWindowViewModel vm) =>
        Assert.IsType<PreviewRenderIdentity>(vm.ImageService.Previews.TryGetPreviewRenderIdentity(
            Assert.IsAssignableFrom<Bitmap>(vm.PreviewImage)));

    internal static async Task<double?> PasteAsync(MainWindowViewModel vm, bool measure)
    {
        var previous = Identity(vm);
        var installed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = measure ? new Stopwatch() : null;
        string? installedHash = null;
        PropertyChangedEventHandler onChanged = (_, args) =>
        {
            if (args.PropertyName != nameof(vm.PreviewImage) || vm.PreviewImage is not Bitmap bitmap) return;
            var identity = vm.ImageService.Previews.TryGetPreviewRenderIdentity(bitmap);
            if (identity is null || identity.ImageFile != vm.SelectedImage ||
                identity.Generation <= previous.Generation) return;

            timer?.Stop();
            installedHash = identity.SettingsHash;
            installed.TrySetResult();
        };
        vm.PropertyChanged += onChanged;

        try
        {
            Assert.True(vm.PasteEditSettingsCommand.CanExecute(null));
            timer?.Start();
            await vm.PasteEditSettingsCommand.ExecuteAsync(null).WaitAsync(TestWaits.Condition);
            await installed.Task.WaitAsync(TestWaits.Condition);
            Assert.NotEqual(previous.SettingsHash, installedHash);
            Assert.Equal(RenderSettingsHash.Compute(vm.SelectedImage!.EditSettings), installedHash);
        }
        finally
        {
            vm.PropertyChanged -= onChanged;
        }

        return timer?.Elapsed.TotalMilliseconds;
    }
}
