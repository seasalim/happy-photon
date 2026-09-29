using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncProfileTransferTests
{
    private static readonly PhotoCameraFacts Canon = new(new("Canon", "EOS 6D"), false);

    internal static RawProfileSelection Profile(RawProfileSource source = RawProfileSource.UserFile) => new()
    {
        Source = source, Location = source == RawProfileSource.Embedded ? null : "profile.dcp",
        ContentHash = new string('a', 64)
    };

    [Theory]
    [InlineData("canon-eos-6d-iso-6400.cr2")]
    [InlineData("canon-eos-350d.cr2")]
    [InlineData("fujifilm-x30.raf")]
    [InlineData("nikon-d70-burst-1.nef")]
    [InlineData("nikon-d70-burst-2.nef")]
    [InlineData("nikon-d300-colorchecker.nef")]
    [InlineData("pentax-k-r.dng")]
    public void AC5_ReaderIdentityEqualsDecoderAcrossFixtureMakers(string name)
    {
        var file = new ImageFile(GoldenTestPaths.Asset(name));
        SyncProfileGateSupport.RequireLocal(file.FilePath);
        var reader = new PhotoFrameFactsReader(new SourceAvailabilityService());
        var opens = 0;
        reader.Opening = (_, _) => opens++;
        var facts = Assert.IsType<PhotoCameraFacts>(reader.ReadCamera(file));
        using var decoded = new RawBaseLoader().LoadPreviewBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(decoded);
        Assert.Equal(decoded.Info.CameraIdentity, facts.Identity);
        Assert.Equal(decoded.Info.CameraIdentity!.Normalized, facts.Identity!.Normalized);
        Assert.Equal(decoded.Info.IsMonochrome, facts.IsMonochrome);
        Assert.NotNull(reader.Read(file, new EditSettings { Lens = new() { Distortion = false, ChromaticAberration = false } }));
        Assert.Equal(1, opens);
    }

    [Theory]
    [InlineData(RawProfileSource.UserFile)]
    [InlineData(RawProfileSource.Adobe)]
    public void SameNormalizedCameraReceivesExternalProfile(RawProfileSource kind)
    {
        var source = new PhotoProfileSnapshot("source.cr2", true, Canon);
        var target = new ImageFile("target.cr2");
        var reader = new PhotoFrameFactsReader(new SourceAvailabilityService());
        reader.RememberCamera(target, new(new("CANON", "Canon EOS-6D"), false));
        var skips = new Dictionary<string, string>();
        var settings = new EditSettings { RawProfile = Profile(kind) };
        var groups = ProfileSettingsTransfer.CompatibleGroups(source, settings, target, Groups(),
            reader, skips, false);
        Assert.Equal(2, groups.Length);
        Assert.Empty(skips);
    }

    [Fact]
    public void AC2_EmbeddedOnlyTransfersToVersionsOfSameFileWithoutReads()
    {
        var reader = new PhotoFrameFactsReader(new SourceAvailabilityService());
        reader.Opening = (_, _) => Assert.Fail("Embedded compatibility must not read a file.");
        var source = new PhotoProfileSnapshot("source.dng", true, null);
        var settings = new EditSettings { RawProfile = Profile(RawProfileSource.Embedded) };
        var skips = new Dictionary<string, string>();
        Assert.Equal(2, ProfileSettingsTransfer.CompatibleGroups(source, settings,
            new ImageFile("source.dng") { Version = 2 }, Groups(), reader, skips, false).Length);
        Assert.Empty(skips);
        Assert.Single(ProfileSettingsTransfer.CompatibleGroups(source, settings,
            new ImageFile("other.dng"), Groups(), reader, skips, false));
        Assert.Equal("embedded in another photo", skips["Camera Profile"]);
    }

    [Theory]
    [InlineData("not RAW", false, false)]
    [InlineData("monochrome", true, true)]
    [InlineData("facts unavailable", true, false)]
    public void IncompatibleTargetsKeepBothGroups(string reason, bool raw, bool mono)
    {
        var target = new ImageFile(raw ? "target.dng" : "target.jpg");
        var reader = new PhotoFrameFactsReader(new SourceAvailabilityService());
        reader.RememberCamera(target, new(null, mono));
        var skips = new Dictionary<string, string>();
        var settings = new EditSettings
        {
            RawProfile = Profile(), Lens = new() { ProfileOverride = SyncProfileGateSupport.Lens }
        };
        Assert.Empty(ProfileSettingsTransfer.CompatibleGroups(new("source.cr2", true, Canon),
            settings, target, Groups(), reader, skips, false));
        Assert.Equal(reason, skips["Camera Profile"]);
        Assert.Equal(reason, skips["Lens Profile"]);
    }

    [Fact]
    public void AC3_AC6_BuiltInAndAutomaticNeedNoFactsForAnyRaw()
    {
        var reader = new PhotoFrameFactsReader(new SourceAvailabilityService());
        reader.Opening = (_, _) => Assert.Fail("Neutral selections must not open sources.");

        foreach (var path in new[] { "canon.cr2", "nikon.nef", "mono.dng" })
        {
            var skips = new Dictionary<string, string>();
            Assert.Equal(2, ProfileSettingsTransfer.CompatibleGroups(new("missing.cr2", true, null),
                new EditSettings(), new ImageFile(path), Groups(), reader, skips, false).Length);
            Assert.Empty(skips);
        }
    }

    [Fact]
    public void MissingSourceIdentitySkipsCameraWithoutReadingTarget()
    {
        var reader = new PhotoFrameFactsReader(new SourceAvailabilityService());
        reader.Opening = (_, _) => Assert.Fail("A target cannot repair missing copy-time identity.");
        var skips = new Dictionary<string, string>();
        Assert.Empty(ProfileSettingsTransfer.CompatibleGroups(new("source.cr2", true, null),
            new() { RawProfile = Profile() }, new ImageFile("target.cr2"),
            Groups().Where(group => group.Name == "Camera Profile").ToArray(), reader, skips, false));
        Assert.Equal("facts unavailable", skips["Camera Profile"]);
    }

    [Fact]
    public void UnreadableTargetIsOpenedAtMostOnceForBothGroups()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "missing.cr2"), "invalid RAW header");
        var reader = new PhotoFrameFactsReader(new SourceAvailabilityService());
        var opens = 0;
        reader.Opening = (_, _) => opens++;
        var skips = new Dictionary<string, string>();
        Assert.Empty(ProfileSettingsTransfer.CompatibleGroups(new("source.cr2", true, Canon),
            new() { RawProfile = Profile(), Lens = new() { ProfileOverride = SyncProfileGateSupport.Lens } },
            new ImageFile(Path.Combine(directory.Path, "missing.cr2")), Groups(), reader, skips, false));
        Assert.Equal(1, opens);
        Assert.Equal(2, skips.Count);
        Assert.All(skips.Values, reason => Assert.Equal("facts unavailable", reason));
    }

    internal static EditSettingsGroup[] Groups() => EditSettingsTransfer.Groups
        .Where(group => group.Name is "Camera Profile" or "Lens Profile").ToArray();
}
