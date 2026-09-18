using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LensPickerTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();
    private const string Nikon20 = "Nikon AF Nikkor 20mm f/2.8D";
    private const string Nikon24 = "Nikon AF Nikkor 24mm f/2.8D";
    private const string Samyang20 = "Samyang 20mm f/1.8 ED AS UMC";

    [Theory]
    [InlineData(0x1C48303024241200ul, Nikon20)]
    [InlineData(0x4A583030140C4D02ul, Samyang20)]
    public void D750CompositeIdentityReachesAllThreeClasses(ulong id, string expected)
    {
        var result = Read(null, Identity(id));
        Assert.Equal(LensPrescriptionStatus.Available, result.Status);
        Assert.Equal(expected, result.Prescription!.LensName);
        AssertAllClasses(result.Prescription.Summary);
        Assert.False(result.Summary!.IsManual);
    }

    [Theory]
    [InlineData("AF Nikkor 20mm f/2.8", Nikon20)]
    [InlineData("Rokinon 20mm f/1.8 ED AS UMC", Samyang20)]
    public void ExifOnlyAliasWorksWithoutMakerNote(string name, string expected)
        => Assert.Equal(expected, Read(name, null).Prescription?.LensName);

    [Fact]
    public void EveryExactCandidateWinsBeforeAnEarlierAlias()
    {
        Assert.Equal(Samyang20, Read("AF Nikkor 20mm f/2.8",
            Identity(0, Samyang20)).Prescription?.LensName);
        Assert.Equal("Nikon AF Nikkor 50mm f/1.8D",
            Read("AF Nikkor 20mm f/2.8", Identity(0x7658505014147A02,
                "Rokinon 20mm f/1.8 ED AS UMC")).Prescription?.LensName);
        Assert.Equal(Nikon20, Read("Missing", Identity(0,
            "AF Nikkor 20mm f/2.8")).Prescription?.LensName);
    }

    [Fact]
    public void AliasesKeepExifThenTransmittedThenDerivedOrder()
    {
        Assert.Equal(Nikon20, Read("AF Nikkor 20mm f/2.8",
            Identity(0x4A583030140C4D02, "Rokinon 20mm f/1.8 ED AS UMC")).Prescription?.LensName);
        Assert.Equal(Nikon20, Read("Missing", Identity(0x4A583030140C4D02,
            "AF Nikkor 20mm f/2.8")).Prescription?.LensName);
    }

    [Fact]
    public void CompatibleMenuConsolidatesLogicalIdentityAndReusesItsList()
    {
        var database = new LensfunDatabase(Path.Combine(
            GoldenTestPaths.RepositoryRoot, "data", "lensfun"));
        var choices = database.ListCompatibleLenses("Nikon", "D750");
        Assert.NotNull(choices.Camera);
        Assert.Contains(Nikon20, choices.Lenses);
        Assert.Single(choices.Lenses, lens => lens == Nikon24);
        Assert.DoesNotContain(choices.Lenses, lens => lens.EndsWith("f/2.8D 54"));
        Assert.DoesNotContain(choices.Lenses, lens => lens.StartsWith("Canon EF"));
        // The camera maker's lenses lead, then everyone else; both blocks sorted.
        var nikon = choices.Lenses.TakeWhile(lens =>
            lens.StartsWith("Nikon ", StringComparison.Ordinal)).ToArray();
        Assert.True(nikon.Length > 100, $"Nikon block has {nikon.Length} entries.");
        Assert.Equal(choices.Lenses.Count(lens =>
            lens.StartsWith("Nikon ", StringComparison.Ordinal)), nikon.Length);
        Assert.Equal(nikon.Order(StringComparer.OrdinalIgnoreCase), nikon);
        var others = choices.Lenses.Skip(nikon.Length).ToArray();
        Assert.Equal(others.Order(StringComparer.OrdinalIgnoreCase), others);
        // Every name carries its maker, including Sigma entries whose Lensfun
        // model string omits it, and the prefixed name resolves like the bare one.
        Assert.All(choices.Lenses, lens => Assert.True(char.IsLetter(lens[0]), lens));
        const string sigma = "Sigma 105mm F1.4 DG HSM | Art";
        Assert.Contains(sigma, choices.Lenses);
        Assert.Equal(
            database.Resolve("Nikon", "D750", "105mm F1.4 DG HSM | Art", 105, 1.4, 6016, 4016)?.LensName,
            database.Resolve("Nikon", "D750", sigma, 105, 1.4, 6016, 4016)?.LensName);
        Assert.Same(choices.Lenses, database.ListCompatibleLenses("Nikon", "D750").Lenses);
        Assert.Null(database.ListCompatibleLenses("Unknown", "Camera").Camera);
    }

    [Fact]
    public void ManualLogicalLensUsesTheSameCropRankAsAutomatic()
    {
        var metadata = Metadata(Nikon24) with { Model = "D70", NormalizedModel = "D70" };
        var dimensions = new LibRawDimensions(0, 0, 3008, 2000, 0, 0, 1);
        var file = new ImageFile("missing.nef");
        var automatic = RawBaseLoader.ReadLensPrescription(file, metadata, null, dimensions);
        var manual = RawBaseLoader.ReadLensPrescription(file,
            metadata with { Lens = "Wrong lens" }, null, dimensions, Nikon24);
        Assert.Equal("Nikon AF Nikkor 24mm f/2.8D 54", automatic.Prescription?.LensName);
        Assert.Equal(automatic.Prescription?.LensName, manual.Prescription?.LensName);
        Assert.Equal(automatic.Prescription?.LensfunDistortion?.RadiusScale,
            manual.Prescription?.LensfunDistortion?.RadiusScale);
        Assert.Equal(automatic.Prescription?.LensfunDistortion?.Coefficients,
            manual.Prescription?.LensfunDistortion?.Coefficients);
        Assert.True(manual.Summary!.IsManual);
        Assert.Equal(Nikon24, manual.Summary.LensName);
    }

    [Fact]
    public void OverrideReplacesAutomaticCandidatesAndCanReturnToAutomatic()
    {
        var identity = Identity(0x1C48303024241200);
        var manual = Read(Nikon20, identity, Samyang20);
        Assert.Equal(Samyang20, manual.Prescription?.LensName);
        Assert.True(manual.Summary!.IsManual);
        Assert.Equal(LensPrescriptionStatus.None, Read(Nikon20, identity, "Missing").Status);
        Assert.Equal(Nikon20, Read(Nikon20, identity).Prescription?.LensName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmbeddedClassesKeepPrecedenceAndAlwaysYieldChoices(bool complete)
    {
        var path = SyntheticRawDngFactory.Write(_root.Path, new SyntheticRawDngOptions
        {
            Make = "Nikon", Model = "D750", IncludeChromaticAberration = complete
        });
        var embedded = new DngLensPrescriptionReader().Read(path).Prescription!;
        var result = RawBaseLoader.ReadLensPrescription(new ImageFile(path),
            Metadata(null), null, new LibRawDimensions(0, 0, 608, 448, 0, 0, 1), Nikon20);
        var expectedWarp = Assert.Single(embedded.Warps);
        var actualWarp = Assert.Single(result.Prescription!.Warps);
        Assert.Equal(expectedWarp.Planes, actualWarp.Planes);
        Assert.Equal(expectedWarp.CenterX, actualWarp.CenterX);
        Assert.Equal(expectedWarp.CenterY, actualWarp.CenterY);
        Assert.Equal(embedded.Vignettes, result.Prescription.Vignettes);
        Assert.Null(result.Prescription.LensfunDistortion);
        Assert.Null(result.Prescription.LensfunVignette);
        if (complete) Assert.Null(result.Prescription.LensfunTca);
        else Assert.NotNull(result.Prescription.LensfunTca);
        AssertAllClasses(result.Summary!);
        Assert.Contains(Nikon20, result.Summary!.CompatibleLenses);
        Assert.NotNull(result.Summary.Camera);
    }

    [Fact]
    public void FirstCompleteEmbeddedRawDecodeStillPublishesPickerSummary()
    {
        var path = SyntheticRawDngFactory.Write(_root.Path, new SyntheticRawDngOptions
        {
            Make = "Nikon", Model = "D750", IncludeChromaticAberration = true
        });
        using var image = new RawBaseLoader().LoadPreviewBase(new ImageFile(path),
            BaseDecodeSettings.Default, CancellationToken.None);
        var summary = Assert.IsType<LensPrescriptionSummary>(image?.Info.LensPrescriptionSummary);
        AssertAllClasses(summary);
        Assert.Contains(Nikon20, summary.CompatibleLenses);
        Assert.Equal("DNG OPCODES", summary.Source);
        Assert.False(summary.IsManual);
    }

    [Fact]
    public async Task OverrideRoundTripsButNeverTransfersOrEntersPresets()
    {
        var baseline = new EditSettings();
        var edited = baseline.Clone();
        edited.Lens.ProfileOverride = Nikon20;
        Assert.True(edited.HasEdits);
        Assert.False(baseline.HasSameEdits(edited));
        Assert.True(edited.HasSameEdits(edited.Clone()));
        var loaded = EditSettingsJson.Deserialize(EditSettingsJson.Serialize(edited), out _);
        Assert.Equal(Nikon20, loaded.Lens.ProfileOverride);
        Assert.Equal("Optics: lens profile", EditHistoryLabel.Derive(baseline, loaded));
        Assert.Null(EditSettingsTransfer.CopySubset(edited).Lens.ProfileOverride);
        var target = new EditSettings { Lens = new LensSettings { ProfileOverride = Samyang20 } };
        EditSettingsTransfer.ApplySubset(edited, target);
        Assert.Equal(Samyang20, target.Lens.ProfileOverride);
        var presets = new PresetService(_root.Path);
        await presets.InitializeAsync();
        var preset = await presets.SaveUserPresetAsync("Lens test", edited);
        Assert.Null(preset.Settings.Lens.ProfileOverride);
        var reloaded = new PresetService(_root.Path);
        await reloaded.InitializeAsync();
        Assert.Null(Assert.Single(reloaded.UserPresets).Settings.Lens.ProfileOverride);
        loaded.Lens.RestoreBaseline();
        Assert.Null(loaded.Lens.ProfileOverride);
        Assert.False(loaded.HasEdits);
        Assert.DoesNotContain("profileOverride", EditSettingsJson.Serialize(loaded));
    }

    [Fact]
    public void OverrideNamesJoinDecodeIdentityWithoutDelimiterCollisions()
    {
        var settings = new EditSettings();
        var automatic = BaseDecodeSettings.From(settings);
        settings.Lens.ProfileOverride = Nikon20;
        var first = BaseDecodeSettings.From(settings);
        settings.Lens.ProfileOverride = Samyang20;
        var second = BaseDecodeSettings.From(settings);
        Assert.Equal(Nikon20, first.LensProfileOverride);
        Assert.Equal(3, new[] { automatic.CacheKey, first.CacheKey, second.CacheKey }.Distinct().Count());
        Assert.NotEqual(first, second);
        Assert.Contains("%3B", (first with { LensProfileOverride = "name;dcp=token" }).CacheKey);
    }

    private static void AssertAllClasses(LensPrescriptionSummary summary)
    {
        Assert.True(summary.HasDistortion);
        Assert.True(summary.HasChromaticAberration);
        Assert.True(summary.HasVignetting);
    }

    private static LensPrescriptionReadResult Read(string? lens, LibRawLensIdentity? identity,
        string? profileOverride = null) => RawBaseLoader.ReadLensPrescription(
            new ImageFile("missing.nef"), Metadata(lens), identity,
            new LibRawDimensions(0, 0, 6016, 4016, 0, 0, 1), profileOverride);

    private static LibRawMetadata Metadata(string? lens) => new(
        "NIKON CORPORATION", "NIKON D750", "Nikon", "D750", lens,
        100, 0.01f, 8, 20, null, null, 1, new LibRawGpsFacts(false, null, null, null));

    private static LibRawLensIdentity Identity(ulong id, string? lens = null) => new(
        id, lens, 0, LibRawLensMounts.NikonF, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, null, 0, null, 0, null);

    public void Dispose() => _root.Dispose();
}
