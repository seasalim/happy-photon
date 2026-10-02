using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class FirstRunExperienceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Back_PreservesCommittedStorageAndDoesNotCommitAgain(bool resume)
    {
        using var catalog = new CatalogService(Path.Combine(_testRoot.Path, "catalog"));
        await using var vm = new MainWindowViewModel(catalog);
        var commits = 0;
        vm.CompleteDataLocationSetupAsync = () =>
        {
            commits++;
            vm.MarkFirstRunStorageCommitted();
            vm.ResumeFirstRunAfterStorage(_testRoot.Path);

            return Task.CompletedTask;
        };
        vm.ShowFirstRunWelcome(_testRoot.Path);
        Assert.False(vm.BackFirstRunCommand.CanExecute(null));
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        Assert.True(vm.CanChangeFirstRunStorage);
        vm.BackFirstRunCommand.Execute(null);
        Assert.Equal(FirstRunStep.Welcome, vm.FirstRunStep);
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);

        if (resume)
        {
            vm.ResumeFirstRunAfterStorage(_testRoot.Path);
        }
        else
        {
            await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        }

        var roots = (vm.SetupCatalogRoot, vm.SetupCacheRoot);
        vm.BackFirstRunCommand.Execute(null);
        Assert.Equal(FirstRunStep.Storage, vm.FirstRunStep);
        Assert.True(vm.IsFirstRunStorageReadOnly);
        Assert.False(vm.CanChangeFirstRunStorage);
        Assert.False(vm.ChangeSetupCatalogCommand.CanExecute(null));
        Assert.False(vm.ChangeSetupCacheCommand.CanExecute(null));
        vm.BackFirstRunCommand.Execute(null);
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        Assert.Equal(FirstRunStep.Pictures, vm.FirstRunStep);
        Assert.Equal(resume ? 0 : 1, commits);
        Assert.Equal(roots, (vm.SetupCatalogRoot, vm.SetupCacheRoot));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PicturesRoundTrip_ReplacesOfferAndCatalogs(bool changeFolder, bool apply)
    {
        using var catalog = new CatalogService(Path.Combine(_testRoot.Path, "catalog"));
        await using var vm = new MainWindowViewModel(catalog);
        var pictures = Directory.CreateDirectory(Path.Combine(_testRoot.Path, "Pictures")).FullName;
        var next = changeFolder
            ? Directory.CreateDirectory(Path.Combine(_testRoot.Path, "Other")).FullName
            : pictures;
        var detections = new List<string>();
        var result = new LightroomDetectionResult(true, ["first.lrcat", "second.lrcat"]);
        vm.DetectLightroomAsync = (path, _) =>
        {
            detections.Add(path);

            return Task.FromResult(result);
        };
        vm.RequestFirstRunCatalogImportAsync = _ => Task.FromResult(true);
        vm.ShowFirstRunWelcome(pictures);
        vm.ResumeFirstRunAfterStorage(pictures);
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        vm.DetectedLightroomCatalogPath = "second.lrcat";

        if (apply)
        {
            await vm.ImportDetectedLightroomCommand.ExecuteAsync(null);
        }
        else
        {
            vm.SkipDetectedLightroomCommand.Execute(null);
        }

        vm.BackFirstRunCommand.Execute(null);
        Assert.Equal(FirstRunStep.Lightroom, vm.FirstRunStep);
        Assert.Equal("second.lrcat", vm.DetectedLightroomCatalogPath);
        Assert.Single(detections);
        vm.BackFirstRunCommand.Execute(null);
        Assert.Equal(FirstRunStep.Pictures, vm.FirstRunStep);
        result = new LightroomDetectionResult(true, ["replacement.lrcat"]);
        await vm.CompleteFirstRunFromLocationAsync(next);
        Assert.Equal(["replacement.lrcat"], vm.DetectedLightroomCatalogPaths);
        Assert.Equal("replacement.lrcat", vm.DetectedLightroomCatalogPath);
        vm.BackFirstRunCommand.Execute(null);
        result = new LightroomDetectionResult(true, Array.Empty<string>());
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        Assert.Equal(FirstRunStep.Lightroom, vm.FirstRunStep);
        Assert.Empty(vm.DetectedLightroomCatalogPaths);
        Assert.Null(vm.DetectedLightroomCatalogPath);
        vm.SkipDetectedLightroomCommand.Execute(null);
        vm.BackFirstRunCommand.Execute(null);
        Assert.Equal(FirstRunStep.Lightroom, vm.FirstRunStep);
        vm.BackFirstRunCommand.Execute(null);
        result = new LightroomDetectionResult(true, ["last.lrcat"]);
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        vm.BackFirstRunCommand.Execute(null);
        result = LightroomDetectionResult.NotDetected;
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        Assert.Equal(FirstRunStep.AllSet, vm.FirstRunStep);
        Assert.Empty(vm.DetectedLightroomCatalogPaths);
        Assert.Null(vm.DetectedLightroomCatalogPath);
        vm.BackFirstRunCommand.Execute(null);
        Assert.Equal(FirstRunStep.Pictures, vm.FirstRunStep);
        Assert.Equal(next, vm.FirstRunPicturesPath);
        Assert.Equal([pictures, next, next, next, next], detections);
        Assert.Null(vm.FirstRunExperienceVersion);
        Assert.False(vm.CanPersistFolderSession);
    }

    [Fact]
    public async Task Back_IsDisabledDuringWorkAndOutsideFirstRun()
    {
        using var catalog = new CatalogService(Path.Combine(_testRoot.Path, "catalog"));
        await using var vm = new MainWindowViewModel(catalog);
        Assert.False(vm.BackFirstRunCommand.CanExecute(null));
        vm.ShowFirstRunWelcome(_testRoot.Path);
        vm.ResumeFirstRunAfterStorage(_testRoot.Path);
        var changes = 0;
        vm.BackFirstRunCommand.CanExecuteChanged += (_, _) => changes++;
        vm.IsFirstRunBusy = true;
        Assert.False(vm.BackFirstRunCommand.CanExecute(null));
        vm.BackFirstRunCommand.Execute(null);
        Assert.Equal(FirstRunStep.Pictures, vm.FirstRunStep);
        vm.IsFirstRunBusy = false;
        Assert.True(vm.BackFirstRunCommand.CanExecute(null));
        Assert.Equal(2, changes);
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        vm.BackFirstRunCommand.Execute(null);
        Assert.Equal(FirstRunStep.Pictures, vm.FirstRunStep);
        vm.ShowWorkspaceReady(1);
        Assert.False(vm.BackFirstRunCommand.CanExecute(null));
    }
}
