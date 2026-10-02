using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ImportCloseSlotBaselineTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(720)]
    [InlineData(620)]
    public async Task ReportCloseSlotAcrossRealFlowStates(int width)
    {
        using var root = new TemporaryDirectory();
        var measurements = new List<(double X, double Width)>();
        await MeasureScenarioAsync("ready");
        await MeasureScenarioAsync("read");
        await MeasureScenarioAsync("preview");
        await MeasureScenarioAsync("apply");
        var xChange = measurements.Max(value => value.X) - measurements.Min(value => value.X);
        var widthChange = measurements.Max(value => value.Width) - measurements.Min(value => value.Width);
        output.WriteLine($"VISUALS-WP11 G3 max-x-change={xChange:F3} max-width-change={widthChange:F3} max-change={Math.Max(xChange, widthChange):F3}");

        Assert.Equal(0, xChange);
        Assert.Equal(0, widthChange);

        async Task MeasureScenarioAsync(string scenario)
        {
            var source = new LightroomCatalogContents(
                Path.Combine(root.Path, "Sample.lrcat"), 1303001, 13, true,
                AssessmentAxes.All, [], [], []);
            var preview = Preview(source);
            var entered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var flow = new CatalogImportFlowViewModel(new CatalogImportFlowOperations(
                async (_, token) =>
                {
                    if (scenario == "read") await BlockAsync(token);

                    return source;
                },
                (_, _) => Task.FromResult<CatalogImportStoredSettings?>(null),
                async (_, _, _, _, token) =>
                {
                    if (scenario == "preview") await BlockAsync(token);

                    return preview;
                },
                async (_, token) =>
                {
                    if (scenario == "apply") await BlockAsync(token);

                    return new CatalogImportApplyResult(preview.Report, [], 1);
                }), source.CatalogPath);
            var dialog = new ImportCatalogDialog(flow, source.CatalogPath) { Width = width };
            using var theme = new TestUiScope(theme: ThemeVariant.Dark);

            try
            {
                // test-teardown-policy: allow - finally cancels, drains the flow and closes the dialog.
                dialog.Show();

                if (scenario is "ready" or "apply")
                {
                    await TestWaits.UntilAsync(() => flow.CanApply);
                    Record("Import shown", "Close", true);
                    dialog.FindControl<Button>("ApplyButton")!
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                    if (scenario == "ready")
                    {
                        await TestWaits.UntilAsync(() => flow.IsApplied && !flow.IsBusy);
                        Record("Import hidden", "Close", false);
                        Record("Close", "Close", false);
                    }
                }

                if (scenario != "ready")
                {
                    await entered.Task.WaitAsync(TestWaits.Condition);
                    var label = scenario switch
                    {
                        "read" => "Cancel catalog read",
                        "preview" => "Cancel check",
                        _ => "Cancel import"
                    };
                    Record(label, label, false);
                }
            }
            finally
            {
                flow.CancelCurrentOperation();
                await TestWaits.UntilAsync(() => !flow.HasInFlightOperation);
                dialog.Close();
            }

            async Task BlockAsync(CancellationToken token)
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            void Record(string state, string label, bool importVisible)
            {
                Dispatcher.UIThread.RunJobs();
                dialog.UpdateLayout();
                var close = dialog.FindControl<Button>("CancelButton")!;
                Assert.Equal(label, close.Content);
                Assert.Equal(importVisible, dialog.FindControl<Button>("ApplyButton")!.IsVisible);
                Assert.True(close.IsEffectivelyVisible);
                Assert.True(close.Bounds.Width > 0);
                var position = close.TranslatePoint(new Point(), dialog);
                Assert.NotNull(position);
                measurements.Add((position.Value.X, close.Bounds.Width));
                output.WriteLine($"VISUALS-WP11 G3 {state} x={position.Value.X:F3} width={close.Bounds.Width:F3}");
            }
        }
    }

    private static CatalogImportPreview Preview(LightroomCatalogContents source)
    {
        var axis = new CatalogImportAxisSummary(1, 0, 0, 0, 0);
        var report = new CatalogImportReport(
            1, 1, 1, 1, 0, 0, 0, 0, 0, axis, axis, axis,
            new Dictionary<string, int>(), [], [], false);

        return new CatalogImportPreview(source.CatalogPath, CatalogImportPolicy.LightroomWins,
            new Dictionary<string, string>(), [], report, "settings", null, "{}", []);
    }
}
