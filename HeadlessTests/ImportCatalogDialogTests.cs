using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ImportCatalogDialogTests
{
    [AvaloniaFact]
    public async Task DialogHasNoManualButtonAndChangesReportLabelAfterApply()
    {
        using var flow = CreateCompletedFlow();
        var dialog = new ImportCatalogDialog(flow, "source.lrcat");
        dialog.Show();
        await WaitForAsync(() => flow.CanApply);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(dialog.FindControl<Button>("PreviewButton"));
        Assert.Equal("WHAT WILL CHANGE",
            dialog.FindControl<TextBlock>("ReportSectionLabel")!.Text);
        var apply = dialog.FindControl<Button>("ApplyButton")!;
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForAsync(() => flow.IsApplied);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("WHAT CHANGED",
            dialog.FindControl<TextBlock>("ReportSectionLabel")!.Text);
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task CloseButtonCancelsRunningCheckWithoutClosingDialog()
    {
        var (flow, tokenTask) = CreateBlockedFlow();
        using (flow)
        {
            var dialog = new ImportCatalogDialog(flow, "source.lrcat");
            dialog.Show();
            var token = await tokenTask;
            Dispatcher.UIThread.RunJobs();

            dialog.FindControl<Button>("CancelButton")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => !flow.HasInFlightOperation);
            Dispatcher.UIThread.RunJobs();

            Assert.True(token.IsCancellationRequested);
            Assert.True(dialog.IsVisible);
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public async Task SystemCloseCancelsRunningCheckAndWaitsForItToFinish()
    {
        var (flow, tokenTask) = CreateBlockedFlow();
        using (flow)
        {
            var dialog = new ImportCatalogDialog(flow, "source.lrcat");
            dialog.Show();
            var token = await tokenTask;
            Dispatcher.UIThread.RunJobs();

            dialog.Close();
            Assert.True(dialog.IsVisible);
            Assert.True(token.IsCancellationRequested);
            await WaitForAsync(() => !flow.HasInFlightOperation);
            Dispatcher.UIThread.RunJobs();

            dialog.Close();
            Assert.False(dialog.IsVisible);
        }
    }

    private static CatalogImportFlowViewModel CreateCompletedFlow()
    {
        var source = Source();
        var preview = Preview(source);
        return new CatalogImportFlowViewModel(
            new CatalogImportFlowOperations(
                (_, _) => Task.FromResult(source),
                (_, _) => Task.FromResult<CatalogImportStoredSettings?>(null),
                (_, _, _, _) => Task.FromResult(preview),
                (_, _) => Task.FromResult(new CatalogImportApplyResult(
                    preview.Report, [], 1))),
            source.CatalogPath);
    }

    private static (CatalogImportFlowViewModel Flow,
        Task<CancellationToken> Token) CreateBlockedFlow()
    {
        var source = Source();
        var tokenSource = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var flow = new CatalogImportFlowViewModel(
            new CatalogImportFlowOperations(
                (_, _) => Task.FromResult(source),
                (_, _) => Task.FromResult<CatalogImportStoredSettings?>(null),
                async (_, _, _, token) =>
                {
                    tokenSource.SetResult(token);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return null!;
                },
                (_, _) => throw new InvalidOperationException()),
            source.CatalogPath);
        return (flow, tokenSource.Task);
    }

    private static LightroomCatalogContents Source() =>
        new("source.lrcat", 1303001, 13, true, AssessmentAxes.All,
            [], [], []);

    private static CatalogImportPreview Preview(LightroomCatalogContents source)
    {
        var axis = new CatalogImportAxisSummary(1, 0, 0, 0, 0);
        var report = new CatalogImportReport(
            1, 1, 1, 1, 0, 0, 0, 0, 0,
            axis, axis, axis, new Dictionary<string, int>(), [], [], false);
        return new CatalogImportPreview(
            source.CatalogPath,
            CatalogImportPolicy.LightroomWins,
            new Dictionary<string, string>(),
            [], report, "settings", null, "{}", []);
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TestWaits.Condition;
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
        Assert.True(predicate());
    }
}
