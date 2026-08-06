using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public enum StartupGateState
{
    Initializing,
    Welcome,
    Ready,
    Error
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
    private string? _firstRunDefaultPath;

    [ObservableProperty]
    private string _firstRunDefaultName = "Pictures";

    [ObservableProperty]
    private string? _firstRunErrorMessage;

    [ObservableProperty]
    private bool _isFirstRunBusy;

    [ObservableProperty]
    private int? _firstRunExperienceVersion;

    [ObservableProperty]
    private bool _canPersistFolderSession;

    public Func<string, Task>? PersistFirstRunCompletionAsync { get; set; }
    public Action? BrowseLocationRequested { get; set; }
    public Func<Task>? RetryStartupAsync { get; set; }
    public Action? CloseApplicationRequested { get; set; }
    public Action? RequestFolderTreeFocus { get; set; }

    public bool IsStartupGateVisible => StartupGateState != StartupGateState.Ready;
    public bool IsStartupInitializing => StartupGateState == StartupGateState.Initializing;
    public bool IsFirstRunVisible => StartupGateState == StartupGateState.Welcome;
    public bool IsStartupError => StartupGateState == StartupGateState.Error;
    public bool IsPickerLedFirstRun => IsFirstRunVisible && FirstRunDefaultPath == null;
    public bool HasDefaultFirstRunLocation => IsFirstRunVisible && FirstRunDefaultPath != null;
    public bool IsWorkspaceInteractionEnabled => StartupGateState == StartupGateState.Ready;
    public string FirstRunPrimaryActionText => $"START IN {FirstRunDefaultName.ToUpperInvariant()}";

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
        FirstRunDefaultPath = defaultPath;
        FirstRunDefaultName = GetFolderDisplayName(defaultPath) ?? "Pictures";
        FirstRunErrorMessage = null;
        CanPersistFolderSession = false;
        StartupGateState = StartupGateState.Welcome;
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

    public void SetFirstRunError(string message)
    {
        FirstRunErrorMessage = message;
    }

    public async Task CompleteFirstRunFromLocationAsync(string path)
    {
        if (IsFirstRunBusy || !IsFirstRunVisible)
        {
            return;
        }

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

        if (PersistFirstRunCompletionAsync == null)
        {
            FirstRunErrorMessage = "Happy Photon couldn't save this location. Please try again.";
            return;
        }

        IsFirstRunBusy = true;
        FirstRunErrorMessage = null;
        try
        {
            await PersistFirstRunCompletionAsync(path);
            ShowWorkspaceReady(CurrentFirstRunExperienceVersion);
            StartWorkflowTour();
            RequestFolderTreeFocus?.Invoke();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"First-run completion failed: {exception}");
            FirstRunErrorMessage =
                "Happy Photon couldn't save this location. Please try again.";
        }
        finally
        {
            IsFirstRunBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartInDefaultLocation))]
    private Task StartInDefaultLocationAsync()
    {
        return FirstRunDefaultPath == null
            ? Task.CompletedTask
            : CompleteFirstRunFromLocationAsync(FirstRunDefaultPath);
    }

    private bool CanStartInDefaultLocation() =>
        IsFirstRunVisible && FirstRunDefaultPath != null && !IsFirstRunBusy;

    [RelayCommand(CanExecute = nameof(CanBrowseElsewhere))]
    private void BrowseElsewhere()
    {
        FirstRunErrorMessage = null;
        BrowseLocationRequested?.Invoke();
    }

    private bool CanBrowseElsewhere() => IsFirstRunVisible && !IsFirstRunBusy;

    [RelayCommand(CanExecute = nameof(CanRetryStartup))]
    private Task RetryStartup()
    {
        return RetryStartupAsync?.Invoke() ?? Task.CompletedTask;
    }

    private bool CanRetryStartup() =>
        IsStartupError && RetryStartupAsync != null;

    [RelayCommand]
    private void CloseApplication()
    {
        CloseApplicationRequested?.Invoke();
    }

    partial void OnStartupGateStateChanged(StartupGateState value)
    {
        OnPropertyChanged(nameof(IsStartupGateVisible));
        OnPropertyChanged(nameof(IsStartupInitializing));
        OnPropertyChanged(nameof(IsFirstRunVisible));
        OnPropertyChanged(nameof(IsStartupError));
        OnPropertyChanged(nameof(IsPickerLedFirstRun));
        OnPropertyChanged(nameof(HasDefaultFirstRunLocation));
        OnPropertyChanged(nameof(IsWorkspaceInteractionEnabled));
        StartInDefaultLocationCommand.NotifyCanExecuteChanged();
        BrowseElsewhereCommand.NotifyCanExecuteChanged();
        RetryStartupCommand.NotifyCanExecuteChanged();
    }

    partial void OnFirstRunDefaultPathChanged(string? value)
    {
        OnPropertyChanged(nameof(IsPickerLedFirstRun));
        OnPropertyChanged(nameof(HasDefaultFirstRunLocation));
        StartInDefaultLocationCommand.NotifyCanExecuteChanged();
    }

    partial void OnFirstRunDefaultNameChanged(string value)
    {
        OnPropertyChanged(nameof(FirstRunPrimaryActionText));
    }

    partial void OnIsFirstRunBusyChanged(bool value)
    {
        StartInDefaultLocationCommand.NotifyCanExecuteChanged();
        BrowseElsewhereCommand.NotifyCanExecuteChanged();
    }

    private static string? GetFolderDisplayName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }
}
