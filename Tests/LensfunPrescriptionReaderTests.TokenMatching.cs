using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LensfunPrescriptionReaderTests
{
    [Fact]
    public void TokenMatcherAllowsReorderingAndIgnoresDuplicateTokens()
    {
        WriteDatabase(Lens(
            "Tokina 12-24mm f/4 AT-X 124 AF Pro DX", "Mount A"));
        var database = new LensfunDatabase(_directory);

        var match = database.Resolve(
            "Camera Co", "Model One",
            "Tokina AT-X 124 AF PRO DX (AF 12-24mm f/4)",
            12, 4, 6000, 4000);
        var differentContent = database.Resolve(
            "Camera Co", "Model One",
            "Tokina AT-X 124 AF PRO DX (AF 12-24mm f/2.8)",
            12, 4, 6000, 4000);

        Assert.Equal("Tokina 12-24mm f/4 AT-X 124 AF Pro DX", match?.LensName);
        Assert.Null(differentContent);
    }

    [Fact]
    public void TokenMatcherPreservesMakerPrefixAndCalibrationSuffixRules()
    {
        WriteDatabase(
            Lens("Lens Co Alpha Zoom", "Mount A") +
            Lens("Gamma Prime 123", "Mount A"));
        var database = new LensfunDatabase(_directory);

        var makerPrefix = database.Resolve(
            "Camera Co", "Model One", "Zoom Alpha", 24, 4, 6000, 4000);
        var calibrationSuffix = database.Resolve(
            "Camera Co", "Model One", "Prime Gamma", 24, 4, 6000, 4000);

        Assert.Equal("Lens Co Alpha Zoom", makerPrefix?.LensName);
        Assert.Equal("Gamma Prime 123", calibrationSuffix?.LensName);
    }

    [Fact]
    public void TokenMatcherRejectsAmbiguousDistinctLensModels()
    {
        WriteDatabase(
            Lens("Alpha Alpha Lens", "Mount A") +
            Lens("Alpha Lens", "Mount A"));
        var database = new LensfunDatabase(_directory);

        Assert.Null(database.Resolve(
            "Camera Co", "Model One", "Lens Alpha", 24, 4, 6000, 4000));
    }

    [Fact]
    public void ExactMatchWinsBeforeAmbiguousTokenMatches()
    {
        WriteDatabase(
            Lens("Alpha Lens", "Mount A") +
            Lens("Lens Alpha", "Mount A"));
        var database = new LensfunDatabase(_directory);

        var match = database.Resolve(
            "Camera Co", "Model One", "Alpha Lens", 24, 4, 6000, 4000);

        Assert.Equal("Alpha Lens", match?.LensName);
    }

    [Fact]
    public void NonEmptyAmbiguousExactSetDoesNotFallBackToTokens()
    {
        WriteDatabase(
            Lens("Lens f/4", "Mount A") +
            Lens("Lens f4", "Mount A"));
        var database = new LensfunDatabase(_directory);

        Assert.Null(database.Resolve(
            "Camera Co", "Model One", "Lens f4", 24, 4, 6000, 4000));
    }
}
