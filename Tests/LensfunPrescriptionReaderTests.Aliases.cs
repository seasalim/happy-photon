using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LensfunPrescriptionReaderTests
{
    [Fact]
    public void EnglishLensAliasesMatchEveryExistingIdentityTier()
    {
        WriteDatabase(
            LensWithAlias("Primary Exact", "Exact Alias", "Mount A") +
            LensWithAlias("Primary Database Prefix", "Lens Co Wide Alias", "Mount A") +
            LensWithAlias("Primary Supplied Prefix", "Tele Alias", "Mount A") +
            LensWithAlias("Primary Variant", "Prime Alias 123", "Mount A") +
            LensWithAlias("Primary Tokens", "Alpha Zoom 24-70 f/2.8", "Mount A"));
        var database = new LensfunDatabase(_directory);

        Assert.Equal("Primary Exact", Resolve("Exact Alias")?.LensName);
        Assert.Equal("Primary Database Prefix", Resolve("Wide Alias")?.LensName);
        Assert.Equal("Primary Supplied Prefix", Resolve("Lens Co Tele Alias")?.LensName);
        Assert.Equal("Primary Variant", Resolve("Prime Alias")?.LensName);
        Assert.Equal("Primary Tokens", Resolve("Zoom Alpha f/2.8 24-70")?.LensName);

        LensfunResolvedProfile? Resolve(string model) => database.Resolve(
            "Camera Co", "Model One", model, 24, 4, 6000, 4000);
    }

    [Fact]
    public void EnglishCameraAliasMatchesWithMakerPrefix()
    {
        WriteDatabase(
            Lens("Exact Lens", "Mount A"), cameraAlias: "Alternate Model");
        var database = new LensfunDatabase(_directory);

        var exact = database.Resolve(
            "Camera Co", "Alternate Model", "Exact Lens", 24, 4, 6000, 4000);
        var match = database.Resolve(
            "Camera Co", "Camera Co Alternate Model", "Exact Lens",
            24, 4, 6000, 4000);

        Assert.Equal("Exact Lens", exact?.LensName);
        Assert.Equal("Exact Lens", match?.LensName);
    }

    [Fact]
    public void PrimaryExactMatchRemainsTerminalAcrossAliasCollision()
    {
        var database = new LensfunDatabase(Path.Combine(
            AppContext.BaseDirectory, "data", "lensfun"));

        var match = database.Resolve(
            "Sony", "ILCE-6000", "Sigma 30mm f/2.8 EX DN",
            30, 2.8, 6000, 4000);

        Assert.Equal("Sigma 30mm f/2.8 EX DN", match?.LensName);
    }

    [Fact]
    public void PrimaryTokenMatchPrecedesAliasExactMatch()
    {
        WriteDatabase(
            LensWithAlias("Alpha Lens", "Unused Alias", "Mount A") +
            LensWithAlias("Second Lens", "Lens Alpha", "Mount A"));
        var database = new LensfunDatabase(_directory);

        var match = database.Resolve(
            "Camera Co", "Model One", "Lens Alpha", 24, 4, 6000, 4000);

        Assert.Equal("Alpha Lens", match?.LensName);
    }

    [Fact]
    public void RealSnapshotPrimaryTokenMatchPrecedesAliasCollision()
    {
        var database = new LensfunDatabase(Path.Combine(
            AppContext.BaseDirectory, "data", "lensfun"));

        var match = database.Resolve(
            "Nikon Corporation", "Nikon D750",
            "Nikkor AF-S 200-500mm f/5.6E ED VR",
            200, 5.6, 6000, 4000);

        Assert.Equal(
            "Nikon AF-S Nikkor 200-500mm f/5.6E ED VR",
            match?.LensName);
    }

    [Fact]
    public void RealSnapshotAliasSelectsSameCropCalibrationAsPrimary()
    {
        var database = new LensfunDatabase(Path.Combine(
            AppContext.BaseDirectory, "data", "lensfun"));

        var primary = database.Resolve(
            "Canon", "Canon EOS 350D DIGITAL",
            "Tamron AF 70-300mm F4-5.6 LD Macro 1:2",
            70, 11, 3456, 2304);
        var alias = database.Resolve(
            "Canon", "Canon EOS 350D DIGITAL",
            "Tamron AF 70-300mm f/4-5.6 Di LD Tele-Macro (1:2)",
            70, 11, 3456, 2304);

        AssertProfilesEqual(primary, alias);
    }

    [Fact]
    public void AliasMatchIncludesPrimarySiblingsWithoutTheAlias()
    {
        var aliasLens = LensWithAlias(
                "Shared Primary", "Subset Alias", "Mount A")
            .Replace("<cropfactor>1.5</cropfactor>",
                "<cropfactor>1.0</cropfactor>", StringComparison.Ordinal);
        var matchingCropLens = Lens(
            "Shared Primary", "Mount A",
            "<distortion model=\"poly3\" focal=\"24\" k1=\"0.2\"/>");
        WriteDatabase(aliasLens + matchingCropLens);
        var database = new LensfunDatabase(_directory);

        var primary = database.Resolve(
            "Camera Co", "Model One", "Shared Primary", 24, 4, 6000, 4000);
        var alias = database.Resolve(
            "Camera Co", "Model One", "Subset Alias", 24, 4, 6000, 4000);

        AssertProfilesEqual(primary, alias);
    }

    [Fact]
    public void AliasCollisionBetweenDistinctLensesIsAmbiguous()
    {
        WriteDatabase(
            LensWithAlias("First Primary", "Shared Alias", "Mount A") +
            LensWithAlias("Second Primary", "Shared Alias", "Mount A"));
        var database = new LensfunDatabase(_directory);

        Assert.Null(database.Resolve(
            "Camera Co", "Model One", "Shared Alias", 24, 4, 6000, 4000));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("ru")]
    [InlineData("fr")]
    public void NonEnglishCameraAndLensAliasesDoNotMatch(string language)
    {
        WriteDatabase(
            LensWithAlias("Primary Lens", "Ignored Lens", "Mount A", language),
            cameraAlias: "Ignored Camera",
            cameraAliasLanguage: language);
        var database = new LensfunDatabase(_directory);

        Assert.Null(database.Resolve(
            "Camera Co", "Ignored Camera", "Primary Lens", 24, 4, 6000, 4000));
        Assert.Null(database.Resolve(
            "Camera Co", "Model One", "Ignored Lens", 24, 4, 6000, 4000));
    }

    private static string LensWithAlias(
        string model,
        string alias,
        string mount,
        string language = "en") => Lens(model, mount).Replace(
            $"<model>{model}</model>",
            $"<model>{model}</model><model lang=\"{language}\">{alias}</model>",
            StringComparison.Ordinal);

    private static void AssertProfilesEqual(
        LensfunResolvedProfile? expected,
        LensfunResolvedProfile? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected.LensName, actual.LensName);
        Assert.Equal(expected.RadiusScale, actual.RadiusScale, 12);
        Assert.Equal(expected.VignetteRadiusScale, actual.VignetteRadiusScale, 12);
        Assert.Equal(expected.CenterX, actual.CenterX, 12);
        Assert.Equal(expected.CenterY, actual.CenterY, 12);
        AssertCalibrationEqual(expected.Distortion, actual.Distortion);
        AssertCalibrationEqual(expected.Tca, actual.Tca);
        AssertCalibrationEqual(expected.Vignette, actual.Vignette);
    }

    private static void AssertCalibrationEqual(
        LensfunCalibration? expected,
        LensfunCalibration? actual)
    {
        if (expected == null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        Assert.Equal(expected.Model, actual.Model);
        Assert.Equal(expected.Focal, actual.Focal, 12);
        Assert.Equal(expected.Aperture, actual.Aperture, 12);
        Assert.Equal(expected.Distance, actual.Distance, 12);
        Assert.Equal(expected.Parameters, actual.Parameters);
    }
}
