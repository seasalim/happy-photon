using System.Diagnostics;
using Avalonia.Controls;
using HappyPhoton.ViewModels;

namespace HappyPhoton;

/// <summary>
/// Opt-in startup milestones for scripts/startup-perf.ps1, written as
/// "milestone,ms-since-process-start" lines. Inactive unless HAPPY_PHOTON_PERF
/// and HAPPY_PHOTON_STARTUP_TRACE (the output file) are both set.
/// </summary>
internal static class StartupTrace
{
    internal static void Attach(Window window, MainWindowViewModel viewModel)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF")) ||
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_STARTUP_TRACE") is not { Length: > 0 } path)
        {
            return;
        }

        var start = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        void Mark(string milestone) => File.AppendAllText(
            path,
            $"{milestone},{(DateTime.UtcNow - start).TotalMilliseconds:F1}{Environment.NewLine}");

        Mark("show");
        window.Opened += (_, _) => window.RequestAnimationFrame(_ => Mark("first-frame"));
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.StartupGateState) &&
                viewModel.StartupGateState != StartupGateState.Initializing)
            {
                Mark("gate-" + viewModel.StartupGateState.ToString().ToLowerInvariant());
            }
        };
    }
}
