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
    private readonly IFileOperationService _fileOperationService;
    private readonly ISourceAvailabilityService _sourceAvailabilityService;
    private readonly UiBitmapRetirement _bitmapRetirement = new();
    private readonly TimeProvider _timeProvider;

    // Set by the window: releases a held loupe peek on any viewer surface and
    // reports whether one was active, so the Escape ladder can rank it first.
    public Func<bool>? CancelActiveLoupePeek { get; set; }
    public Func<Task>? PersistAppSettingsAsync { get; set; }
    public Func<string, Task>? CopyToClipboardAsync { get; set; }

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
        TimeProvider? timeProvider = null,
        IFileOperationService? fileOperationService = null,
        Func<long, Task<bool>>? deleteCatalogVersionAsync = null)
    {
        _catalogService = catalogService;
        _deleteCatalogVersionAsync =
            deleteCatalogVersionAsync ?? catalogService.DeleteVersionAsync;
        _fileOperationService = fileOperationService ?? new FileOperationService();
        _sourceAvailabilityService =
            availabilityService ?? new SourceAvailabilityService();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _rawRuntimeHealth = rawRuntimeHealth;
        _updateCheckService = updateCheckService ?? new UpdateCheckService();
        _updateInstallChannel = updateInstallChannel ?? UpdateChannelSelector.Current;
        _exportActivities = new BackgroundExportActivityRegistry(
            SignalBackgroundActivityStarted);
        Browse = new BrowseImageState(RetireThumbnail);
        ConfigureCapturePairs();
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
                _sourceAvailabilityService,
                health);
            service.Previews.PreviewRefreshed += OnPreviewRefreshed;
            service.Previews.PreviewLoadCompleted += OnPreviewLoadCompleted;
            service.Previews.BaseRefreshStateChanged +=
                OnBaseRefreshStateChanged;
            service.Previews.RenderedThumbnailWorkStarted +=
                OnRenderedThumbnailWorkStarted;
            service.Previews.AdjacentWarmWorkStarted +=
                OnAdjacentWarmWorkStarted;
            return service;
        });
        _loadMetadataAsync = loadMetadataAsync ??
            (image => ImageService.Metadata.LoadAsync(image));
        _postSelection = postSelection ??
            (action => Dispatcher.UIThread.Post(
                action,
                DispatcherPriority.Background));
        PresetService = new PresetService();
        Browse.FilterChanged += OnBrowseFilterChanged;
        Browse.StateChanged += OnBrowseStateChanged;
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

    [ObservableProperty]
    private bool _isFullScreenMode;

    public BrowseImageState Browse { get; }

    [ObservableProperty]
    private ImageFile? _selectedImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExportProofCaptionVisible))]
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

    public string? ActiveFileName => SelectedImage is { } image
        ? $"{image.FileName} · {image.VersionDisplayLabel}"
        : null;

    public ExportSettings ExportSettings { get; } = new();

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

    // Callback for delete confirmation dialog
    internal Func<DeleteConfirmationRequest, Task<bool>>? ConfirmDeleteAsync { get; set; }

    // Callback for rejected-image delete confirmation dialog
    public Func<int, int, string?, Task<bool>>?
        ConfirmDeleteRejectedAsync { get; set; }

    // Callback for file-operation failure summaries
    public Func<IReadOnlyList<FileOperationFailure>, Task>?
        ShowFileOperationFailuresAsync { get; set; }

    // Debouncing for live preview. The pending task is awaited at disposal:
    // its action autosaves through the catalog, and a fire-and-forget write
    // races catalog disposal once cancellation can no longer stop it. Both
    // fields are UI-thread-owned; scheduling and disposal never race them.
    private CancellationTokenSource? _previewDebounce;
    private Task? _previewDebounceTask;
    internal event Action? PreviewDebounceDrainStarted;
    internal event Action? PreviewDebounceDrainCompleted;
    private CancellationTokenSource? _histogramDebounce;
    private bool _isLoadingImage;

    // Undo/redo history for edit state
    private readonly EditHistory _history = new();
    private EditSettings? _lastSavedState;
    private Task? _pendingHistoryLoad;
    private Task? _pendingHistoryCommit;
    private Task? _serializedHistoryCommit;
    private ImageFile? _serializedHistoryImage;
    private EditSettings? _serializedHistorySettings;
    private long _historySubjectGeneration;

    public IReadOnlyList<EditHistoryEntry> HistoryEntries =>
        _history.Entries.Reverse().ToArray();

    [ObservableProperty]
    private bool _isHistoryLoaded;

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
        ToggleBeforeAfterSplitCommand.NotifyCanExecuteChanged();
        NotifyClippingCommandState();
    }
    partial void OnCanResetChanged(bool value)
    {
        ResetEditsCommand.NotifyCanExecuteChanged();
        ToggleBeforeAfterCommand.NotifyCanExecuteChanged();
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

    private void SchedulePreviewUpdate(string? historyLabel = null)
    {
        if (_isLoadingImage || !CanEditSelectedImage ||
            _renderOutcomeChannelClosed)
        {
            return;
        }

        _thumbnailDebounce?.Cancel();
        var generation = RequestEditedRender();

        var debounce = ReplaceDebounce(ref _previewDebounce);
        TrackPreviewDebounce(DebouncedAction.RunAsync(
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
            if (IsSliderEditActive) return;
            await AutoSaveAsync(historyLabel);
            debounce.Token.ThrowIfCancellationRequested();
            ScheduleThumbnailRefresh();
        },
            timeProvider: _timeProvider));
    }

    private void ScheduleCropPreviewUpdate()
    {
        if (_isLoadingImage || SelectedImage == null ||
            _renderOutcomeChannelClosed)
        {
            return;
        }

        CancelAdjacentPreviewWarm(
            invalidateWorker: true,
            dropRetained: true,
            imageFile: SelectedImage);
        var generation = ReserveRenderOutcome(
            PreviewSurfaceIntent.Edited,
            promotionEligible: false);
        var debounce = ReplaceDebounce(ref _previewDebounce);
        TrackPreviewDebounce(DebouncedAction.RunAsync(
            "crop preview update",
            TimeSpan.FromMilliseconds(60),
            debounce.Token,
            () => UpdatePreviewWithCurrentSliders(
                debounce.Token,
                generation,
                promotable: false),
            timeProvider: _timeProvider));
    }

    internal Task? PendingPreviewDebounceTask => _previewDebounceTask;

    internal Task? PendingHistoryLoadTask => _pendingHistoryLoad;

    internal Task? PendingHistoryCommitTask => _pendingHistoryCommit;

    private void TrackPreviewDebounce(Task run)
    {
        // A superseded debounce can already be past its cancellation checks
        // inside the autosave, so disposal must drain every unfinished task,
        // not just the newest one.
        var pending = _previewDebounceTask;
        _previewDebounceTask = pending is { IsCompleted: false }
            ? Task.WhenAll(pending, run)
            : run;
    }

    private void SyncHistoryFlags()
    {
        CancelHistoryHover();
        CanUndo = IsHistoryLoaded && _history.CanUndo;
        CanRedo = IsHistoryLoaded && _history.CanRedo;
        OnPropertyChanged(nameof(HistoryEntries));
        ClearHistoryCommand.NotifyCanExecuteChanged();
        ClearHistoryAboveStepCommand.NotifyCanExecuteChanged();
        JumpToHistoryStepCommand.NotifyCanExecuteChanged();
    }

    private async Task AutoSaveAsync(
        string? historyLabel = null,
        EditSettings? before = null)
    {
        var image = SelectedImage;
        if (image == null) return;

        SaveSlidersTo(image.EditSettings);
        image.HasEdits = image.EditSettings.HasEdits;

        await SaveEditSettingsAsync(image, historyLabel, before);
        if (ReferenceEquals(image, SelectedImage))
            _lastSavedState = image.EditSettings.Clone();
    }

    private async Task SaveEditSettingsAsync(ImageFile imageFile)
    {
        await SaveEditSettingsAsync(imageFile, null, null);
    }

    private async Task SaveEditSettingsAsync(
        ImageFile imageFile,
        EditSettings settings,
        bool recordHistory = true)
    {
        await SaveEditSettingsCoreAsync(
            imageFile, settings, null, null, recordHistory);
    }

    private Task SaveEditSettingsAsync(
        ImageFile imageFile,
        string? historyLabel,
        EditSettings? before = null) =>
        SaveEditSettingsCoreAsync(
            imageFile, imageFile.EditSettings, historyLabel, before, true);

    private Task SaveEditSettingsCoreAsync(
        ImageFile imageFile,
        EditSettings settings,
        string? historyLabel,
        EditSettings? before,
        bool recordHistory,
        Func<Task<bool>>? beforeSave = null)
    {
        var settingsSnapshot = settings.Clone();
        var tracksHistory = recordHistory && IsDevelopMode &&
                            ReferenceEquals(imageFile, SelectedImage);
        var historyGeneration = Volatile.Read(ref _historySubjectGeneration);
        var predecessor = tracksHistory ? _serializedHistoryCommit : null;
        var predecessorSettings = predecessor is { IsCompleted: false } &&
                                  ReferenceEquals(
                                      imageFile, _serializedHistoryImage)
            ? _serializedHistorySettings
            : null;
        var beforeSnapshot = (before ?? predecessorSettings ??
                              _lastSavedState ?? settings).Clone();
        var load = tracksHistory ? _pendingHistoryLoad : null;
        var save = SaveEditSettingsOperationAsync(
            imageFile, settingsSnapshot, historyLabel, beforeSnapshot,
            tracksHistory, historyGeneration, predecessor, load, beforeSave);
        if (tracksHistory)
        {
            _serializedHistoryCommit = save;
            _serializedHistoryImage = imageFile;
            _serializedHistorySettings = settingsSnapshot;
            TrackHistoryCommit(save);
        }
        return save;
    }

    private async Task SaveEditSettingsOperationAsync(
        ImageFile imageFile,
        EditSettings settings,
        string? historyLabel,
        EditSettings before,
        bool tracksHistory,
        long historyGeneration,
        Task? predecessor,
        Task? load,
        Func<Task<bool>>? beforeSave = null)
    {
        if (beforeSave != null && !await beforeSave()) return;
        await imageFile.EnsureCatalogIdAsync(_catalogService);
        if (predecessor != null)
            await ObservePendingHistoryWorkAsync(predecessor);
        if (load != null)
            await load;

        var mutation = tracksHistory &&
                       IsCurrentHistorySubject(imageFile, historyGeneration) &&
                       IsHistoryLoaded
            ? _history.PrepareAppend(
                before,
                settings,
                historyLabel)
            : null;
        await _catalogService.SaveEditSettingsWithHistoryAsync(
            imageFile.CatalogId, settings, mutation);
        if (ReferenceEquals(imageFile, SelectedImage) &&
            (!tracksHistory ||
             IsCurrentHistorySubject(imageFile, historyGeneration)))
            _lastSavedState = settings.Clone();
        if (mutation != null &&
            IsCurrentHistorySubject(imageFile, historyGeneration))
        {
            _history.Publish(mutation);
            SyncHistoryFlags();
        }
    }

    private void TrackHistoryCommit(Task commit)
    {
        var pending = _pendingHistoryCommit;
        _pendingHistoryCommit = pending is { IsCompleted: false }
            ? Task.WhenAll(pending, commit)
            : commit;
    }

}
