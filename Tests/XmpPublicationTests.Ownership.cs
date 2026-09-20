using System.Collections.Concurrent;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class XmpPublicationTests
{
    [Fact]
    public async Task ReconcileCompletesBeforeMarking_AndItsAdoptedValuesArePublished()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var image = await SeedAsync(catalog, "reconcile", 2);
        await using var vm = CreateVm(catalog, [image]);
        var reconcile = Signal();
        SetField(vm, "_xmpReconcileTask", reconcile.Task);
        var publication = vm.WriteXmpSidecarsCommand.ExecuteAsync(null);
        Assert.False(publication.IsCompleted);
        Assert.Equal(AssessmentAxes.None, (await Snapshot(catalog, image)).PendingAxes);
        var snapshot = await Snapshot(catalog, image);
        var adoption = await catalog.AdoptSidecarFactsAsync([new(snapshot,
            new(Sidecar(image), snapshot.AssessedUtc.AddMinutes(1), 0, true),
            new(XmpFact<int>.Matched(5), XmpFact<ImageFlag>.Missing,
                XmpFact<ColorLabel>.Missing, XmpFact<CropRegion>.Missing))]);
        Assert.Single(adoption);
        reconcile.SetResult();
        await publication.WaitAsync(TestWaits.Condition);
        Assert.Equal(5, Facts(image).Rating.Value);
        Assert.Equal(snapshot.Revision + 1, (await Snapshot(catalog, image)).Revision);
        SetField(vm, "_xmpReconcileTask", Task.CompletedTask);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedReconcile_PublishesUnlessFolderWasCancelled(bool cancelled)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var image = await SeedAsync(catalog, "failed-reconcile", 3);
        await using var vm = CreateVm(catalog, [image]);
        var reconcile = Signal();
        SetField(vm, "_xmpReconcileTask", reconcile.Task);
        var publication = vm.WriteXmpSidecarsCommand.ExecuteAsync(null);
        try
        {
            Assert.False(publication.IsCompleted);
            if (cancelled)
            {
                SetField(vm, "_browseGeneration", 1);
                vm.Browse.SetImages([]);
                reconcile.SetCanceled();
            }
            else reconcile.SetException(new IOException("Reconcile failed"));
            await publication.WaitAsync(TestWaits.Condition);
            Assert.Equal(!cancelled, File.Exists(Sidecar(image)));
            Assert.Equal(AssessmentAxes.None, (await Snapshot(catalog, image)).PendingAxes);
            if (!cancelled)
            {
                Assert.Equal(3, Facts(image).Rating.Value);
                Assert.Equal("XMP sidecars written for 1 photo", vm.TransientStatus);
            }
        }
        finally { SetField(vm, "_xmpReconcileTask", Task.CompletedTask); }
    }

    [Fact]
    public async Task CapacityFour_WithActiveJobAndMutation_PublishesAllTenWithoutQueueFull()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var images = new List<ImageFile>();
        for (var i = 0; i < 10; i++) images.Add(await SeedAsync(catalog, $"bulk-{i}", 3));
        var active = await SeedAsync(catalog, "active", 2);
        var mutation = await SeedAsync(catalog, "mutation", 4);
        await using var vm = CreateVm(catalog, images.ToArray());
        var writer = Writer(vm);
        var reports = new ConcurrentQueue<string>();
        writer.Report = reports.Enqueue;
        var entered = Signal();
        var release = Signal();
        writer.BeforePromotionAsync = async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        Assert.True(writer.TryEnqueue(await Snapshot(catalog, active), AssessmentAxes.All,
            [active.FilePath], XmpSidecarNaming.FullName));
        await entered.Task.WaitAsync(TestWaits.Condition);
        var publication = vm.WriteXmpSidecarsCommand.ExecuteAsync(null);
        try
        {
            await TestWaits.UntilAsync(() => images.All(image => image.PendingAssessmentAxes != AssessmentAxes.None));
            var committed = Assert.Single(await catalog.MutateAssessmentsAsync([
                new(mutation.CatalogId, AssessmentAxes.Rating, Rating: 5, PendingAxes: AssessmentAxes.Rating)]));
            Assert.True(writer.TryEnqueue(committed, AssessmentAxes.Rating,
                [mutation.FilePath], XmpSidecarNaming.FullName));
            Assert.False(publication.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await publication.WaitAsync(TestWaits.Condition);
        Assert.All(images, image => Assert.True(File.Exists(Sidecar(image))));
        Assert.True(File.Exists(Sidecar(active)));
        Assert.Equal(5, Facts(mutation).Rating.Value);
        Assert.Empty(reports);
        Assert.Equal("XMP sidecars written for 10 photos", vm.TransientStatus);
    }

    [Fact]
    public async Task MutationOfLaterTargetDuringDrain_CannotBeOverwrittenByCapturedSnapshot()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var images = new List<ImageFile>();
        for (var i = 0; i < 6; i++) images.Add(await SeedAsync(catalog, $"newer-{i}", 3));
        await using var vm = CreateVm(catalog, images.ToArray());
        var writer = Writer(vm);
        var entered = Signal();
        var release = Signal();
        writer.BeforePromotionAsync = async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        var publication = vm.WriteXmpSidecarsCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TestWaits.Condition);
        try
        {
            var later = images[^1];
            var mutation = Assert.Single(await catalog.MutateAssessmentsAsync([
                new(later.CatalogId, AssessmentAxes.Rating, Rating: 5, PendingAxes: AssessmentAxes.Rating)]));
            Assert.True(writer.TryEnqueue(mutation, mutation.PendingAxes,
                images.Select(i => i.FilePath).ToArray(), XmpSidecarNaming.FullName));
        }
        finally { release.TrySetResult(); }
        await publication.WaitAsync(TestWaits.Condition);
        Assert.Equal(5, Facts(images[^1]).Rating.Value);
        Assert.Equal(5, images[^1].Rating);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FolderSwitchOrWriterStop_EndsAdmissionAndLeavesLaterRowsPending(bool stop)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var images = new List<ImageFile>();
        for (var i = 0; i < 6; i++) images.Add(await SeedAsync(catalog, $"stop-{i}", 3));
        await using var vm = CreateVm(catalog, images.ToArray());
        var writer = Writer(vm);
        var entered = Signal();
        var release = Signal();
        writer.BeforePromotionAsync = async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        var publication = vm.WriteXmpSidecarsCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TestWaits.Condition);
        if (stop) await writer.StopAsync();
        else
        {
            SetField(vm, "_browseGeneration", 1);
            vm.Browse.SetImages([]);
            release.TrySetResult();
        }
        await publication.WaitAsync(TestWaits.Condition);
        await writer.DrainAsync().WaitAsync(TestWaits.Condition);
        Assert.All(images.Skip(2), image => Assert.False(File.Exists(Sidecar(image))));
        Assert.All(await catalog.LoadAssessmentSnapshotsAsync(images.Skip(2).Select(i => i.CatalogId).ToArray()),
            row => Assert.Equal(AssessmentAxes.All, row.PendingAxes));
    }

    [Fact]
    public async Task CompletedDeleteWhileDraining_CannotAdmitAnOrphanSidecar()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var images = new List<ImageFile>();
        for (var i = 0; i < 6; i++) images.Add(await SeedAsync(catalog, $"delete-{i}", 3));
        await using var vm = CreateVm(catalog, images.ToArray());
        var writer = Writer(vm);
        var entered = Signal();
        var release = Signal();
        writer.BeforePromotionAsync = async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        var publication = vm.WriteXmpSidecarsCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TestWaits.Condition);
        var deleted = images[^1];
        try
        {
            Claim(vm, deleted.FilePath, true);
            await catalog.DeleteImageAsync(deleted.CatalogId);
            vm.Browse.SetImages(images.Where(image => image != deleted).ToArray());
            Claim(vm, deleted.FilePath, false);
        }
        finally { release.TrySetResult(); }
        await publication.WaitAsync(TestWaits.Condition);
        Assert.False(File.Exists(Sidecar(deleted)));
        Assert.All(images.Take(5), image => Assert.True(File.Exists(Sidecar(image))));
        Assert.Equal("XMP sidecars written for 5 photos", vm.TransientStatus);
    }
}
