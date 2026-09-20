using System.Collections.Concurrent;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class XmpSidecarWriterCapacityControlTests(ITestOutputHelper output)
{
    // Pinned G4 control: keep direct admission independent of bulk publication.
    [Fact]
    public async Task DirectAdmissionAtCapacityFourRejectsSixOfTenRatedV1Images()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        var snapshots = new List<AssessmentSnapshot>();
        for (var index = 0; index < 10; index++)
        {
            // GetOrCreateImageAsync seeds the permanent V1 row; no original is read.
            var path = Path.Combine(root.Path, $"control-{index}.cr3");
            var id = await catalog.GetOrCreateImageAsync(path);
            snapshots.Add(Assert.Single(await catalog.MutateAssessmentsAsync(
                [new AssessmentMutation(id, AssessmentAxes.Rating, Rating: 3,
                    PendingAxes: AssessmentAxes.Rating)])));
        }

        var paths = snapshots.Select(snapshot => snapshot.FilePath).ToArray();
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var reports = new ConcurrentQueue<string>();
        await using var writer = new XmpSidecarWriter(
            catalog, ColorLabelNames.Defaults, capacity: 4);
        writer.Report = reports.Enqueue;
        writer.BeforePromotionAsync = async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        writer.Start();

        var admitted = 0;
        var rejected = 0;
        var queueFullReports = 0;
        try
        {
            for (var index = 0; index < snapshots.Count; index++)
            {
                var accepted = writer.TryEnqueue(
                    snapshots[index], AssessmentAxes.Rating,
                    paths, XmpSidecarNaming.FullName);
                if (accepted) admitted++;
                else rejected++;

                if (index == 0)
                {
                    Assert.True(accepted);
                    await entered.Task.WaitAsync(TestWaits.Condition);
                }
            }

            queueFullReports = reports.Count(report =>
                report.Contains("XMP queue full", StringComparison.Ordinal));
            output.WriteLine(
                $"G4 control: admitted={admitted}; rejected={rejected}; " +
                $"queue-full reports={queueFullReports}");
            Assert.Empty(Directory.GetFiles(root.Path, "*.xmp"));
        }
        finally
        {
            release.TrySetResult();
            await writer.DrainAsync().WaitAsync(TestWaits.Condition);
        }

        Assert.Equal(4, admitted);
        Assert.Equal(6, rejected);
        Assert.Equal(6, queueFullReports);
        Assert.Equal(6, reports.Count);
        Assert.Equal(4, Directory.GetFiles(root.Path, "*.xmp").Length);
        var current = await catalog.LoadAssessmentSnapshotsAsync(
            snapshots.Select(snapshot => snapshot.ImageId).ToArray());
        Assert.Equal(4, current.Count(row => row.PendingAxes == AssessmentAxes.None));
        Assert.Equal(6, current.Count(row => row.PendingAxes == AssessmentAxes.Rating));
    }
}
