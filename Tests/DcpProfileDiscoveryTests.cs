using System.Security.Cryptography;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DcpProfileDiscoveryTests
{
    [Fact]
    public async Task Discover_OrdersSourcesAndDeduplicatesByProfilePayload()
    {
        using var directory = new TemporaryDirectory();
        var embeddedBytes = SyntheticDcpFactory.Create(new SyntheticDcpOptions
        {
            Name = "Embedded label",
            UniqueCameraModel = "Canon EOS 6D",
            EmbedPolicy = 0
        });
        var userBytes = SyntheticDcpFactory.Create(new SyntheticDcpOptions
        {
            Name = "User label",
            UniqueCameraModel = "Canon EOS 6D",
            EmbedPolicy = 1
        });
        var adobeBytes = SyntheticDcpFactory.Create(new SyntheticDcpOptions
        {
            Name = "Adobe label",
            UniqueCameraModel = "Canon EOS 6D",
            EmbedPolicy = 3
        });
        var dngPath = Path.Combine(directory.Path, "image.dng");
        var userPath = Path.Combine(directory.Path, "user.dcp");
        var adobeRoot = Path.Combine(directory.Path, "Adobe");
        Directory.CreateDirectory(adobeRoot);
        File.WriteAllBytes(dngPath, embeddedBytes);
        File.WriteAllBytes(userPath, userBytes);
        File.WriteAllBytes(Path.Combine(adobeRoot, "shared.dcp"), adobeBytes);
        Assert.NotEqual(Hash(embeddedBytes), Hash(userBytes));
        Assert.NotEqual(Hash(userBytes), Hash(adobeBytes));
        var image = new ImageFile(dngPath)
        {
            EditSettings = new EditSettings
            {
                RawProfile = Selection(
                    RawProfileSource.UserFile,
                    userPath,
                    Hash(userBytes))
            }
        };
        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            adobeRoots: [adobeRoot]);

        var result = await discovery.DiscoverAsync(
            image,
            new CameraIdentity("Canon", "Canon EOS 6D"),
            CancellationToken.None);

        var selected = Assert.Single(result.Options, option => !option.IsBuiltIn);
        Assert.Equal(RawProfileSource.UserFile, selected.Selection?.Source);
        Assert.Equal("Canon EOS 6D", selected.DeclaredCameraModel);
        Assert.Equal("Built-in camera color", result.Options[^1].DisplayName);
        Assert.True(result.HasProfiles);
        Assert.True(result.AdobeScanAttempted);
        Assert.Equal(1, result.AdobeProfilesScanned);
        Assert.Equal(1, result.AdobeIdentityMatchCount);
    }

    [Fact]
    public async Task Discover_AdobeMatchingNormalizesAliasesAndSortsByName()
    {
        using var directory = new TemporaryDirectory();
        var root = Path.Combine(directory.Path, "CameraProfiles");
        Directory.CreateDirectory(root);
        SyntheticDcpFactory.WriteTemporary(root, new SyntheticDcpOptions
        {
            Name = "Zulu",
            UniqueCameraModel = "CANON-EOS_6D",
            ColorMatrix1 = [1.01, 0, 0, 0, 1, 0, 0, 0, 1]
        }, "z.dcp");
        SyntheticDcpFactory.WriteTemporary(root, new SyntheticDcpOptions
        {
            Name = "Alpha",
            UniqueCameraModel = "Canon EOS 6D"
        }, "a.dcp");
        SyntheticDcpFactory.WriteTemporary(root, new SyntheticDcpOptions
        {
            Name = "Other",
            UniqueCameraModel = "Nikon D850"
        }, "other.dcp");
        var image = new ImageFile(Path.Combine(directory.Path, "image.cr2"));
        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            adobeRoots: [root]);

        var result = await discovery.DiscoverAsync(
            image,
            new CameraIdentity("Canon", "EOS 6D"),
            CancellationToken.None);

        Assert.Equal(
            ["Alpha", "Zulu"],
            result.Options
                .Where(option => option.Selection?.Source == RawProfileSource.Adobe)
                .Select(option => option.DisplayName));
        Assert.True(result.AdobeScanAttempted);
        Assert.Equal(3, result.AdobeProfilesScanned);
        Assert.Equal(2, result.AdobeIdentityMatchCount);
    }

    [Fact]
    public async Task Discover_ReportsReadableAdobeProfilesWhenNoneMatch()
    {
        using var directory = new TemporaryDirectory();
        SyntheticDcpFactory.WriteTemporary(directory.Path, new SyntheticDcpOptions
        {
            Name = "Other body",
            UniqueCameraModel = "Nikon D850"
        });
        File.WriteAllText(
            Path.Combine(directory.Path, "not-a-profile.dcp"),
            "not a readable DCP");

        var result = await new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            adobeRoots: [directory.Path]).DiscoverAsync(
                new ImageFile(Path.Combine(directory.Path, "image.cr2")),
                new CameraIdentity("Canon", "EOS 6D"),
                CancellationToken.None);

        Assert.True(result.AdobeScanAttempted);
        Assert.Equal(1, result.AdobeProfilesScanned);
        Assert.Equal(0, result.AdobeIdentityMatchCount);
        Assert.DoesNotContain(result.Options, option =>
            option.Selection?.Source == RawProfileSource.Adobe);
    }

    [Fact]
    public async Task Discover_EmptyAdobeRootsReportsCompletedZeroScan()
    {
        using var directory = new TemporaryDirectory();
        var result = await new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            adobeRoots: [directory.Path]).DiscoverAsync(
                new ImageFile(Path.Combine(directory.Path, "image.cr2")),
                new CameraIdentity("Canon", "EOS 6D"),
                CancellationToken.None);

        Assert.True(result.AdobeScanAttempted);
        Assert.Equal(0, result.AdobeProfilesScanned);
        Assert.Equal(0, result.AdobeIdentityMatchCount);
        Assert.True(Assert.Single(result.Options).IsBuiltIn);
    }

    [Fact]
    public async Task Discover_ProbesAllAdobeFilesButFullyOpensOnlyCameraMatches()
    {
        using var directory = new TemporaryDirectory();
        SyntheticDcpFactory.WriteTemporary(directory.Path, new SyntheticDcpOptions
        {
            Name = "Matching",
            UniqueCameraModel = "Canon EOS 6D"
        }, "matching.dcp");
        SyntheticDcpFactory.WriteTemporary(directory.Path, new SyntheticDcpOptions
        {
            IncludeColorMatrix1 = false,
            UniqueCameraModel = "Nikon D850"
        }, "unmatched-invalid.dcp");
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally);

        var result = await new DcpProfileDiscovery(
            availability,
            adobeRoots: [directory.Path]).DiscoverAsync(
                new ImageFile(Path.Combine(directory.Path, "image.cr2")),
                new CameraIdentity("Canon", "EOS 6D"),
                CancellationToken.None);

        Assert.Equal("Matching", Assert.Single(
            result.Options,
            option => option.Selection?.Source == RawProfileSource.Adobe).DisplayName);
        Assert.Equal(3, availability.CallCount);
    }

    [Fact]
    public async Task Discover_WithoutIdentityDoesNotAttemptAdobeScan()
    {
        using var directory = new TemporaryDirectory();
        var root = Path.Combine(directory.Path, "CameraProfiles");
        Directory.CreateDirectory(root);
        SyntheticDcpFactory.WriteTemporary(root, new SyntheticDcpOptions
        {
            Name = "Would match",
            UniqueCameraModel = "Canon EOS 6D"
        });
        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            adobeRoots: [root]);

        var result = await discovery.DiscoverAsync(
            new ImageFile(Path.Combine(directory.Path, "image.cr2")),
            null,
            CancellationToken.None);

        Assert.False(result.HasProfiles);
        Assert.True(Assert.Single(result.Options).IsBuiltIn);
        Assert.False(result.AdobeScanAttempted);
        Assert.Equal(0, result.AdobeProfilesScanned);
        Assert.Equal(0, result.AdobeIdentityMatchCount);
    }

    [Fact]
    public async Task Discover_InvalidPersistedSelectionStaysVisibleWithoutSubstitution()
    {
        using var directory = new TemporaryDirectory();
        var path = SyntheticDcpFactory.WriteTemporary(
            directory.Path,
            new SyntheticDcpOptions { EmbedPolicy = 3 });
        var image = new ImageFile(Path.Combine(directory.Path, "image.cr2"))
        {
            EditSettings = new EditSettings
            {
                RawProfile = Selection(
                    RawProfileSource.UserFile,
                    path,
                    new string('e', 64))
            }
        };
        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            adobeRoots: []);

        var result = await discovery.DiscoverAsync(
            image,
            null,
            CancellationToken.None);

        var invalid = Assert.Single(result.Options, option => !option.IsBuiltIn);
        Assert.Equal(DcpProfileErrorCode.HashMismatch, invalid.Status);
        Assert.False(invalid.CanSelect);
        Assert.Equal(image.EditSettings.RawProfile.ContentHash,
            invalid.Selection?.ContentHash);
    }

    [Fact]
    public async Task Discover_UnavailablePersistedFileKeepsUnavailableStatus()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "online-only.dcp");
        await File.WriteAllTextAsync(path, "content must not be read");
        var image = new ImageFile(Path.Combine(directory.Path, "image.cr2"))
        {
            EditSettings = new EditSettings
            {
                RawProfile = Selection(
                    RawProfileSource.UserFile,
                    path,
                    new string('d', 64))
            }
        };

        var result = await new DcpProfileDiscovery(
            new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration),
            adobeRoots: []).DiscoverAsync(
                image,
                null,
                CancellationToken.None);

        var unavailable = Assert.Single(
            result.Options,
            option => !option.IsBuiltIn);
        Assert.Equal(DcpProfileErrorCode.Unavailable, unavailable.Status);
        Assert.Equal(image.EditSettings.RawProfile.ContentHash,
            unavailable.Selection?.ContentHash);
    }

    [Fact]
    public async Task Discover_EmbeddedOwnsDuplicateOfPersistedAdobeProfile()
    {
        using var directory = new TemporaryDirectory();
        var bytes = SyntheticDcpFactory.Create(new SyntheticDcpOptions
        {
            Name = "Duplicate",
            UniqueCameraModel = "Canon EOS 6D"
        });
        var dng = Path.Combine(directory.Path, "image.dng");
        var adobe = Path.Combine(directory.Path, "duplicate.dcp");
        File.WriteAllBytes(dng, bytes);
        File.WriteAllBytes(adobe, bytes);
        var image = new ImageFile(dng)
        {
            EditSettings = new EditSettings
            {
                RawProfile = Selection(
                    RawProfileSource.Adobe,
                    adobe,
                    Hash(bytes))
            }
        };

        var result = await new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            adobeRoots: [directory.Path]).DiscoverAsync(
                image,
                new CameraIdentity("Canon", "EOS 6D"),
                CancellationToken.None);

        var option = Assert.Single(result.Options, item => !item.IsBuiltIn);
        Assert.Equal(RawProfileSource.Embedded, option.Selection?.Source);
    }

    [Fact]
    public async Task Discover_UnavailableEmbeddedSelectionNeverReadsContent()
    {
        using var directory = new TemporaryDirectory();
        var dngPath = Path.Combine(directory.Path, "cloud.dng");
        await File.WriteAllTextAsync(dngPath, "not a DNG");
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        var image = new ImageFile(dngPath)
        {
            EditSettings = new EditSettings
            {
                RawProfile = Selection(
                    RawProfileSource.Embedded,
                    null,
                    new string('f', 64))
            }
        };

        var result = await new DcpProfileDiscovery(
            availability,
            adobeRoots: []).DiscoverAsync(
                image,
                null,
                CancellationToken.None);

        var unavailable = Assert.Single(
            result.Options,
            option => !option.IsBuiltIn);
        Assert.Equal(DcpProfileErrorCode.Unavailable, unavailable.Status);
        Assert.Equal(1, availability.CallCount);
    }

    [Fact]
    public async Task Resolution_LiveChecksHashCorruptionDeletionAndAvailability()
    {
        using var directory = new TemporaryDirectory();
        var path = SyntheticDcpFactory.WriteTemporary(
            directory.Path,
            new SyntheticDcpOptions { EmbedPolicy = 3 });
        var bytes = await File.ReadAllBytesAsync(path);
        var image = new ImageFile(Path.Combine(directory.Path, "image.cr2"));
        var selection = Selection(
            RawProfileSource.UserFile,
            path,
            Hash(bytes));
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally);
        var service = new DcpProfileService(availability);

        var active = await service.ResolveAsync(
            image, selection, true, CancellationToken.None);
        var changedBytes = SyntheticDcpFactory.Create(
            new SyntheticDcpOptions { Name = "Changed" });
        var changedHash = Hash(changedBytes);
        File.WriteAllBytes(path, changedBytes);
        var changed = await service.ResolveAsync(
            image, selection, true, CancellationToken.None);
        File.Delete(path);
        var missing = await service.ResolveAsync(
            image, selection, true, CancellationToken.None);
        availability.Availability = SourceAvailability.RequiresHydration;
        File.WriteAllBytes(path, bytes);
        var unavailable = await service.ResolveAsync(
            image, selection, true, CancellationToken.None);

        Assert.True(active.IsActive);
        Assert.Equal(DcpProfileErrorCode.HashMismatch, changed.Status);
        Assert.Contains(changedHash, changed.Token);
        Assert.Equal(DcpProfileErrorCode.Missing, missing.Status);
        Assert.Equal(DcpProfileErrorCode.Unavailable, unavailable.Status);
        Assert.NotEqual(active.Token, changed.Token);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Resolution_AllowsEmbedPolicyForProprietaryRaw(uint embedPolicy)
    {
        using var directory = new TemporaryDirectory();
        var path = SyntheticDcpFactory.WriteTemporary(
            directory.Path,
            new SyntheticDcpOptions { EmbedPolicy = embedPolicy });
        var bytes = await File.ReadAllBytesAsync(path);
        var selection = Selection(
            RawProfileSource.UserFile,
            path,
            Hash(bytes));
        var service = new DcpProfileService(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));

        var proprietary = await service.ResolveAsync(
            new ImageFile(Path.Combine(directory.Path, "image.cr2")),
            selection,
            true,
            CancellationToken.None);
        Assert.True(proprietary.IsActive);
    }

    [Fact]
    public async Task Resolution_RechecksAvailabilityImmediatelyBeforeOpen()
    {
        using var directory = new TemporaryDirectory();
        var path = SyntheticDcpFactory.WriteTemporary(
            directory.Path,
            new SyntheticDcpOptions { EmbedPolicy = 3 });
        var bytes = await File.ReadAllBytesAsync(path);
        var availability = new ChangingAvailabilityService();
        var result = await new DcpProfileService(availability).ResolveAsync(
            new ImageFile(Path.Combine(directory.Path, "image.cr2")),
            Selection(RawProfileSource.UserFile, path, Hash(bytes)),
            true,
            CancellationToken.None);

        Assert.Equal(2, availability.CallCount);
        Assert.Equal(DcpProfileErrorCode.Unavailable, result.Status);
    }

    private static RawProfileSelection Selection(
        RawProfileSource source,
        string? path,
        string hash) => new()
        {
            Source = source,
            Location = path,
            ContentHash = hash
        };

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private sealed class ChangingAvailabilityService : ISourceAvailabilityService
    {
        private int _callCount;
        internal int CallCount => Volatile.Read(ref _callCount);

        public SourceAvailability GetAvailability(string filePath) =>
            Interlocked.Increment(ref _callCount) == 1
                ? SourceAvailability.AvailableLocally
                : SourceAvailability.RequiresHydration;
    }
}
