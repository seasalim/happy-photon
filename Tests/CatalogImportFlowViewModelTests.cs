using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogImportFlowViewModelTests
{
    [Fact]
    public async Task RootCommit_HidesApplyAndCancelsPredecessorUntilNewestReportLands()
    {
        var harness = new FlowHarness();
        using var flow = harness.CreateFlow();
        await CompleteInitializationAsync(flow, harness);
        Assert.True(flow.CanApply);

        var secondRun = flow.ChooseRootAsync(FlowHarness.SourceRoot, "B");
        Assert.False(flow.CanApply);
        var second = harness.Requests[1];
        var thirdRun = flow.ChooseRootAsync(FlowHarness.SourceRoot, "C");
        var third = harness.Requests[2];

        Assert.True(second.Token.IsCancellationRequested);
        second.Complete(2);
        await secondRun;
        Assert.False(flow.CanApply);
        third.Complete(3);
        await thirdRun;

        Assert.True(flow.CanApply);
        Assert.Equal(3, flow.Report!.UpdatedPhotos);
        Assert.Equal("C", third.Mappings[FlowHarness.SourceRoot]);
    }

    [Fact]
    public async Task TextChanged_InvalidatesImmediatelyAndRestoreReusesMatchingReport()
    {
        var harness = new FlowHarness();
        using var flow = harness.CreateFlow();
        await CompleteInitializationAsync(flow, harness);

        flow.UpdateRootText(FlowHarness.SourceRoot, "B");
        Assert.False(flow.CanApply);
        Assert.Null(flow.Report);
        flow.UpdateRootText(FlowHarness.SourceRoot, FlowHarness.InitialPath);
        await flow.CommitInputsAsync();

        Assert.True(flow.CanApply);
        Assert.Equal(1, flow.Report!.UpdatedPhotos);
        Assert.Single(harness.Requests);
    }

    [Fact]
    public async Task TypeThenRestoreDuringInitialRun_CannotPublishInvalidatedRun()
    {
        var harness = new FlowHarness();
        using var flow = harness.CreateFlow();
        var initialization = flow.InitializeAsync();
        var first = Assert.Single(harness.Requests);

        flow.UpdateRootText(FlowHarness.SourceRoot, "B");
        flow.UpdateRootText(FlowHarness.SourceRoot, FlowHarness.InitialPath);
        var restoredRun = flow.CommitInputsAsync();
        var second = harness.Requests[1];
        first.Complete(1);
        second.Complete(2);
        await Task.WhenAll(initialization, restoredRun);

        Assert.True(first.Token.IsCancellationRequested);
        Assert.True(flow.CanApply);
        Assert.Equal(2, flow.Report!.UpdatedPhotos);
    }

    [Fact]
    public async Task RootEditorLostFocus_OnlyChecksAgainWhenSessionTextChanged()
    {
        var harness = new FlowHarness();
        using var flow = harness.CreateFlow();
        await CompleteInitializationAsync(flow, harness);

        flow.BeginRootEdit(FlowHarness.SourceRoot);
        await flow.CommitRootEditAsync(
            FlowHarness.SourceRoot, FlowHarness.InitialPath);
        Assert.Single(harness.Requests);

        flow.BeginRootEdit(FlowHarness.SourceRoot);
        flow.UpdateRootText(FlowHarness.SourceRoot, "B");
        var changedCommit = flow.CommitRootEditAsync(FlowHarness.SourceRoot, "B");
        Assert.Equal(2, harness.Requests.Count);
        harness.Requests[1].Complete(2);
        await changedCommit;

        Assert.True(flow.CanApply);
    }

    [Fact]
    public async Task PolicyChangeChecksAgainWhileUnchangedOverrideDoesNot()
    {
        var harness = new FlowHarness();
        using var flow = harness.CreateFlow();
        await CompleteInitializationAsync(flow, harness);

        await flow.OverrideRootAsync(FlowHarness.SourceRoot);
        Assert.Single(harness.Requests);
        var policyRun = flow.SetPolicyAsync(CatalogImportPolicy.FillEmptyOnly);
        Assert.False(flow.CanApply);
        Assert.Equal(CatalogImportPolicy.FillEmptyOnly, harness.Requests[1].Policy);
        harness.Requests[1].Complete(2);
        await policyRun;

        Assert.True(flow.CanApply);
    }

    [Fact]
    public async Task ApplyReceivesExactPreviewedPayload()
    {
        var harness = new FlowHarness();
        CatalogImportPreview? applied = null;
        harness.Apply = (preview, _) =>
        {
            applied = preview;
            return Task.FromResult(new CatalogImportApplyResult(
                preview.Report, [], 1));
        };
        using var flow = harness.CreateFlow();
        await CompleteInitializationAsync(flow, harness);
        var previewed = harness.Requests[0].Preview!;

        await flow.ApplyAsync();

        Assert.Same(previewed, applied);
        Assert.True(flow.ApplySucceeded);
        Assert.True(flow.IsApplied);
    }

    [Fact]
    public async Task ConflictAutomaticallyChecksAgainAndKeepsExplanationVisible()
    {
        var harness = new FlowHarness();
        harness.Apply = (_, _) => throw new CatalogImportConflictException();
        using var flow = harness.CreateFlow();
        await CompleteInitializationAsync(flow, harness);

        var apply = flow.ApplyAsync();
        Assert.Equal(2, harness.Requests.Count);
        Assert.Contains("changed", flow.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.False(flow.CanApply);
        harness.Requests[1].Complete(2);
        await apply;

        Assert.True(flow.CanApply);
        Assert.Equal(2, flow.Report!.UpdatedPhotos);
        Assert.Contains("changed", flow.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedConflictRefreshLeavesApplyUnavailable()
    {
        var harness = new FlowHarness();
        harness.Apply = (_, _) => throw new CatalogImportConflictException();
        using var flow = harness.CreateFlow();
        await CompleteInitializationAsync(flow, harness);

        var apply = flow.ApplyAsync();
        harness.Requests[1].Fail(new InvalidOperationException("read failed"));
        await apply;

        Assert.False(flow.CanApply);
        Assert.Contains("changed", flow.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("read failed", flow.StatusText);
    }

    private static async Task CompleteInitializationAsync(
        CatalogImportFlowViewModel flow,
        FlowHarness harness)
    {
        var initialization = flow.InitializeAsync();
        Assert.Single(harness.Requests).Complete(1);
        await initialization;
    }

    private sealed class FlowHarness
    {
        public const string SourceRoot = "D:/Photos/";
        public static readonly string InitialPath = Path.GetFullPath("A");
        public List<PreviewRequest> Requests { get; } = [];
        public Func<CatalogImportPreview, CancellationToken,
            Task<CatalogImportApplyResult>> Apply { get; set; } =
            (preview, _) => Task.FromResult(new CatalogImportApplyResult(
                preview.Report, [], 0));

        public CatalogImportFlowViewModel CreateFlow()
        {
            var operations = new CatalogImportFlowOperations(
                (_, _) => Task.FromResult(Source()),
                (_, _) => Task.FromResult<CatalogImportStoredSettings?>(new(
                    "source.lrcat",
                    new Dictionary<string, string> { [SourceRoot] = InitialPath },
                    new Dictionary<string, CatalogImportPolicy>())),
                (source, mappings, policy, token) =>
                {
                    var request = new PreviewRequest(
                        source,
                        new Dictionary<string, string>(mappings),
                        policy,
                        token);
                    Requests.Add(request);
                    return request.Task;
                },
                (preview, token) => Apply(preview, token));
            return new CatalogImportFlowViewModel(operations, "source.lrcat");
        }

        private static LightroomCatalogContents Source() =>
            new("source.lrcat", 1303001, 13, true, AssessmentAxes.All,
                [new CatalogSourceRoot(SourceRoot, 1)], [], []);
    }

    private sealed class PreviewRequest(
        LightroomCatalogContents source,
        IReadOnlyDictionary<string, string> mappings,
        CatalogImportPolicy policy,
        CancellationToken token)
    {
        private readonly TaskCompletionSource<CatalogImportPreview> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyDictionary<string, string> Mappings { get; } = mappings;
        public CatalogImportPolicy Policy { get; } = policy;
        public CancellationToken Token { get; } = token;
        public Task<CatalogImportPreview> Task => _completion.Task;
        public CatalogImportPreview? Preview { get; private set; }

        public void Complete(int updatedPhotos)
        {
            Preview = new CatalogImportPreview(
                source.CatalogPath,
                Policy,
                Mappings,
                [],
                Report(updatedPhotos),
                "settings",
                null,
                "{}",
                []);
            _completion.SetResult(Preview);
        }

        public void Fail(Exception exception) => _completion.SetException(exception);

        private static CatalogImportReport Report(int updatedPhotos)
        {
            var axis = new CatalogImportAxisSummary(
                updatedPhotos, 0, 0, 0, 0);
            return new CatalogImportReport(
                1, 1, updatedPhotos, 1, 0, 0, 0, 0,
                axis, axis, axis,
                new Dictionary<string, int>(), [], [], false);
        }
    }
}
