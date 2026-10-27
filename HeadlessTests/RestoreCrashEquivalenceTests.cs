using Xunit;

namespace HappyPhoton.Tests;

public sealed class RestoreCrashEquivalenceTests
{
    [Fact]
    public void OrdinaryMode_RejectsAnUnclassifiedDiscoveredBoundary()
    {
        var map = new RestoreCrashPathMap(["before:known"], []);
        var failure = Assert.ThrowsAny<Exception>(() =>
            map.Validate("path", ["before:known", "before:new-stage"], requireComplete: true));
        Assert.Contains("before:new-stage", failure.Message);
    }

    [Fact]
    public void CaseOnlyPath_RejectsAnExclusiveUnselectedBoundary()
    {
        string[] matching = ["before:replace:catalog.db", "after:journal-delete"];
        string[] selected = ["before:case-delete:presets/first.json", "after:case-delete:presets/first.json"];
        var failure = Assert.ThrowsAny<Exception>(() =>
            RestoreCrashEquivalence.ValidateCaseOnlyCoverage("case-only",
                [.. matching, .. selected, "before:new-case-only-stage"], selected, matching));
        Assert.Contains("before:new-case-only-stage", failure.Message);
    }

    [Fact]
    public void RemovedBoundary_RejectsAStaleMap()
    {
        var map = new RestoreCrashPathMap(["before:removed"], []);
        var failure = Assert.ThrowsAny<Exception>(() =>
            map.Validate("path", ["before:known"], requireComplete: false));
        Assert.Contains("before:removed", failure.Message);
    }

    [Fact]
    public void EquivalentBoundary_MustBeKeptAndPrecedeThePrunedPoint()
    {
        var map = new RestoreCrashPathMap(["after:known"], new() { ["before:known"] = "after:known" });
        var failure = Assert.ThrowsAny<Exception>(() =>
            map.Validate("path", ["before:known", "after:known"], requireComplete: true));
        Assert.Contains("after:known must precede before:known", failure.Message);
    }

    [Fact]
    public void CacheThumbsDeletion_CannotBeMappedToItsBeforeBoundary()
    {
        var map = new RestoreCrashPathMap(["before:cache:thumbs"],
            new() { ["after:cache:thumbs"] = "before:cache:thumbs" });
        var failure = Assert.ThrowsAny<Exception>(() =>
            map.Validate("path", ["before:cache:thumbs", "after:cache:thumbs"], requireComplete: true));
        Assert.Contains("must not be mapped as equal", failure.Message);
    }
}
