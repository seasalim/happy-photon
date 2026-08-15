using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public sealed record CatalogImportFlowOperations(
    Func<string, CancellationToken, Task<LightroomCatalogContents>> ReadCatalogAsync,
    Func<string, CancellationToken, Task<CatalogImportStoredSettings?>> LoadSettingsAsync,
    Func<LightroomCatalogContents, IReadOnlyDictionary<string, string>,
        CatalogImportPolicy, CancellationToken, Task<CatalogImportPreview>> CreatePreviewAsync,
    Func<CatalogImportPreview, CancellationToken,
        Task<CatalogImportApplyResult>> ApplyAsync)
{
    public static CatalogImportFlowOperations From(MainWindowViewModel viewModel) =>
        new(
            viewModel.ReadLightroomCatalogAsync,
            (path, _) => viewModel.LoadCatalogImportSettingsAsync(path),
            viewModel.PreviewCatalogImportAsync,
            viewModel.ApplyCatalogImportAsync);
}

public sealed class CatalogImportFlowViewModel : ObservableObject, IDisposable
{
    private const string ReviewReadyStatus =
        "Review what will change, then apply when ready.";
    private readonly CatalogImportFlowOperations _operations;
    private readonly string _catalogPath;
    private readonly Dictionary<string, string> _rootMappings =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _editStarts =
        new(StringComparer.Ordinal);
    private CancellationTokenSource? _initializationCancellation;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _applyCancellation;
    private string _liveSignature = string.Empty;
    private string? _previewedSignature;
    private string? _runningPreviewSignature;
    private string? _previewStatusToPreserve;
    private CatalogImportPreview? _preview;
    private long _previewGeneration;
    private bool _isInitialized;
    private bool _isInitializing;
    private bool _isPreviewRunning;
    private bool _isApplying;
    private bool _isApplied;
    private bool _applySucceeded;
    private string _statusText = string.Empty;
    private string? _failureText;
    private CatalogImportReport? _report;

    public CatalogImportFlowViewModel(
        MainWindowViewModel viewModel,
        string catalogPath)
        : this(CatalogImportFlowOperations.From(viewModel), catalogPath)
    {
    }

    public CatalogImportFlowViewModel(
        CatalogImportFlowOperations operations,
        string catalogPath)
    {
        _operations = operations;
        _catalogPath = catalogPath;
        StatusText = "Creating a consistent, read-only snapshot…";
    }

    public event EventHandler? InputsReady;

    public LightroomCatalogContents? Source { get; private set; }
    public CatalogImportStoredSettings? StoredSettings { get; private set; }
    public IReadOnlyDictionary<string, string> RootMappings => _rootMappings;
    public CatalogImportPolicy Policy { get; private set; } =
        CatalogImportPolicy.LightroomWins;
    public bool IsReimport => StoredSettings != null;
    public bool IsInitialized => _isInitialized;
    public bool IsInitializing => _isInitializing;
    public bool IsPreviewRunning => _isPreviewRunning;
    public bool IsApplying => _isApplying;
    public bool IsApplied => _isApplied;
    public bool ApplySucceeded => _applySucceeded;
    public bool IsBusy => IsInitializing || IsPreviewRunning || IsApplying;
    public bool HasInFlightOperation => IsBusy;
    public bool InputsEnabled => IsInitialized && !IsApplying && !IsApplied;
    public bool CanApply => !IsBusy && !IsApplied && _preview != null &&
                            _previewedSignature == _liveSignature;
    public CatalogImportReport? Report => _report;
    public string? FailureText => _failureText;
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public async Task InitializeAsync()
    {
        if (IsInitialized || IsInitializing) return;
        _initializationCancellation = new CancellationTokenSource();
        SetInitializing(true);
        try
        {
            var token = _initializationCancellation.Token;
            Source = await _operations.ReadCatalogAsync(_catalogPath, token);
            token.ThrowIfCancellationRequested();
            StoredSettings = await _operations.LoadSettingsAsync(_catalogPath, token);
            token.ThrowIfCancellationRequested();
            InitializeInputs();
            _isInitialized = true;
            OnPropertyChanged(nameof(IsInitialized));
            OnPropertyChanged(nameof(InputsEnabled));
            InputsReady?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            ShowFailure("Catalog check canceled. No catalog changes were made.");
            return;
        }
        catch (Exception exception)
        {
            ShowFailure(exception.Message);
            return;
        }
        finally
        {
            _initializationCancellation.Dispose();
            _initializationCancellation = null;
            SetInitializing(false);
        }

        await RefreshPreviewAsync();
    }

    public void BeginRootEdit(string sourceRoot)
    {
        if (!_editStarts.ContainsKey(sourceRoot))
            _editStarts[sourceRoot] = GetRootMapping(sourceRoot);
    }

    public void UpdateRootText(string sourceRoot, string? text)
    {
        if (!InputsEnabled) return;
        var value = text ?? string.Empty;
        if (string.Equals(GetRootMapping(sourceRoot), value,
                StringComparison.Ordinal))
        {
            return;
        }

        _rootMappings[sourceRoot] = value;
        InputsChanged();
    }

    public async Task CommitRootEditAsync(string sourceRoot, string? text)
    {
        UpdateRootText(sourceRoot, text);
        var value = GetRootMapping(sourceRoot);
        var changed = !_editStarts.Remove(sourceRoot, out var start) ||
                      !string.Equals(start, value, StringComparison.Ordinal);
        if (changed) await CommitInputsAsync();
    }

    public async Task ChooseRootAsync(string sourceRoot, string path)
    {
        UpdateRootText(sourceRoot, path);
        await CommitInputsAsync();
    }

    public Task OverrideRootAsync(string sourceRoot) => CommitInputsAsync();

    public async Task SetPolicyAsync(CatalogImportPolicy policy)
    {
        if (!InputsEnabled || Policy == policy) return;
        Policy = policy;
        OnPropertyChanged(nameof(Policy));
        InputsChanged();
        await CommitInputsAsync();
    }

    public async Task CommitInputsAsync()
    {
        if (!InputsEnabled) return;
        if (_preview != null && _previewedSignature == _liveSignature)
        {
            InvalidateRunningPreview();
            _report = _preview.Report;
            _failureText = null;
            StatusText = ReviewReadyStatus;
            OnPropertyChanged(nameof(Report));
            OnPropertyChanged(nameof(FailureText));
            NotifyCanApply();
            return;
        }

        await RefreshPreviewAsync();
    }

    public async Task ApplyAsync()
    {
        if (!CanApply || _preview == null) return;
        var preview = _preview;
        _applyCancellation = new CancellationTokenSource();
        SetApplying(true);
        StatusText = "Applying all catalog changes in one transaction…";
        string? conflictText = null;
        try
        {
            var result = await _operations.ApplyAsync(
                preview, _applyCancellation.Token);
            _report = result.Report;
            _failureText = null;
            _isApplied = true;
            _applySucceeded = true;
            StatusText = result.DatabaseWrites == 0
                ? "Everything is already up to date. No catalog rows were changed."
                : "Import complete. Lightroom and your original photographs were not changed.";
            OnPropertyChanged(nameof(Report));
            OnPropertyChanged(nameof(FailureText));
            OnPropertyChanged(nameof(IsApplied));
            OnPropertyChanged(nameof(ApplySucceeded));
        }
        catch (OperationCanceledException)
        {
            StatusText = "Import canceled. No Happy Photon catalog changes were applied.";
        }
        catch (CatalogImportConflictException exception)
        {
            conflictText = exception.Message;
            _preview = null;
            _previewedSignature = null;
            ShowFailure(conflictText);
        }
        catch (Exception exception)
        {
            ShowFailure(exception.Message);
            _preview = null;
            _previewedSignature = null;
        }
        finally
        {
            _applyCancellation.Dispose();
            _applyCancellation = null;
            SetApplying(false);
        }

        if (conflictText != null)
            await RefreshPreviewAsync(conflictText);
    }

    public void CancelCurrentOperation()
    {
        if (_applyCancellation != null)
        {
            _applyCancellation.Cancel();
        }
        else if (_previewCancellation != null)
        {
            _previewGeneration++;
            _previewCancellation.Cancel();
            StatusText = _previewStatusToPreserve ??
                "Check canceled. No catalog changes were made.";
        }
        else
        {
            _initializationCancellation?.Cancel();
        }
    }

    public void Dispose()
    {
        CancelCurrentOperation();
        _applyCancellation?.Dispose();
        _previewCancellation?.Dispose();
        _initializationCancellation?.Dispose();
    }

    private void InitializeInputs()
    {
        if (Source == null) return;
        var roots = Source.Roots.Where(root => root.PhotoCount > 0).ToArray();
        var resolved = CatalogImportService.ResolveMappings(
            roots, StoredSettings?.RootMappings ??
                   new Dictionary<string, string>());
        foreach (var root in roots)
        {
            _rootMappings[root.SourcePath] = resolved.GetValueOrDefault(
                root.SourcePath, string.Empty);
        }

        Policy = StoredSettings?.Policies.Values.FirstOrDefault() ==
                 CatalogImportPolicy.FillEmptyOnly
            ? CatalogImportPolicy.FillEmptyOnly
            : CatalogImportPolicy.LightroomWins;
        _liveSignature = CreateSignature();
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(StoredSettings));
        OnPropertyChanged(nameof(RootMappings));
        OnPropertyChanged(nameof(Policy));
        OnPropertyChanged(nameof(IsReimport));
    }

    private async Task RefreshPreviewAsync(string? preservedStatus = null)
    {
        if (Source == null || IsApplying || IsApplied) return;
        InvalidateRunningPreview();
        var generation = ++_previewGeneration;
        var signature = _liveSignature;
        var mappings = _rootMappings
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value,
                StringComparer.Ordinal);
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        _runningPreviewSignature = signature;
        _previewStatusToPreserve = preservedStatus;
        _report = null;
        if (preservedStatus == null)
        {
            _failureText = null;
            StatusText = "Checking what will change…";
        }
        OnPropertyChanged(nameof(Report));
        OnPropertyChanged(nameof(FailureText));
        SetPreviewRunning(true);
        try
        {
            var preview = await _operations.CreatePreviewAsync(
                Source, mappings, Policy, cancellation.Token);
            if (generation != _previewGeneration ||
                signature != _liveSignature ||
                !ReferenceEquals(_previewCancellation, cancellation))
            {
                return;
            }

            _preview = preview;
            _previewedSignature = signature;
            _report = preview.Report;
            _failureText = null;
            StatusText = preservedStatus ?? ReviewReadyStatus;
            OnPropertyChanged(nameof(Report));
            OnPropertyChanged(nameof(FailureText));
        }
        catch (OperationCanceledException)
        {
            if (generation == _previewGeneration && signature == _liveSignature)
            {
                StatusText = preservedStatus ??
                    "Check canceled. No catalog changes were made.";
            }
        }
        catch (Exception exception)
        {
            if (generation == _previewGeneration && signature == _liveSignature)
            {
                var message = preservedStatus == null
                    ? exception.Message
                    : $"{preservedStatus} Automatic re-check failed: {exception.Message}";
                ShowFailure(message);
                _preview = null;
                _previewedSignature = null;
            }
        }
        finally
        {
            cancellation.Dispose();
            if (ReferenceEquals(_previewCancellation, cancellation))
            {
                _previewCancellation = null;
                _runningPreviewSignature = null;
                _previewStatusToPreserve = null;
                SetPreviewRunning(false);
            }
        }
    }

    private void InputsChanged()
    {
        _liveSignature = CreateSignature();
        if (IsPreviewRunning && _runningPreviewSignature != _liveSignature)
            InvalidateRunningPreview();
        if (_previewedSignature != _liveSignature)
        {
            _report = null;
            StatusText = "Finish editing to check what will change.";
            OnPropertyChanged(nameof(Report));
        }
        else if (_preview != null)
        {
            _report = _preview.Report;
            StatusText = ReviewReadyStatus;
            OnPropertyChanged(nameof(Report));
        }
        NotifyCanApply();
    }

    private void InvalidateRunningPreview()
    {
        if (_previewCancellation == null) return;
        _previewGeneration++;
        _previewCancellation.Cancel();
    }

    private string CreateSignature()
    {
        var parts = _rootMappings.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key.Length}:{pair.Key}{pair.Value.Length}:{pair.Value}");
        return $"{(int)Policy}|{string.Concat(parts)}";
    }

    private string GetRootMapping(string sourceRoot) =>
        _rootMappings.GetValueOrDefault(sourceRoot, string.Empty);

    private void ShowFailure(string message)
    {
        StatusText = message;
        _failureText = message;
        _report = null;
        OnPropertyChanged(nameof(FailureText));
        OnPropertyChanged(nameof(Report));
        NotifyCanApply();
    }

    private void SetInitializing(bool value)
    {
        if (_isInitializing == value) return;
        _isInitializing = value;
        NotifyOperationState(nameof(IsInitializing));
    }

    private void SetPreviewRunning(bool value)
    {
        if (_isPreviewRunning == value) return;
        _isPreviewRunning = value;
        NotifyOperationState(nameof(IsPreviewRunning));
    }

    private void SetApplying(bool value)
    {
        if (_isApplying == value) return;
        _isApplying = value;
        NotifyOperationState(nameof(IsApplying));
        OnPropertyChanged(nameof(InputsEnabled));
    }

    private void NotifyOperationState(string propertyName)
    {
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(HasInFlightOperation));
        NotifyCanApply();
    }

    private void NotifyCanApply() => OnPropertyChanged(nameof(CanApply));
}
