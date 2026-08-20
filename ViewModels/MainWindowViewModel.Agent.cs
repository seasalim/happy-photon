using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _isAgentServerEnabled;

    private readonly SemaphoreSlim _agentToggleGate = new(1, 1);
    private string? _agentToken;
    private McpServerHost? _agentHost;
    private AgentToolService? _agentTools;
    private bool _isApplyingAgentSettings;

    public Func<Task>? PersistAppSettingsAsync { get; set; }
    public Func<string, Task>? CopyToClipboardAsync { get; set; }

    public string? AgentToken => _agentToken;
    public string AgentServerLabel => IsAgentServerEnabled ? "Agent ●" : "Agent";
    public string AgentServerTooltip => IsAgentServerEnabled
        ? $"Listening on 127.0.0.1:{McpServerHost.Port}; toggle off to stop"
        : "Start the local agent server";

    public void InitializeAgentSettings(bool enabled, string? token)
    {
        _agentToken = AgentAccessToken.IsValid(token) ? token : null;
        if (!enabled) return;

        _isApplyingAgentSettings = true;
        IsAgentServerEnabled = true;
        _isApplyingAgentSettings = false;
    }

    partial void OnIsAgentServerEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(AgentServerLabel));
        OnPropertyChanged(nameof(AgentServerTooltip));
        _ = HandleAgentToggleAsync(value, _isApplyingAgentSettings);
    }

    private async Task HandleAgentToggleAsync(bool enable, bool suppressSideEffects)
    {
        await _agentToggleGate.WaitAsync();
        try
        {
            if (enable)
            {
                await StartAgentServerAsync(suppressSideEffects);
            }
            else
            {
                if (_agentHost != null) await _agentHost.StopAsync();
                if (!suppressSideEffects)
                {
                    ShowTransientStatus("Agent server off");
                    await PersistAgentSettingsSafelyAsync();
                }
            }
        }
        catch (Exception ex)
        {
            ShowTransientStatus($"Agent server failed: {ex.Message}");
            _isApplyingAgentSettings = true;
            IsAgentServerEnabled = false;
            _isApplyingAgentSettings = false;
            await PersistAgentSettingsSafelyAsync();
        }
        finally
        {
            _agentToggleGate.Release();
        }
    }

    private async Task StartAgentServerAsync(bool suppressSideEffects)
    {
        _agentToken ??= AgentAccessToken.Generate();
        _agentTools ??= new AgentToolService(this, ImageService, _catalogService);
        _agentHost ??= new McpServerHost();
        await _agentHost.StartAsync(_agentTools, _agentToken);

        if (suppressSideEffects) return;

        var copied = false;
        if (CopyToClipboardAsync != null)
        {
            try
            {
                await CopyToClipboardAsync(_agentHost.GetUrl(_agentToken));
                copied = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Agent URL clipboard copy failed: {ex.Message}");
            }
        }

        ShowTransientStatus(copied
            ? "Agent server on — URL copied to clipboard"
            : "Agent server on — URL copy unavailable");
        await PersistAgentSettingsSafelyAsync();
    }

    private async Task PersistAgentSettingsSafelyAsync()
    {
        if (PersistAppSettingsAsync == null) return;
        try
        {
            await PersistAppSettingsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Agent settings persistence failed: {ex.Message}");
        }
    }

    public async Task ShutdownAgentServerAsync()
    {
        await _agentToggleGate.WaitAsync();
        try
        {
            if (_agentHost != null) await _agentHost.StopAsync();
        }
        finally
        {
            _agentToggleGate.Release();
        }
    }

    // Agent tool callers marshal these operations to the UI thread.
    internal async Task<List<AgentBatchFailure>> SetRatingForImagesAsync(
        IReadOnlyList<ImageFile> images,
        int rating)
    {
        var failed = new List<AgentBatchFailure>();
        rating = Math.Clamp(rating, 0, 5);

        foreach (var image in images)
        {
            if (image.Rating == rating) continue;

            var originalRating = image.Rating;
            try
            {
                image.Rating = rating;
                await image.EnsureCatalogIdAsync(_catalogService);
                await CommitAssessmentAsync(
                    [new AssessmentMutation(
                        image.CatalogId, AssessmentAxes.Rating,
                        Rating: image.Rating)]);
            }
            catch (Exception ex)
            {
                image.Rating = originalRating;
                System.Diagnostics.Debug.WriteLine(
                    $"Agent rating update failed for {image.FilePath}: {ex.Message}");
                failed.Add(new AgentBatchFailure(image.FilePath, ex.Message));
            }
        }

        Library.RefreshFilters();
        UpdateSelectedCount();
        return failed;
    }

    internal async Task<List<AgentBatchFailure>> SetFlagForImagesAsync(
        IReadOnlyList<ImageFile> images,
        ImageFlag flag)
    {
        var failed = new List<AgentBatchFailure>();

        foreach (var image in images)
        {
            if (image.Flag == flag) continue;

            var originalFlag = image.Flag;
            try
            {
                image.Flag = flag;
                await image.EnsureCatalogIdAsync(_catalogService);
                await CommitAssessmentAsync(
                    [new AssessmentMutation(
                        image.CatalogId, AssessmentAxes.Flag,
                        Flag: image.Flag)]);
            }
            catch (Exception ex)
            {
                image.Flag = originalFlag;
                System.Diagnostics.Debug.WriteLine(
                    $"Agent flag update failed for {image.FilePath}: {ex.Message}");
                failed.Add(new AgentBatchFailure(image.FilePath, ex.Message));
            }
        }

        Library.RefreshFilters();
        UpdateSelectedCount();
        return failed;
    }

    internal async Task<List<AgentBatchFailure>> SetColorLabelForImagesAsync(
        IReadOnlyList<ImageFile> images,
        ColorLabel colorLabel)
    {
        if (images.Count == 0) return [];

        try
        {
            foreach (var image in images)
            {
                await image.EnsureCatalogIdAsync(_catalogService);
            }

            await CommitAssessmentAsync(images.Select(image =>
                new AssessmentMutation(
                    image.CatalogId, AssessmentAxes.Label,
                    ColorLabel: colorLabel)).ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Agent color label update failed: {ex.Message}");
            return images.Select(image =>
                new AgentBatchFailure(image.FilePath, ex.Message)).ToList();
        }

        foreach (var image in images)
        {
            image.ColorLabel = colorLabel;
        }

        Library.RefreshFilters();
        UpdateSelectedCount();
        return [];
    }

    internal async Task<List<AgentBatchFailure>> ApplyColorSettingsToImagesAsync(
        IReadOnlyList<ImageFile> images,
        EditSettings source,
        string? appliedPresetId)
    {
        var copied = EditSettingsTransfer.CopySubset(source);
        copied.AppliedPresetId = appliedPresetId;
        return await ApplySettingsToImagesAsync(
            images,
            settings => EditSettingsTransfer.ApplySubset(copied, settings));
    }

    internal Task<List<AgentBatchFailure>> ApplyAgentEditSettingsToImagesAsync(
        IReadOnlyList<ImageFile> images,
        AgentEditSettingsPatch patch) =>
        ApplySettingsToImagesAsync(images, patch.ApplyTo);

    private async Task<List<AgentBatchFailure>> ApplySettingsToImagesAsync(
        IReadOnlyList<ImageFile> images,
        Action<EditSettings> apply)
    {
        var failed = new List<AgentBatchFailure>();
        var succeeded = new List<(ImageFile Image, EditSettings Previous)>(
            images.Count);

        foreach (var image in images)
        {
            var originalSettings = image.EditSettings.Clone();
            try
            {
                apply(image.EditSettings);
                image.HasEdits = image.EditSettings.HasEdits;
                await SaveEditSettingsAsync(image);
                succeeded.Add((image, originalSettings));
            }
            catch (Exception ex)
            {
                image.EditSettings = originalSettings;
                image.HasEdits = originalSettings.HasEdits;
                System.Diagnostics.Debug.WriteLine(
                    $"Agent edit update failed for {image.FilePath}: {ex.Message}");
                failed.Add(new AgentBatchFailure(image.FilePath, ex.Message));
            }
        }

        if (SelectedImage != null &&
            succeeded.Any(change => ReferenceEquals(
                change.Image,
                SelectedImage)))
        {
            _history.Clear();
            SyncHistoryFlags();

            _isLoadingImage = true;
            try
            {
                LoadSlidersFrom(SelectedImage.EditSettings);
            }
            finally
            {
                _isLoadingImage = false;
            }

            _lastSavedState = SelectedImage.EditSettings.Clone();
            if (IsDevelopMode || IsFullScreenMode)
            {
                await UpdatePreviewWithCurrentSliders();
            }
            UpdateCanReset();
        }

        Library.RefreshFilters();
        UpdateSelectedCount();
        _ = RefreshThumbnailsAsync(succeeded);
        return failed;
    }
}
