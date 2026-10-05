using System.Runtime.InteropServices;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    internal const string DcpHintPresentedKey = "dcp_hint_presented";
    internal const string DcpHintNotice =
        "Develop › Profile › Get Adobe camera profiles… · no Adobe camera profiles on this computer";

    private CancellationTokenSource? _dcpHintProbeCts;
    private DcpAdobeProfilePresence? _dcpHintPresence;
    private bool _ranFirstRun;
    private bool _dcpHintPresented;
    private bool _dcpHintKeyLoaded;
    private Task? _dcpHintKeyLoad;

    internal Func<CancellationToken, Task<DcpAdobeProfilePresence>> ProbeDcpProfilesAsync { get; set; } =
        token => DcpAdobeProfileIndex.ProbeAsync(cancellationToken: token);

    internal OSPlatform DcpHintPlatform { get; set; } = OperatingSystem.IsWindows()
        ? OSPlatform.Windows : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;

    internal Task DcpHintProbe { get; private set; } = Task.CompletedTask;

    internal static bool SupportsDcpHint(OSPlatform platform) =>
        platform == OSPlatform.Windows || platform == OSPlatform.OSX;

    public bool IsDcpHintNoteVisible => IsFirstRunAllSetStep && SupportsDcpHint(DcpHintPlatform) &&
        _dcpHintPresence == DcpAdobeProfilePresence.None && _dcpHintKeyLoaded && !_dcpHintPresented;

    private void StartDcpHintProbe()
    {
        if (_ranFirstRun) return;

        _ranFirstRun = true;
        _dcpHintProbeCts = new CancellationTokenSource();
        DcpHintProbe = ProbeDcpHintAsync(_dcpHintProbeCts.Token);
    }

    private async Task ProbeDcpHintAsync(CancellationToken token)
    {
        try
        {
            var presence = await ProbeDcpProfilesAsync(token).WaitAsync(token);
            if (token.IsCancellationRequested) return;

            _dcpHintPresence = presence;
            OnPropertyChanged(nameof(IsDcpHintNoteVisible));
        }
        catch (Exception exception)
        {
            _dcpHintPresence = DcpAdobeProfilePresence.Unknown;
            System.Diagnostics.Debug.WriteLine($"DCP hint probe: {exception.Message}");
        }
    }

    private void CancelDcpHintProbe()
    {
        _dcpHintProbeCts?.Cancel();
        _dcpHintProbeCts?.Dispose();
        _dcpHintProbeCts = null;
    }

    private async Task LoadDcpHintKeyAsync()
    {
        try
        {
            _dcpHintPresented = await _catalogService.GetAppSettingAsync(DcpHintPresentedKey) != null;
            _dcpHintKeyLoaded = true;
            OnPropertyChanged(nameof(IsDcpHintNoteVisible));
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"DCP hint setting: {exception.Message}");
        }
    }

    private void RefreshDcpHintNote()
    {
        if (IsFirstRunAllSetStep) _dcpHintKeyLoad ??= LoadDcpHintKeyAsync();

        OnPropertyChanged(nameof(IsDcpHintNoteVisible));
    }

    private async Task FinishDcpHintAsync()
    {
        var visible = IsDcpHintNoteVisible;
        CancelDcpHintProbe();
        if (!visible) return;

        _dcpHintPresented = true;

        try
        {
            await _catalogService.SetAppSettingAsync(DcpHintPresentedKey, "true");
        }
        catch (Exception exception)
        {
            // The hint must never block finishing setup; an unsaved key only means the notice may follow.
            System.Diagnostics.Debug.WriteLine($"DCP hint setting: {exception.Message}");
        }
    }

    private async Task OfferDcpHintAsync(DcpDiscoveryResult result)
    {
        if (_ranFirstRun || !SupportsDcpHint(DcpHintPlatform) || !result.AdobeScanAttempted ||
            !result.AdobeEnumerationComplete || result.AdobeCandidates != 0)
        {
            return;
        }

        await BackupNoticeLoad;
        if (_oneTimeNotice.IsPending) return;

        _dcpHintKeyLoad ??= LoadDcpHintKeyAsync();
        await _dcpHintKeyLoad;
        if (!_dcpHintKeyLoaded || _dcpHintPresented) return;

        _oneTimeNotice.Offer(DcpHintNotice, DcpHintPresentedKey, "true");
        OnPropertyChanged(nameof(StatusMessage));
    }
}
