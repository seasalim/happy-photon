using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class LoadingLabelObserverTests
{
    [WindowsFact]
    public async Task KeepsContinuousIntervalsAcrossStepsAndFollowsNewPanes()
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(catalog);
        using var observer = new LoadingLabelObserver("loupe", "ShowLoadingMessage");
        observer.Attach(vm);
        observer.BeginStep(1);
        var first = new PublishedPane();
        vm.LoupePane = first;
        first.Pulse();
        observer.BeginStep(2);
        first.IsLoading = false;
        first.IsLoading = true;
        first.IsLoading = false;
        var second = new PublishedPane();
        vm.LoupePane = second;
        second.IsLoading = false;
        observer.Dispose();
        var report = observer.Snapshot();
        Assert.Equal(3, report.Intervals.Length);
        Assert.Equal(3, report.Activations.Length);
        Assert.Equal(1, report.Intervals[0].StartStep);
        Assert.Equal(2, report.Intervals[0].EndStep);
        Assert.All(report.Intervals, i => Assert.Equal("condition-false", i.EndReason));
        Assert.Equal(report.Intervals.Select(i => (int?)i.Id), report.Activations.Select(a => a.IntervalId));
        Assert.All(report.Intervals, i => Assert.True(i.DurationMs >= 0));
    }

    [WindowsFact]
    public async Task PublishedPropertyIsIndependentOfUnderlyingCondition()
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(catalog);
        using var observer = new LoadingLabelObserver("loupe", nameof(PublishedPane.Published));
        observer.Attach(vm);
        observer.BeginStep(1);
        var pane = new PublishedPane();
        vm.LoupePane = pane;
        Assert.Equal(0, observer.ActivationCount);
        Assert.True(observer.HasOpenInterval);
        pane.Published = true;
        pane.Pulse();
        pane.IsLoading = false;
        pane.Published = false;
        observer.BeginStep(2);
        pane.IsLoading = true;
        pane.IsLoading = false;
        observer.Dispose();
        var report = observer.Snapshot();
        Assert.Equal(2, report.Intervals.Length);
        Assert.Equal(report.Intervals[0].Id, Assert.Single(report.Activations).IntervalId);
        Assert.Equal(0, report.UncorrelatedActivations);
    }

    [WindowsFact]
    public async Task ReplacementAndTeardownCannotMasqueradeAsCompletedIntervals()
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(catalog);
        using var observer = new LoadingLabelObserver("loupe", "ShowLoadingMessage");
        observer.Attach(vm);
        observer.BeginStep(1);
        vm.LoupePane = new PublishedPane();
        vm.LoupePane = new PublishedPane();
        observer.Dispose();
        var report = observer.Snapshot();
        Assert.Equal(2, report.IncompleteIntervals);
        Assert.Equal(0, report.Distribution.Count);
        Assert.Equal(new[] { "owner-replaced", "observation-ended" }, report.Intervals.Select(i => i.EndReason));
    }

    private sealed class PublishedPane() : ComparePaneViewModel(new ImageFile("observer-only.jpg"))
    {
        private bool _published;
        public bool Published { get => _published; set => SetProperty(ref _published, value); }
        internal void Pulse() => OnPropertyChanged(string.Empty);
    }
}
