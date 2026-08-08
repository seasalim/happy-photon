using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SourceAvailabilityServiceTests : IDisposable
{
    private const FileAttributes RecallOnOpen =
        (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess =
        (FileAttributes)0x00400000;
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-availability-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(FileAttributes.Offline)]
    [InlineData(RecallOnOpen)]
    [InlineData(RecallOnDataAccess)]
    [InlineData(FileAttributes.Offline | RecallOnOpen)]
    [InlineData(FileAttributes.Offline | RecallOnDataAccess)]
    [InlineData(RecallOnOpen | RecallOnDataAccess)]
    [InlineData(FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)]
    public void WindowsRecallAttributes_RequireHydration(
        FileAttributes attributes)
    {
        Assert.Equal(
            SourceAvailability.RequiresHydration,
            SourceAvailabilityService.ClassifyWindowsAttributes(attributes));
    }

    [Theory]
    [InlineData(FileAttributes.Normal)]
    [InlineData(FileAttributes.Archive)]
    [InlineData(FileAttributes.ReadOnly | FileAttributes.Archive)]
    public void WindowsLocalAttributes_AreAvailable(FileAttributes attributes)
    {
        Assert.Equal(
            SourceAvailability.AvailableLocally,
            SourceAvailabilityService.ClassifyWindowsAttributes(attributes));
    }

    [Fact]
    public void FolderEnumeration_CapturesAvailabilityHint()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "photo.jpg");
        File.WriteAllBytes(path, [1]);

        ImageFile image = Assert.Single(
            new FolderService().GetImagesInFolder(_root));

        var expected = OperatingSystem.IsWindows()
            ? SourceAvailability.AvailableLocally
            : SourceAvailability.Unknown;
        Assert.Equal(expected, image.SourceAvailabilityHint);
        Assert.Equal(Path.GetFullPath(path), image.FilePath);
    }

    [Fact]
    public void DirectImageConstruction_DefaultsToUnknownHint()
    {
        Assert.Equal(
            SourceAvailability.Unknown,
            new ImageFile("photo.jpg").SourceAvailabilityHint);
    }

    [Fact]
    public void MissingPath_UsesPlatformAvailabilityBehavior()
    {
        var availability = new SourceAvailabilityService().GetAvailability(
            Path.Combine(_root, "missing.jpg"));

        Assert.Equal(
            OperatingSystem.IsWindows()
                ? SourceAvailability.Unavailable
                : SourceAvailability.Unknown,
            availability);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
