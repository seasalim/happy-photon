using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class HelpAboutDialog : Window
{
    private AppBuildIdentity BuildIdentity { get; } = AppBuildInfo.Identity;
    private readonly Func<Uri, Task<bool>>? _launchUriAsync;

    public HelpAboutDialog() : this(null)
    {
    }

    internal HelpAboutDialog(
        MainWindowViewModel? viewModel,
        Func<Uri, Task<bool>>? launchUriAsync = null)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _launchUriAsync = launchUriAsync;
        DataContext = this;
        if (viewModel?.IsUpdateAvailable == true)
        {
            HelpAboutTabs.SelectedIndex = 1;
        }
    }

    public MainWindowViewModel? ViewModel { get; }

    public IReadOnlyList<ShortcutGroup> Groups => ShortcutCatalog.Groups;

    public string VersionDisplayText => $"Version · {BuildIdentity.FriendlyVersion}";

    public string SourceRevisionDisplayText => BuildIdentity.ShortSourceRevision is { } revision
        ? $"Source revision · {revision}"
        : BuildIdentity.Provenance == BuildIdentityProvenance.UnstampedLocalFallback
            ? "Source revision · unstamped local build"
            : "Source revision · unavailable (incomplete stamp)";

    public string BuildDateDisplayText => BuildIdentity.DateDisplayText;

    public string CopyrightText => BuildIdentity.Copyright;

    public bool HasProjectUrl => BuildIdentity.ProjectUrl != null;

    public bool HasLicenseUrl => BuildIdentity.LicenseUrl != null;

    public bool HasThirdPartyNoticesUrl => BuildIdentity.ThirdPartyNoticesUrl != null;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnCopyVersionInfoClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            {
                CopyFeedbackText.Text = "Clipboard is not available.";
                return;
            }

            await clipboard.SetTextAsync(BuildIdentity.SupportText);
            CopyFeedbackText.Text = "Version info copied.";
        }
        catch (Exception)
        {
            CopyFeedbackText.Text = "Could not copy version info.";
        }
    }

    private async void OnProjectClick(object? sender, RoutedEventArgs e) =>
        await OpenUrlAsync(BuildIdentity.ProjectUrl, "project");

    private async void OnLicenseClick(object? sender, RoutedEventArgs e) =>
        await OpenUrlAsync(BuildIdentity.LicenseUrl, "license");

    private async void OnThirdPartyNoticesClick(object? sender, RoutedEventArgs e) =>
        await OpenUrlAsync(BuildIdentity.ThirdPartyNoticesUrl, "third-party notices");

    private async void OnUpgradeClick(object? sender, RoutedEventArgs e)
    {
        var uri = ViewModel?.UpgradeUri;
        try
        {
            if (uri == null || !await LaunchUriAsync(uri))
            {
                UpdateActionFeedbackText.Text = "Could not open the update destination.";
                return;
            }

            UpdateActionFeedbackText.Text = string.Empty;
        }
        catch (Exception)
        {
            UpdateActionFeedbackText.Text = "Could not open the update destination.";
        }
    }

    private async Task OpenUrlAsync(string? url, string description)
    {
        try
        {
            if (url == null || !await LaunchUriAsync(new Uri(url)))
            {
                CopyFeedbackText.Text = $"Could not open {description}.";
                return;
            }

            CopyFeedbackText.Text = string.Empty;
        }
        catch (Exception)
        {
            CopyFeedbackText.Text = $"Could not open {description}.";
        }
    }

    private Task<bool> LaunchUriAsync(Uri uri)
    {
        if (_launchUriAsync != null)
        {
            return _launchUriAsync(uri);
        }

        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        return launcher?.LaunchUriAsync(uri) ?? Task.FromResult(false);
    }
}
