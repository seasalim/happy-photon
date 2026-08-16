using Avalonia.Threading;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private LibRawRuntimeHealth? _rawRuntimeHealth;
    private Task<LibRawRuntimeHealth>? _rawRuntimeProbe;

    public bool IsRawRuntimeHealthPending => _rawRuntimeHealth == null;

    public bool IsRawRuntimeDegraded => _rawRuntimeHealth is { IsHealthy: false };

    public string RawRuntimeStatusText => IsRawRuntimeDegraded
        ? $"Native RAW support is unavailable ({RejectedComponentText()}); " +
          "compatible files will use the fallback decoder."
        : string.Empty;

    public string RawRuntimeSupportText => IsRawRuntimeDegraded
        ? AppBuildInfo.Identity.SupportText + Environment.NewLine +
          "RAW runtime: degraded" + Environment.NewLine +
          _rawRuntimeHealth!.DiagnosticText
        : AppBuildInfo.Identity.SupportText;

    internal async Task EnsureRawRuntimeReadyAsync(
        Func<Task<LibRawRuntimeHealth>>? probe = null)
    {
        if (_rawRuntimeHealth != null)
        {
            return;
        }

        _rawRuntimeProbe ??= probe?.Invoke() ?? LibRawNativeSupport.ProbeAsync();
        var health = await _rawRuntimeProbe.ConfigureAwait(false);
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyRawRuntimeHealth(health);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => ApplyRawRuntimeHealth(health));
    }

    internal void ApplyRawRuntimeHealth(LibRawRuntimeHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);
        if (_imageService.IsValueCreated &&
            !ReferenceEquals(_rawRuntimeHealth, health))
        {
            throw new InvalidOperationException(
                "RAW runtime health cannot change after image services are composed.");
        }

        _rawRuntimeHealth = health;
        OnPropertyChanged(nameof(IsRawRuntimeHealthPending));
        OnPropertyChanged(nameof(IsRawRuntimeDegraded));
        OnPropertyChanged(nameof(RawRuntimeStatusText));
        OnPropertyChanged(nameof(RawRuntimeSupportText));
    }

    private string RejectedComponentText() =>
        _rawRuntimeHealth?.RejectedComponent switch
        {
            LibRawRuntimeComponent.Bridge => "bridge rejected",
            LibRawRuntimeComponent.LibRawCompanion => "LibRaw companion rejected",
            _ => "runtime rejected",
        };
}
