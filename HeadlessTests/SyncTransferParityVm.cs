using System.Reflection;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

internal sealed class SyncTransferParityVm : IAsyncDisposable
{
    internal const string SourcePresetId = "user_sync_source";

    private readonly CatalogVmFixture _fixture = new("sync-parity");

    private readonly TestTimeProvider _clock = new();

    internal CatalogService Catalog { get; private set; } = null!;

    internal MainWindowViewModel Vm { get; private set; } = null!;

    internal async Task InitializeAsync(bool includeDeletedPreset = false)
    {
        Catalog = await _fixture.CreateCatalogAsync();
        Vm = _fixture.CreateViewModel(Catalog, new TinyBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            timeProvider: _clock);
        Vm.ImageService.Previews.AdjacentWarmEnabled = false;
        Vm.IsDevelopMode = true;
        var presets = _fixture.Path("presets");
        Directory.CreateDirectory(presets);

        var ids = new List<string> { SyncTransferParityCorpus.PresetId, SourcePresetId };

        if (includeDeletedPreset)
        {
            ids.Add(SyncTransferParityCorpus.DeletedPresetId);
        }

        foreach (var id in ids)
        {
            await File.WriteAllTextAsync(Path.Combine(presets, $"{id}.json"),
                JsonSerializer.Serialize(new UserPresetFile { Id = id, Name = id }));
        }

        await Vm.PresetService.UseDirectoryAsync(presets);
    }

    internal async Task<ImageFile> ImageAsync(string name, EditSettings settings)
    {
        var extension = settings.RawProfile == null ? "jpg" : "dng";
        var image = new ImageFile(_fixture.Path($"{name}.{extension}")) { EditSettings = settings.Clone() };
        image.CatalogId = await Catalog.GetOrCreateImageAsync(image.FilePath);
        await Catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);

        return image;
    }

    internal async Task SelectAsync(ImageFile image, bool develop = true)
    {
        Vm.IsDevelopMode = develop;
        Vm.Browse.SetImages([image]);
        Vm.Browse.SelectOnly(image);
        Vm.SelectedImage = image;

        if (develop)
        {
            await Assert.IsAssignableFrom<Task>(Vm.PendingHistoryLoadTask).WaitAsync(TestWaits.Condition);
            Assert.True(Vm.IsHistoryLoaded);
            await TestWaits.UntilAsync(() => Vm.PreviewImage != null &&
                Vm.InitialPreviewActivityCount == 0 && Vm.ImageService.Previews.PreviewActivityCount == 0);
        }
    }

    internal async Task CopyAsync(ImageFile source)
    {
        await SelectAsync(source);
        Assert.True(Vm.CopyEditSettingsCommand.CanExecute(null));
        Vm.CopyEditSettingsCommand.Execute(null);
        Assert.True(Vm.HasCopiedSettings);
    }

    internal async Task EnterDraftAsync()
    {
        await Vm.ToggleCropModeCommand.ExecuteAsync(null);
        await SettleCropAsync();
        Vm.CurrentCrop = new CropRegion { Left = .21, Top = .24, Right = .76, Bottom = .81 };
        Vm.HorizonRotation = -7.5;
        await SettleCropAsync();
        Assert.True(Vm.IsCropMode);
        Assert.NotEqual(Vm.HorizonRotation, Vm.SelectedImage!.EditSettings.HorizonRotation);
        Assert.NotEqual(Vm.CurrentCrop.Left, Vm.SelectedImage.EditSettings.Crop!.Left);
    }

    internal async Task ExitDraftAsync()
    {
        await Vm.CancelCropCommand.ExecuteAsync(null);
        await SettleCropAsync();
    }

    private async Task SettleCropAsync()
    {
        _clock.Advance(TimeSpan.FromMilliseconds(200));

        if (Vm.PendingPreviewDebounceTask is { } pending)
        {
            await pending.WaitAsync(TestWaits.Condition);
        }
    }

    internal async Task RecordAsync(SyncTransferParityRecording recording, string key, ImageFile image,
        bool includeRender = false)
    {
        if (Vm.PendingHistoryCommitTask is { } pending)
        {
            await pending.WaitAsync(TestWaits.Condition);
        }

        var stored = await Catalog.LoadImageStatesAsync([image.FilePath]);
        var history = await Catalog.LoadEditHistoryAsync(image.CatalogId);
        recording.Add(key, new
        {
            Model = SyncTransferParityRecording.Settings(image.EditSettings),
            Persisted = SyncTransferParityRecording.Settings(Assert.Single(stored[image.FilePath]).EditSettings),
            history.Position,
            History = history.Entries.Select(entry => new
            {
                entry.Label,
                Settings = SyncTransferParityRecording.Settings(entry.Settings)
            }).ToArray(),
            Live = LiveState(),
            RenderSettingsHash = includeRender ? RenderHash() : null
        });
    }

    private object LiveState()
    {
        var live = Vm.SelectedImage!.EditSettings.Clone();
        // Observe the production projection, plus raw controls that it normalizes or omits.
        typeof(MainWindowViewModel).GetMethod("SaveSlidersTo", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(Vm, [live]);

        return new
        {
            Settings = SyncTransferParityRecording.Settings(live),
            Vm.Exposure,
            Vm.Brightness,
            Vm.Contrast,
            Vm.Highlights,
            Vm.Shadows,
            Vm.Whites,
            Vm.Blacks,
            Vm.Saturation,
            Vm.Vibrance,
            Vm.SelectedWhiteBalanceMode,
            Vm.WhiteBalanceKelvinPosition,
            Vm.WhiteBalanceTint,
            Vm.HlReconstruction,
            Vm.CaptureSharpen,
            Vm.LuminanceNr,
            Vm.ChromaNr,
            Vm.Vignette,
            Vm.Midpoint,
            Vm.Grain,
            Vm.GrainSize,
            Vm.ActiveMixerBand,
            Vm.MixerHue,
            Vm.MixerSaturation,
            Vm.MixerLuminance,
            Vm.LensDistortion,
            Vm.LensChromaticAberration,
            Vm.LensVignetting,
            Vm.LensProfileOverride,
            Vm.GeometryVertical,
            Vm.GeometryHorizontal,
            Vm.GeometryAspect,
            Vm.GeometryDistortion,
            Vm.Rotation,
            Vm.HorizonRotation,
            Vm.CurrentCrop,
            Vm.IsCropMode,
            Vm.ActivePresetId,
            Vm.ActiveCurveChannel,
            Curve = Vm.CurrentCurve?.Points.Select(point => new { point.X, point.Y }).ToArray(),
            LocalIds = Vm.LocalRows.Select(row => row.Local.Id).ToArray(),
            SelectedLocalId = Vm.SelectedLocal?.Id,
            RawProfile = Vm.RawProfilePickerState.SelectedOption?.Selection,
            Vm.CanUndo,
            Vm.CanRedo,
            History = Vm.HistoryEntries.Select(entry => new { entry.Label, entry.IsCurrent }).ToArray()
        };
    }

    internal long RenderGeneration => RenderIdentity().Generation;

    private string RenderHash() => RenderIdentity().SettingsHash;

    private PreviewRenderIdentity RenderIdentity()
    {
        var bitmap = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(Vm.PreviewImage);
        var identity = Assert.IsType<PreviewRenderIdentity>(Vm.ImageService.Previews.TryGetPreviewRenderIdentity(bitmap));
        Assert.Same(Vm.SelectedImage, identity.ImageFile);

        return identity;
    }

    public async ValueTask DisposeAsync()
    {
        if (Vm != null) await Vm.DisposeAsync();
        Catalog?.Dispose();
        _fixture.Dispose();
    }

    // Real successful rendering on synthetic pixels, with no source file content reads.
    private sealed class TinyBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) => BaseImageLoadOutcome.Loaded(Create(file, decode));

        public BaseImage LoadFullBase(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) => Create(file, decode);

        private static BaseImage Create(ImageFile file, BaseDecodeSettings decode) => new(
            new MagickImage(MagickColors.Gray, 16, 12) { ColorSpace = ColorSpace.RGB },
            new BaseImageInfo(file.IsRaw ? BaseSourceKind.RawLibRaw : BaseSourceKind.Standard, file.IsRaw, decode, null, null,
                6504, 0, false, null, 1, 16, 12));
    }
}
