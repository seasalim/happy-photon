using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// WP9 gates retain the baseline workloads and report the approved chrome metrics.
public sealed class DialogChromeBaselineTests(ITestOutputHelper output)
{
    private const int Runs = 3;

    [AvaloniaFact]
    public void G2_FooterButtonMetrics()
    {
        using var theme = new TestUiScope(theme: ThemeVariant.Dark);
        using var directory = new TemporaryDirectory();
        var samples = new List<(string Name, double Height, double MinWidth)>();

        for (var run = 1; run <= Runs; run++)
        {
            ObserveFooter(new SettingsDialog(), ["Close"], run, samples);
            ObserveFooter(new HelpAboutDialog(), ["Close"], run, samples);
            ObserveFooter(new PasteSettingsDialog(new PasteSettingsViewModel(
                "source.jpg", 1, new Dictionary<string, bool>())), ["Cancel", "Paste"], run, samples);
            ObserveFooter(new RestoreBackupDialog(directory.Path),
                ["Cancel", "Restore…"], run, samples);
            ObserveFooter(new ConfirmationDialog("Confirmation", "Confirm this action?",
                ConfirmationDialogButtons.YesNo, false, "Cancel", "Continue"), ["Cancel", "Continue"], run, samples);
            ObserveFooter(new TextInputDialog("Text input", "Enter a name", "Example"),
                ["Cancel", "OK"], run, samples);
        }

        foreach (var group in samples.GroupBy(sample => sample.Name))
        {
            output.WriteLine($"G2 median {group.Key}: height={Median(group.Select(s => s.Height)):F2}px; " +
                $"MinWidth={Median(group.Select(s => s.MinWidth)):F2}px; runs={group.Count()}");
        }
    }

    [AvaloniaTheory]
    [InlineData("Move to Trash", true, "Cancel", "Move to Trash")]
    [InlineData("Restore catalog", true, "Cancel", "Restore")]
    [InlineData("Download originals for export?", false, "Cancel", "Download / Export")]
    public async Task G4_InitialFocusKeyboardResults(
        string title, bool destructive, string cancelLabel, string confirmLabel)
    {
        using var owner = new TestUiScope(new Window(), ThemeVariant.Dark);

        for (var run = 1; run <= Runs; run++)
        {
            foreach (var key in new[] { Key.Escape, Key.Enter })
            {
                // Match the current callers' flags and labels; no destructive operation runs.
                var result = ConfirmationDialog.ConfirmAsync(owner.Window!, title,
                    "Report-only prompt; no source files are accessed.", destructive, cancelLabel, confirmLabel);
                var dialog = owner.Window!.OwnedWindows.OfType<ConfirmationDialog>().Single();

                try
                {
                    Settle(dialog);
                    var focused = dialog.FocusManager?.GetFocusedElement();
                    Assert.Equal(destructive ? cancelLabel : confirmLabel, Assert.IsType<Button>(focused).Content);
                    var focus = focused is Button button ? $"Button({button.Content})" : focused?.GetType().Name ?? "none";
                    dialog.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
                    if (dialog.IsVisible) dialog.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
                    Settle(dialog);
                    Assert.True(result.IsCompletedSuccessfully);
                    Assert.Equal(key == Key.Enter && !destructive, result.Result);
                    Assert.False(dialog.IsVisible);
                    var value = result.Result.ToString();
                    output.WriteLine($"G4 run={run} prompt={title}; destructive={destructive}; " +
                        $"initialFocus={focus}; key={key}; result={value}; open={dialog.IsVisible}");
                }
                finally
                {
                    dialog.Close(false);
                    await result.WaitAsync(TestWaits.Condition);
                }
            }
        }
    }

    [AvaloniaFact]
    public void G5_OpenDialogsLiveThemeSwitch()
    {
        for (var run = 1; run <= Runs; run++)
        {
            using var theme = new TestUiScope(theme: ThemeVariant.Dark);
            var confirmation = new ConfirmationDialog("Confirmation", "Confirm this action?",
                ConfirmationDialogButtons.YesNo, false, "Cancel", "Continue");
            var input = new TextInputDialog("Text input", "Enter a name", "Example");
            using var confirmationScope = new TestUiScope(confirmation);
            using var inputScope = new TestUiScope(input);
            Settle(confirmation);
            Settle(input);
            ReportColours(confirmation, run, "Dark");
            ReportColours(input, run, "Dark");
            // test-teardown-policy: allow - using theme restores the prior application variant.
            Application.Current!.RequestedThemeVariant = HappyPhotonThemes.MidGray;
            Settle(confirmation);
            Settle(input);
            ReportColours(confirmation, run, "Middle Gray");
            ReportColours(input, run, "Middle Gray");
        }
    }

    private void ObserveFooter(Window dialog, string[] labels, int run,
        List<(string Name, double Height, double MinWidth)> samples)
    {
        using var scope = new TestUiScope(dialog);
        Settle(dialog);
        var buttons = dialog.GetLogicalDescendants().OfType<Button>()
            .Where(button => labels.Contains(button.Content?.ToString())).ToArray();
        Assert.Equal(labels.Length, buttons.Length);

        foreach (var button in buttons)
        {
            Assert.Equal(28, button.Bounds.Height);
            Assert.Equal(84, button.MinWidth);
            Assert.Contains("quiet-button", button.Classes);
            var name = $"{dialog.GetType().Name}/{button.Content}";
            samples.Add((name, button.Bounds.Height, button.MinWidth));
            output.WriteLine($"G2 run={run} {name}: height={button.Bounds.Height:F2}px; MinWidth={button.MinWidth:F2}px");
        }

        // Counts cover all six dialogs within each fresh sample, not just this footer.
        if (dialog is TextInputDialog)
        {
            var current = samples.TakeLast(10).ToArray();
            output.WriteLine($"G2 run={run}: distinctHeights={current.Select(s => s.Height).Distinct().Count()}; " +
                $"distinctMinWidths={current.Select(s => s.MinWidth).Distinct().Count()}");
        }
    }

    private void ReportColours(Window dialog, int run, string requestedTheme)
    {
        var text = dialog.GetLogicalDescendants().OfType<TextBlock>().First();
        var background = ((ISolidColorBrush)dialog.Background!).Color;
        var foreground = ((ISolidColorBrush)text.Foreground!).Color;

        Assert.True(dialog.IsVisible);
        Assert.Equal(Color.Parse(requestedTheme == "Dark" ? "#1b1b20" : "#3d3d3d"), background);
        Assert.Equal(Color.Parse(requestedTheme == "Dark" ? "#e4e1e9" : "#ffffff"), foreground);

        output.WriteLine($"G5 run={run} dialog={dialog.GetType().Name}; requested={requestedTheme}; " +
            $"actual={dialog.ActualThemeVariant}; open={dialog.IsVisible}; " +
            $"background={background}; text={foreground}");
    }

    private static double Median(IEnumerable<double> values) => values.Order().ElementAt(Runs / 2);

    internal static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
