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
}
