using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly FolderService _folderService = new();
    private readonly FolderTreeService _folderTreeService;
    private readonly CatalogService _catalogService;
    private readonly Lazy<ImageService> _imageService;
    private readonly Func<ImageFile, Task> _loadMetadataAsync;
    private readonly FileOperationService _fileOperationService = new();
    private readonly UiBitmapRetirement _bitmapRetirement = new();
    private readonly TimeProvider _timeProvider;

    public PresetService PresetService { get; }

    public MainWindowViewModel(CatalogService catalogService)
        : this(catalogService, baseLoader: null)
    {
    }

    internal MainWindowViewModel(
        CatalogService catalogService,
        IBaseImageLoader? baseLoader,
        Func<ImageFile, Task>? loadMetadataAsync = null,
        ISourceAvailabilityService? availabilityService = null,
        Action<Action>? postSelection = null,
        UpdateCheckService? updateCheckService = null,
        UpdateInstallChannel? updateInstallChannel = null,
        LibRawRuntimeHealth? rawRuntimeHealth = null,
        TimeProvider? timeProvider = null)
    {
        _catalogService = catalogService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _rawRuntimeHealth = rawRuntimeHealth;
        _updateCheckService = updateCheckService ?? new UpdateCheckService();
        _updateInstallChannel = updateInstallChannel ?? UpdateChannelSelector.Current;
        _exportActivities = new BackgroundExportActivityRegistry(
            SignalBackgroundActivityStarted);
        Library = new LibraryImageState(RetireThumbnail);
        _folderTreeService = new FolderTreeService(
            catalogService.HasExplicitPath ? catalogService.CatalogPath : null);
        _imageService = new Lazy<ImageService>(() =>
        {
            var health = _rawRuntimeHealth ?? LibRawNativeSupport.Health;
            var loader = baseLoader ?? new BaseLoaderRouter(
                new RawBaseLoader(health),
                new StandardBaseLoader());
            var service = new ImageService(
                catalogService,
                loader,
                availabilityService ?? new SourceAvailabilityService(),
                health);
            service.Previews.PreviewRefreshed += OnPreviewRefreshed;
            service.Previews.PreviewLoadCompleted += OnPreviewLoadCompleted;
            service.Previews.BaseRefreshStateChanged +=
                OnBaseRefreshStateChanged;
            service.Previews.RenderedThumbnailWorkStarted +=
                OnRenderedThumbnailWorkStarted;
            return service;
        });
        _loadMetadataAsync = loadMetadataAsync ??
            (image => ImageService.Metadata.LoadAsync(image));
        _postSelection = postSelection ??
            (action => Dispatcher.UIThread.Post(
                action,
                DispatcherPriority.Background));
        PresetService = new PresetService();
        Library.FilterChanged += OnLibraryFilterChanged;
        Library.StateChanged += OnLibraryStateChanged;
    }

    // Parameterless constructor for design-time
    public MainWindowViewModel() : this(new CatalogService())
    {
    }

    public Task InitializeAsync() => InitializeAsync(new AppDataLocations(
        _catalogService.CatalogPath,
        _catalogService.CachePath,
        AppDataLocationOrigin.Persisted,
        AppDataLocationOrigin.Persisted,
        string.Equals(_catalogService.CatalogPath, _catalogService.CachePath,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal)
            ? AppDataLocationTopology.LegacyCoLocated
            : AppDataLocationTopology.Split));

    public Task InitializeAsync(AppDataLocations locations)
    {
        _folderTreeService.UseExcludedRoots(
            new[] { locations.CatalogRoot, locations.CacheRoot }.Distinct());
        return PresetService.UseDirectoryAsync(locations.PresetsRoot);
    }

    internal ImageService ImageService => _imageService.Value;

    private const int ThumbnailConcurrency = 6;
    private CancellationTokenSource? _thumbnailLoadingCts;
    private CancellationTokenSource? _previewLoadingCts;

    // Add a new field for thumbnail debouncing
    private CancellationTokenSource? _thumbnailDebounce;

    [ObservableProperty]
    private string? _currentFolderPath;

    // Folder tree
    [ObservableProperty]
    private ObservableCollection<FolderNode> _rootFolders = new();

    [ObservableProperty]
    private FolderNode? _selectedFolder;

    // View mode toggle (Library vs Develop)
    [ObservableProperty]
    private bool _isDevelopMode;

    [ObservableProperty]
    private bool _isFullScreenMode;

    public LibraryImageState Library { get; }

    [ObservableProperty]
    private ImageFile? _selectedImage;

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    private HistogramData? _histogram;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private double _minZoom = 0.1;

    [ObservableProperty]
    private double _maxZoom = 5.0;

    [ObservableProperty]
    private int _selectedCount;

    public string? ActiveFileName => SelectedImage?.FileName;

    public ExportSettings ExportSettings { get; } = new();

    // Edit sliders bound to selected image's settings
    [ObservableProperty]
    private double _exposure;

    [ObservableProperty]
    private int _brightness;

    [ObservableProperty]
    private int _contrast;

    [ObservableProperty]
    private int _saturation;

    [ObservableProperty]
    private int _vibrance;

    [ObservableProperty]
    private int _shadows;

    [ObservableProperty]
    private int _highlights;

    [ObservableProperty]
    private int _rotation;

    [ObservableProperty]
    private double _horizonRotation;

    [ObservableProperty]
    private string? _activePresetId;

    [ObservableProperty]
    private bool _hasSelectedImage;

    public bool CanEditSelectedImage =>
        HasSelectedImage &&
        SelectedImage?.SourceRequiresHydration != true;

    public bool CanSavePreset => CanEditSelectedImage && IsDevelopMode;

    [ObservableProperty]
    private bool _canReset;

    [ObservableProperty]
    private bool _isShowingOriginal;

    // Crop mode state
    [ObservableProperty]
    private bool _isCropMode;

    [ObservableProperty]
    private CropRegion? _currentCrop;

    [ObservableProperty]
    private bool _isCropAspectLocked = true;

    // Original crop before entering crop mode (for cancel)
    private CropRegion? _cropBeforeEdit;
    private double _horizonRotationBeforeEdit;

    // Commands that need View access for dialogs - set from code-behind
    public IRelayCommand? ZoomFitCommand { get; set; }

    // Callback to request zoom-to-fit after image loads
    public Action? RequestZoomFit { get; set; }

    // Callback to show the owned export dialog from the View
    public Func<ExportDialogMode, Task>? RequestExportDialogAsync { get; set; }

    // Callback for delete confirmation dialog
    public Func<string, Task<bool>>? ConfirmMoveToTrashAsync { get; set; }

    // Callback for rejected-image delete confirmation dialog
    public Func<int, string?, Task<bool>>? ConfirmDeleteRejectedAsync { get; set; }

    // Callback for rejected-image delete failure dialog
    public Func<int, Task>? ShowDeleteRejectedFailuresAsync { get; set; }

    // Debouncing for live preview
    private CancellationTokenSource? _previewDebounce;
    private CancellationTokenSource? _histogramDebounce;
    private bool _isLoadingImage;

    // Undo/redo history for edit state
    private readonly EditHistory _history = new();
    private EditSettings? _lastSavedState;

    // Hover preview state
    private bool _isHoveringPreset;
    private EditSettings? _preHoverSettings;
    private CancellationTokenSource? _hoverPreviewCts;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    partial void OnCanUndoChanged(bool value) => UndoCommand.NotifyCanExecuteChanged();
    partial void OnCanRedoChanged(bool value) => RedoCommand.NotifyCanExecuteChanged();
    partial void OnHasSelectedImageChanged(bool value)
    {
        NotifySelectedImageEditStateChanged();
        OnPropertyChanged(nameof(IsDevelopEmptyStateVisible));
    }

    private void NotifySelectedImageEditStateChanged()
    {
        OnPropertyChanged(nameof(CanEditSelectedImage));
        OnPropertyChanged(nameof(CanSavePreset));
        CopyEditSettingsCommand.NotifyCanExecuteChanged();
        PasteEditSettingsCommand.NotifyCanExecuteChanged();
        NotifyClippingCommandState();
    }
    partial void OnCanResetChanged(bool value)
    {
        ResetEditsCommand.NotifyCanExecuteChanged();
        ToggleBeforeAfterCommand.NotifyCanExecuteChanged();
    }

    // Partial methods for live preview updates
    partial void OnExposureChanged(double value) => OnEditValueChanged();
    partial void OnBrightnessChanged(int value) => OnEditValueChanged();
    partial void OnContrastChanged(int value) => OnEditValueChanged();
    partial void OnSaturationChanged(int value) => OnEditValueChanged();
    partial void OnVibranceChanged(int value) => OnEditValueChanged();
    partial void OnShadowsChanged(int value) => OnEditValueChanged();
    partial void OnHighlightsChanged(int value) => OnEditValueChanged();
    partial void OnHorizonRotationChanged(double value) => OnHorizonRotationValueChanged();

    private void OnEditValueChanged()
    {
        if (_isLoadingImage || !CanEditSelectedImage)
        {
            return;
        }

        UpdateCanReset();
        SchedulePreviewUpdate();
    }

    private void OnHorizonRotationValueChanged()
    {
        var image = SelectedImage;
        if (_isLoadingImage || !CanEditSelectedImage || image == null) return;

        if (IsCropMode)
        {
            ConstrainCropToSafeHorizonBounds();
            ScheduleCropPreviewUpdate();
            return;
        }

        image.EditSettings.HorizonRotation = HorizonRotation;
        SchedulePreviewUpdate(pushUndo: false);
    }

    private void SaveSlidersTo(EditSettings target)
    {
        target.Exposure = Exposure;
        SaveWhiteBalanceTo(target);
        SaveHighlightReconstructionTo(target);
        SaveDetailTo(target);
        SaveEffectsTo(target);
        SaveMixerTo(target);
        SaveLensTo(target);
        target.Brightness = Brightness;
        target.Contrast = Contrast;
        target.Saturation = Saturation;
        target.Vibrance = Vibrance;
        target.Shadows = Shadows;
        target.Highlights = Highlights;
        target.Rotation = Rotation;
        target.HorizonRotation = HorizonRotation;
        target.AppliedPresetId = ActivePresetId;
    }

    private void LoadSlidersFrom(EditSettings source)
    {
        Exposure = source.Exposure;
        LoadWhiteBalanceFrom(source);
        LoadHighlightReconstructionFrom(source);
        LoadDetailFrom(source);
        LoadEffectsFrom(source);
        LoadMixerFrom(source);
        LoadLensFrom(source);
        Brightness = source.Brightness;
        Contrast = source.Contrast;
        Saturation = source.Saturation;
        Vibrance = source.Vibrance;
        Shadows = source.Shadows;
        Highlights = source.Highlights;
        Rotation = source.Rotation;
        HorizonRotation = source.HorizonRotation;
        CurrentCrop = source.Crop?.Clone();
        LoadCurrentCurveFrom(source);
        ActivePresetId = source.AppliedPresetId != null &&
                         PresetService.GetById(source.AppliedPresetId) != null
            ? source.AppliedPresetId
            : null;
    }

    private static CancellationTokenSource ReplaceDebounce(
        ref CancellationTokenSource? current)
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref current, next);
        previous?.Cancel();
        previous?.Dispose();
        return next;
    }

    private void SchedulePreviewUpdate(bool pushUndo = true)
    {
        if (_isLoadingImage || !CanEditSelectedImage) return;

        _thumbnailDebounce?.Cancel();
        if (pushUndo) PushUndoState();
        var generation = RequestEditedRender();

        var debounce = ReplaceDebounce(ref _previewDebounce);
        _ = DebouncedAction.RunAsync(
            "preview update",
            TimeSpan.FromMilliseconds(150),
            debounce.Token,
            async () =>
        {
            if (!await UpdatePreviewWithCurrentSliders(
                debounce.Token,
                generation))
            {
                return;
            }
            debounce.Token.ThrowIfCancellationRequested();
            await AutoSaveAsync();
            debounce.Token.ThrowIfCancellationRequested();
            ScheduleThumbnailRefresh();
        },
            timeProvider: _timeProvider);
    }

    private void ScheduleCropPreviewUpdate()
    {
        if (_isLoadingImage || SelectedImage == null) return;

        var generation = ReserveRenderOutcome(
            PreviewSurfaceIntent.Edited,
            promotionEligible: false);
        var debounce = ReplaceDebounce(ref _previewDebounce);
        _ = DebouncedAction.RunAsync(
            "crop preview update",
            TimeSpan.FromMilliseconds(60),
            debounce.Token,
            () => UpdatePreviewWithCurrentSliders(
                debounce.Token,
                generation,
                promotable: false),
            timeProvider: _timeProvider);
    }

    private void ScheduleThumbnailRefresh()
    {
        var image = SelectedImage;
        if (image == null) return;
        var debounce = ReplaceDebounce(ref _thumbnailDebounce);
        _ = DebouncedAction.RunAsync(
            "thumbnail refresh",
            TimeSpan.FromMilliseconds(500),
            debounce.Token,
            () => TrackDirectThumbnailOperation(
                RefreshSelectedThumbnailAsync(image, debounce.Token)),
            timeProvider: _timeProvider);
    }

    private void PushUndoState()
    {
        if (SelectedImage == null) return;

        _history.PushEdit(SelectedImage.EditSettings.Clone());
        SyncHistoryFlags();
    }

    private void SyncHistoryFlags()
    {
        CanUndo = _history.CanUndo;
        CanRedo = _history.CanRedo;
    }

    private async Task AutoSaveAsync()
    {
        if (SelectedImage == null) return;

        SaveSlidersTo(SelectedImage.EditSettings);
        SelectedImage.HasEdits = SelectedImage.EditSettings.HasEdits;

        await SaveEditSettingsAsync(SelectedImage);
        _lastSavedState = SelectedImage.EditSettings.Clone();
    }

    private async Task SaveEditSettingsAsync(ImageFile imageFile)
    {
        await SaveEditSettingsAsync(imageFile, imageFile.EditSettings);
    }

    private async Task SaveEditSettingsAsync(
        ImageFile imageFile,
        EditSettings settings)
    {
        await imageFile.EnsureCatalogIdAsync(_catalogService);
        await _catalogService.SaveEditSettingsAsync(imageFile.CatalogId, settings);
    }

}
