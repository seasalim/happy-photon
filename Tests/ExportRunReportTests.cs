using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportRunReportTests
{
    [Fact]
    public void PartialFailureAndWarnings_AreReportedTogetherPerTarget()
    {
        var capture = new ImageFile("photo.dng");
        var settings = new ExportSettings { OutputFolder = "exports" };
        var job = settings.CreateJob(
            [capture],
            [new ExportVariant("web", 2048), new ExportVariant("small", 1024)],
            useSubfolders: true);
        var failedTarget = job.Targets[1];
        var result = new ExportBatchResult(
            job,
            [
                new ExportTargetOutcome(
                    job.Targets[0].Capture,
                    job.Targets[0].Recipe,
                    job.Targets[0].ResolvedPath,
                    null),
                new ExportTargetOutcome(
                    failedTarget.Capture,
                    failedTarget.Recipe,
                    failedTarget.ResolvedPath,
                    "write failed")
            ],
            [new ExportWarning(capture, "profile_missing", "profile warning")]);

        var report = ExportRunReport.FromResult(result);

        Assert.True(report.HasFailures);
        Assert.True(report.HasWarnings);
        Assert.Equal("1 of 2 files exported.", report.Summary);
        Assert.Equal("small", Assert.Single(report.FailedTargets).Recipe.Name);
        Assert.Equal("profile warning", Assert.Single(report.Warnings).Message);
    }
}
