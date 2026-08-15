using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BurstGroupingServiceTests
{
    private static DateTime T(double seconds) =>
        new DateTime(2026, 7, 1, 12, 0, 0).AddSeconds(seconds);

    [Fact]
    public void ClustersConsecutiveFramesIntoOrderedGroups()
    {
        var (groups, without) = BurstGroupingService.ComputeGroups(new (string, DateTime?)[]
        {
            ("a.jpg", T(0)), ("b.jpg", T(1)), ("c.jpg", T(2)),
            ("solo.jpg", T(15)),
            ("d.jpg", T(30)), ("e.jpg", T(31)),
        });

        Assert.Equal(0, without);
        Assert.Equal(2, groups.Count);
        Assert.Equal("burst_1", groups[0].Id);
        Assert.Equal(new[] { "a.jpg", "b.jpg", "c.jpg" }, groups[0].ImageIds);
        Assert.Equal("burst_2", groups[1].Id);
        Assert.Equal(new[] { "d.jpg", "e.jpg" }, groups[1].ImageIds);
    }

    [Fact]
    public void GapEqualToMaxClustersJustOverSplits()
    {
        var (atBoundary, _) = BurstGroupingService.ComputeGroups(
            new (string, DateTime?)[] { ("a", T(0)), ("b", T(2.0)) });
        Assert.Single(atBoundary);

        var (overBoundary, _) = BurstGroupingService.ComputeGroups(
            new (string, DateTime?)[] { ("a", T(0)), ("b", T(2.001)) });
        Assert.Empty(overBoundary);
    }

    [Fact]
    public void EqualTimestampsCluster()
    {
        var (groups, _) = BurstGroupingService.ComputeGroups(
            new (string, DateTime?)[] { ("b", T(0)), ("a", T(0)), ("c", T(0)) });

        var group = Assert.Single(groups);
        Assert.Equal(new[] { "a", "b", "c" }, group.ImageIds);
        Assert.Equal(0, group.DurationSeconds);
    }

    [Fact]
    public void MinGroupSizeFiltersSingletons()
    {
        var frames = new (string, DateTime?)[] { ("a", T(0)), ("b", T(60)) };

        var (withMin2, _) = BurstGroupingService.ComputeGroups(frames, minGroupSize: 2);
        Assert.Empty(withMin2);

        var (withMin1, _) = BurstGroupingService.ComputeGroups(frames, minGroupSize: 1);
        Assert.Equal(2, withMin1.Count);
    }

    [Fact]
    public void NullTimestampsExcludedAndCounted()
    {
        var (groups, without) = BurstGroupingService.ComputeGroups(new (string, DateTime?)[]
        {
            ("a", T(0)), ("b", T(1)), ("x", null), ("y", null),
        });

        Assert.Equal(2, without);
        Assert.Single(groups);

        var (none, all) = BurstGroupingService.ComputeGroups(
            new (string, DateTime?)[] { ("x", null), ("y", null) });
        Assert.Empty(none);
        Assert.Equal(2, all);
    }

    [Fact]
    public void InputOrderIrrelevant()
    {
        var shuffled = new (string, DateTime?)[]
        {
            ("e", T(31)), ("b", T(1)), ("d", T(30)), ("a", T(0)), ("c", T(2)),
        };

        var (groups, _) = BurstGroupingService.ComputeGroups(shuffled);

        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { "a", "b", "c" }, groups[0].ImageIds);
        Assert.Equal(new[] { "d", "e" }, groups[1].ImageIds);
    }

    [Fact]
    public void StartTimeAndDurationAreCorrect()
    {
        var (groups, _) = BurstGroupingService.ComputeGroups(
            new (string, DateTime?)[] { ("a", T(10)), ("b", T(11)), ("c", T(12.5)) });

        var group = Assert.Single(groups);
        Assert.Equal(T(10), group.StartTime);
        Assert.Equal(2.5, group.DurationSeconds, precision: 3);
    }

    [Fact]
    public void DegenerateParametersAreClamped()
    {
        var frames = new (string, DateTime?)[] { ("a", T(0)), ("b", T(0)) };

        var (groups, _) = BurstGroupingService.ComputeGroups(frames, maxGapSeconds: -5);
        Assert.Single(groups);

        var (singles, _) = BurstGroupingService.ComputeGroups(
            new (string, DateTime?)[] { ("a", T(0)) }, minGroupSize: -3);
        Assert.Single(singles);
    }

    [Fact]
    public void StandaloneRawJpegPairIsNotABurst()
    {
        var directory = Path.Combine("photos", "standalone");
        var raw = Path.Combine(directory, "capture.cr3");
        var jpeg = Path.Combine(directory, "capture.jpg");

        var (groups, without) = BurstGroupingService.ComputeGroups(
            new (string, DateTime?)[] { (raw, T(0)), (jpeg, T(0)) });

        Assert.Empty(groups);
        Assert.Equal(0, without);
    }

    [Fact]
    public void RawJpegBurstCountsAndOrdersLogicalCaptures()
    {
        var directory = Path.Combine("photos", "paired-burst");
        var firstRaw = Path.Combine(directory, "one.nef");
        var firstJpeg = Path.Combine(directory, "one.jpg");
        var secondRaw = Path.Combine(directory, "two.nef");
        var secondJpeg = Path.Combine(directory, "two.jpg");

        var (groups, _) = BurstGroupingService.ComputeGroups(
            new (string, DateTime?)[]
            {
                (secondRaw, T(1)), (firstJpeg, T(0)),
                (secondJpeg, T(1)), (firstRaw, T(0))
            });

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Captures.Count);
        Assert.Equal([firstJpeg, firstRaw], group.Captures[0].ImageIds);
        Assert.Equal([secondJpeg, secondRaw], group.Captures[1].ImageIds);
        Assert.Equal(
            [firstJpeg, firstRaw, secondJpeg, secondRaw],
            group.ImageIds);
    }

    [Fact]
    public void MixedPairsAndSinglesClusterAtCaptureLevel()
    {
        var directory = Path.Combine("photos", "mixed-burst");
        var firstRaw = Path.Combine(directory, "one.dng");
        var firstJpeg = Path.Combine(directory, "one.jpg");
        var single = Path.Combine(directory, "two.jpg");
        var thirdRaw = Path.Combine(directory, "three.arw");
        var thirdJpeg = Path.Combine(directory, "three.jpeg");

        var (groups, _) = BurstGroupingService.ComputeGroups(
            new (string, DateTime?)[]
            {
                (firstRaw, T(0)), (firstJpeg, T(0)), (single, T(1)),
                (thirdRaw, T(2)), (thirdJpeg, T(2))
            });

        var group = Assert.Single(groups);
        Assert.Equal(3, group.Captures.Count);
        Assert.Equal(5, group.ImageIds.Count);
        Assert.Equal([single], group.Captures[1].ImageIds);
    }

    [Fact]
    public void PairUsesItsOnlyTimestampAndStillCountsMissingFileTimestamp()
    {
        var directory = Path.Combine("photos", "partial-time");
        var raw = Path.Combine(directory, "one.raf");
        var jpeg = Path.Combine(directory, "one.jpg");
        var next = Path.Combine(directory, "two.jpg");

        var (groups, without) = BurstGroupingService.ComputeGroups(
            new (string, DateTime?)[]
            {
                (raw, null), (jpeg, T(0)), (next, T(1))
            });

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Captures.Count);
        Assert.Equal(1, without);
        Assert.Equal([jpeg, raw], group.Captures[0].ImageIds);
    }

    [Fact]
    public void PairWithNoTimestampsIsExcludedAndCountedPerFile()
    {
        var directory = Path.Combine("photos", "no-time");
        var raw = Path.Combine(directory, "capture.orf");
        var jpeg = Path.Combine(directory, "capture.jpg");

        var (groups, without) = BurstGroupingService.ComputeGroups(
            new (string, DateTime?)[] { (raw, null), (jpeg, null) });

        Assert.Empty(groups);
        Assert.Equal(2, without);
    }

    [Fact]
    public void ShuffledRawJpegInputKeepsGroupCaptureAndMemberOrderStable()
    {
        var directory = Path.Combine("photos", "deterministic");
        var files = new (string Id, DateTime? Taken)[]
        {
            (Path.Combine(directory, "one.cr3"), T(0)),
            (Path.Combine(directory, "one.jpg"), T(0)),
            (Path.Combine(directory, "two.cr3"), T(1)),
            (Path.Combine(directory, "two.jpg"), T(1))
        };

        var ordered = Assert.Single(BurstGroupingService.ComputeGroups(files).Groups);
        var shuffled = Assert.Single(BurstGroupingService.ComputeGroups(
            new[] { files[3], files[0], files[2], files[1] }).Groups);

        Assert.Equal(ordered.Id, shuffled.Id);
        Assert.Equal(ordered.ImageIds, shuffled.ImageIds);
        Assert.Equal(
            ordered.Captures.Select(capture => string.Join("|", capture.ImageIds)),
            shuffled.Captures.Select(capture => string.Join("|", capture.ImageIds)));
    }
}
