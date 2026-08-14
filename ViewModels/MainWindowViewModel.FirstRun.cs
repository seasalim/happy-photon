using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public enum StartupGateState
{
    Initializing,
    PointerRecovery,
    Welcome,
    Ready,
    Error
}

public enum FirstRunStep
{
    Welcome,
    Storage,
    Pictures,
    Lightroom,
    AllSet
}

internal enum FirstRunStartupDecision
{
    Restore,
    GrandfatherExistingInstallation,
    ShowWelcome
}

public partial class MainWindowViewModel
{
    public const int CurrentFirstRunExperienceVersion = 1;

    [ObservableProperty]
    private StartupGateState _startupGateState = StartupGateState.Initializing;

    [ObservableProperty]
    private FirstRunStep _firstRunStep;

    [ObservableProperty]
    private string? _firstRunDefaultPath;

    [ObservableProperty]
    private string? _firstRunPicturesPath;

    [ObservableProperty]
    private string? _detectedLightroomCatalogPath;

    [ObservableProperty]
    private IReadOnlyList<string> _detectedLightroomCatalogPaths = [];

    [ObservableProperty]
    private string? _firstRunErrorMessage;

    [ObservableProperty]
    private bool _isFirstRunBusy;

    [ObservableProperty]
    private bool _isFirstRunStorageCommitted;

    [ObservableProperty]
    private int? _firstRunExperienceVersion;

    [ObservableProperty]
    private bool _canPersistFolderSession;

    public Func<string, Task>? PersistFirstRunCompletionAsync { get; set; }
    public Func<string, CancellationToken, Task<LightroomDetectionResult>>?
        DetectLightroomAsync { get; set; }
    public Func<string?, Task<bool>>? RequestFirstRunCatalogImportAsync { get; set; }
    public Func<Task<string?>>? RequestFirstRunCatalogPathAsync { get; set; }
    public Action? BrowseLocationRequested { get; set; }
    public Func<Task>? RetryStartupAsync { get; set; }
    public Action? CloseApplicationRequested { get; set; }
    public Action? RequestFolderTreeFocus { get; set; }

    public bool IsStartupGateVisible => StartupGateState != StartupGateState.Ready;
    public bool IsStartupInitializing => StartupGateState == StartupGateState.Initializing;
    public bool IsFirstRunVisible => StartupGateState == StartupGateState.Welcome;
    public bool IsStartupError => StartupGateState == StartupGateState.Error;
    public bool IsFirstRunWelcomeStep =>
        IsFirstRunVisible && FirstRunStep == FirstRunStep.Welcome;
    public bool IsFirstRunStorageStep =>
        IsFirstRunVisible && FirstRunStep == FirstRunStep.Storage;
    public bool IsFirstRunPicturesStep =>
        IsFirstRunVisible && FirstRunStep == FirstRunStep.Pictures;
    public bool IsFirstRunLightroomStep =>
        IsFirstRunVisible && FirstRunStep == FirstRunStep.Lightroom;
    public bool IsFirstRunAllSetStep =>
        IsFirstRunVisible && FirstRunStep == FirstRunStep.AllSet;
    public bool IsPickerLedFirstRun =>
        IsFirstRunPicturesStep && FirstRunDefaultPath == null;
    public bool HasDefaultFirstRunLocation =>
        IsFirstRunPicturesStep && FirstRunDefaultPath != null;
    public bool IsWorkspaceInteractionEnabled => StartupGateState == StartupGateState.Ready;
    public bool HasDetectedLightroomCatalogs =>
        DetectedLightroomCatalogPaths.Count > 0;

    internal static FirstRunStartupDecision DecideFirstRunStartup(
        Models.AppSettings settings)
    {
        if (settings.FirstRunExperienceVersion >= CurrentFirstRunExperienceVersion)
        {
            return FirstRunStartupDecision.Restore;
        }

        return settings.FirstRunExperienceVersion == null &&
               !string.IsNullOrWhiteSpace(settings.RootFolderPath)
            ? FirstRunStartupDecision.GrandfatherExistingInstallation
            : FirstRunStartupDecision.ShowWelcome;
    }

    public void ShowInitializing()
    {
        FirstRunErrorMessage = null;
        StartupGateState = StartupGateState.Initializing;
    }

    public void ShowFirstRunWelcome(string? defaultPath)
    {
        SetFirstRunDefaultLocation(defaultPath);
        FirstRunPicturesPath = null;
        DetectedLightroomCatalogPath = null;
        DetectedLightroomCatalogPaths = [];
        FirstRunErrorMessage = null;
        CanPersistFolderSession = false;
        FirstRunStep = FirstRunStep.Welcome;
        StartupGateState = StartupGateState.Welcome;
    }

    public void ResumeFirstRunAfterStorage(string? defaultPath)
    {
        SetFirstRunDefaultLocation(defaultPath);
        FirstRunErrorMessage = null;
        CanPersistFolderSession = false;
        IsFirstRunStorageCommitted = true;
        FirstRunStep = FirstRunStep.Pictures;
        StartupGateState = StartupGateState.Welcome;
    }

    public void MarkFirstRunStorageCommitted()
    {
        IsFirstRunStorageCommitted = true;
        IsFirstRunStorageReadOnly = true;
    }

    public void ShowStartupFailure(string message)
    {
        FirstRunErrorMessage = message;
        CanPersistFolderSession = false;
        StartupGateState = StartupGateState.Error;
    }

    public void ShowWorkspaceReady(int version)
    {
        FirstRunExperienceVersion = version;
        CanPersistFolderSession = true;
        FirstRunErrorMessage = null;
        StartupGateState = StartupGateState.Ready;
    }

    public void SetFirstRunError(string message) => FirstRunErrorMessage = message;

    [RelayCommand(CanExecute = nameof(CanContinueFirstRun))]
    private async Task ContinueFirstRunAsync()
    {
        if (IsFirstRunBusy || !IsFirstRunVisible) return;
        FirstRunErrorMessage = null;

        switch (FirstRunStep)
        {
            case FirstRunStep.Welcome:
                FirstRunStep = FirstRunStep.Storage;
                break;
            case FirstRunStep.Storage:
                if (IsFirstRunStorageCommitted)
                    FirstRunStep = FirstRunStep.Pictures;
                else
                    await CompleteStorageSetupAsync();
                break;
            case FirstRunStep.Pictures when FirstRunDefaultPath != null:
                await CompleteFirstRunFromLocationAsync(FirstRunDefaultPath);
                break;
            case FirstRunStep.Pictures:
                BrowseElsewhere();
                break;
        }
    }

    private bool CanContinueFirstRun() =>
        IsFirstRunVisible && !IsFirstRunBusy &&
        !IsFirstRunLightroomStep && !IsFirstRunAllSetStep;

    public async Task CompleteFirstRunFromLocationAsync(string path)
    {
        if (IsFirstRunBusy || !IsFirstRunVisible) return;

        var validation = ValidateBrowseLocation(path);
        if (validation == BrowseLocationValidation.Catalog)
        {
            FirstRunErrorMessage =
                "Choose a folder outside the Happy Photon catalog. It contains application data.";
            return;
        }

        if (validation != BrowseLocationValidation.Valid)
        {
            FirstRunErrorMessage =
                "Happy Photon couldn't open that location. Choose another folder and try again.";
            return;
        }

        IsFirstRunBusy = true;
        FirstRunErrorMessage = null;
        try
        {
            FirstRunPicturesPath = path;
            SetFirstRunDefaultLocation(path);
            var detection = DetectLightroomAsync == null
                ? LightroomDetectionResult.NotDetected
                : await DetectLightroomAsync(path, CancellationToken.None);
            if (detection.IsDetected)
            {
                DetectedLightroomCatalogPaths = detection.CatalogPaths;
                DetectedLightroomCatalogPath = detection.CatalogPaths.FirstOrDefault();
                FirstRunStep = FirstRunStep.Lightroom;
                return;
            }

            FirstRunStep = FirstRunStep.AllSet;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"First-run completion failed: {exception}");
            FirstRunErrorMessage =
                "Happy Photon couldn't save this location. Please try again.";
        }
        finally
        {
            IsFirstRunBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanImportDetectedLightroom))]
    private async Task ImportDetectedLightroomAsync()
    {
        if (IsFirstRunBusy || !IsFirstRunLightroomStep ||
            RequestFirstRunCatalogImportAsync == null)
        {
            return;
        }

        IsFirstRunBusy = true;
        FirstRunErrorMessage = null;
        try
        {
            if (await RequestFirstRunCatalogImportAsync(DetectedLightroomCatalogPath))
                FirstRunStep = FirstRunStep.AllSet;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"First-run import failed: {exception}");
            FirstRunErrorMessage =
                "Happy Photon couldn't finish the Lightroom import. Please try again or skip it.";
        }
        finally
        {
            IsFirstRunBusy = false;
        }
    }

    private bool CanImportDetectedLightroom() =>
        IsFirstRunLightroomStep && !IsFirstRunBusy &&
        RequestFirstRunCatalogImportAsync != null;

    [RelayCommand(CanExecute = nameof(CanChooseAnotherLightroomCatalog))]
    private async Task ChooseAnotherLightroomCatalogAsync()
    {
        if (RequestFirstRunCatalogPathAsync == null || IsFirstRunBusy) return;
        IsFirstRunBusy = true;
        FirstRunErrorMessage = null;
        try
        {
            var path = await RequestFirstRunCatalogPathAsync();
            if (string.IsNullOrWhiteSpace(path)) return;

            if (!DetectedLightroomCatalogPaths.Contains(
                    path,
                    StringComparer.OrdinalIgnoreCase))
            {
                DetectedLightroomCatalogPaths = [.. DetectedLightroomCatalogPaths, path];
            }
            DetectedLightroomCatalogPath = path;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Lightroom catalog selection failed: {exception}");
            FirstRunErrorMessage =
                "Happy Photon couldn't open the catalog picker. Please try again.";
        }
        finally
        {
            IsFirstRunBusy = false;
        }
    }

    private bool CanChooseAnotherLightroomCatalog() =>
        IsFirstRunLightroomStep && !IsFirstRunBusy &&
        RequestFirstRunCatalogPathAsync != null;

    [RelayCommand(CanExecute = nameof(CanSkipDetectedLightroom))]
    private void SkipDetectedLightroom()
    {
        if (IsFirstRunBusy || !IsFirstRunLightroomStep) return;
        FirstRunErrorMessage = null;
        FirstRunStep = FirstRunStep.AllSet;
    }

    private bool CanSkipDetectedLightroom() =>
        IsFirstRunLightroomStep && !IsFirstRunBusy;

    [RelayCommand(CanExecute = nameof(CanFinishFirstRun))]
    private Task StartFirstRunTourAsync() => CompleteFirstRunAsync(startTour: true);

    [RelayCommand(CanExecute = nameof(CanFinishFirstRun))]
    private Task SkipFirstRunTourAsync() => CompleteFirstRunAsync(startTour: false);

    private bool CanFinishFirstRun() => IsFirstRunAllSetStep && !IsFirstRunBusy;

    private async Task CompleteFirstRunAsync(bool startTour)
    {
        if (IsFirstRunBusy || !IsFirstRunAllSetStep) return;
        IsFirstRunBusy = true;
        FirstRunErrorMessage = null;
        try
        {
            await FinishFirstRunAsync(startTour);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"First-run completion failed: {exception}");
            FirstRunErrorMessage =
                "Happy Photon couldn't save this location. Please try again.";
        }
        finally
        {
            IsFirstRunBusy = false;
        }
    }

    private async Task FinishFirstRunAsync(bool startTour)
    {
        if (string.IsNullOrWhiteSpace(FirstRunPicturesPath) ||
            PersistFirstRunCompletionAsync == null)
        {
            throw new InvalidOperationException(
                "Happy Photon couldn't save this location. Please try again.");
        }

        await PersistFirstRunCompletionAsync(FirstRunPicturesPath);
        ShowWorkspaceReady(CurrentFirstRunExperienceVersion);
        if (startTour) StartWorkflowTour();
        RequestFolderTreeFocus?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanStartInDefaultLocation))]
    private Task StartInDefaultLocationAsync() =>
        FirstRunDefaultPath == null
            ? Task.CompletedTask
            : CompleteFirstRunFromLocationAsync(FirstRunDefaultPath);

    private bool CanStartInDefaultLocation() =>
        HasDefaultFirstRunLocation && !IsFirstRunBusy;

    [RelayCommand(CanExecute = nameof(CanBrowseElsewhere))]
    private void BrowseElsewhere()
    {
        FirstRunErrorMessage = null;
        BrowseLocationRequested?.Invoke();
    }

    private bool CanBrowseElsewhere() =>
        IsFirstRunPicturesStep && !IsFirstRunBusy;

    [RelayCommand(CanExecute = nameof(CanRetryStartup))]
    private Task RetryStartup() => RetryStartupAsync?.Invoke() ?? Task.CompletedTask;

    private bool CanRetryStartup() => IsStartupError && RetryStartupAsync != null;

    [RelayCommand]
    private void CloseApplication() => CloseApplicationRequested?.Invoke();

    partial void OnStartupGateStateChanged(StartupGateState value)
    {
        NotifyFirstRunPresentationChanged();
        OnPropertyChanged(nameof(IsStartupGateVisible));
        OnPropertyChanged(nameof(IsStartupInitializing));
        OnPropertyChanged(nameof(IsFirstRunVisible));
        OnPropertyChanged(nameof(IsStartupError));
        OnPropertyChanged(nameof(IsWorkspaceInteractionEnabled));
        OnPropertyChanged(nameof(IsPointerRecoveryVisible));
        OnPropertyChanged(nameof(CanSetAsideCatalog));
        RetryStartupCommand.NotifyCanExecuteChanged();
        SetAsideCatalogCommand.NotifyCanExecuteChanged();
    }

    partial void OnFirstRunStepChanged(FirstRunStep value)
    {
        FirstRunErrorMessage = null;
        NotifyFirstRunPresentationChanged();
        OnPropertyChanged(nameof(CanChangeFirstRunStorage));
        ChangeSetupCatalogCommand.NotifyCanExecuteChanged();
        ChangeSetupCacheCommand.NotifyCanExecuteChanged();
    }

    partial void OnFirstRunDefaultPathChanged(string? value) =>
        NotifyFirstRunPresentationChanged();

    partial void OnDetectedLightroomCatalogPathsChanged(IReadOnlyList<string> value) =>
        OnPropertyChanged(nameof(HasDetectedLightroomCatalogs));

    partial void OnIsFirstRunBusyChanged(bool value)
    {
        NotifyFirstRunCommandsChanged();
    }

    private void NotifyFirstRunPresentationChanged()
    {
        OnPropertyChanged(nameof(IsFirstRunWelcomeStep));
        OnPropertyChanged(nameof(IsFirstRunStorageStep));
        OnPropertyChanged(nameof(IsFirstRunPicturesStep));
        OnPropertyChanged(nameof(IsFirstRunLightroomStep));
        OnPropertyChanged(nameof(IsFirstRunAllSetStep));
        OnPropertyChanged(nameof(IsPickerLedFirstRun));
        OnPropertyChanged(nameof(HasDefaultFirstRunLocation));
        NotifyFirstRunCommandsChanged();
    }

    private void NotifyFirstRunCommandsChanged()
    {
        ContinueFirstRunCommand.NotifyCanExecuteChanged();
        StartInDefaultLocationCommand.NotifyCanExecuteChanged();
        BrowseElsewhereCommand.NotifyCanExecuteChanged();
        ImportDetectedLightroomCommand.NotifyCanExecuteChanged();
        SkipDetectedLightroomCommand.NotifyCanExecuteChanged();
        ChooseAnotherLightroomCatalogCommand.NotifyCanExecuteChanged();
        StartFirstRunTourCommand.NotifyCanExecuteChanged();
        SkipFirstRunTourCommand.NotifyCanExecuteChanged();
    }

    private void SetFirstRunDefaultLocation(string? path)
    {
        FirstRunDefaultPath = path;
    }
}
