using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncSpotFactsTests
{
    [Fact]
    public void MonochromeHeaderAndDecodedFactsRetainIdentity()
    {
        var path = Environment.GetEnvironmentVariable("HAPPY_PHOTON_LOOKS_MONOCHROME_FIXTURE") ??
            Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "compatibility-fixtures", "m2462362.DNG");
        Assert.SkipWhen(!File.Exists(path), "Set HAPPY_PHOTON_LOOKS_MONOCHROME_FIXTURE to the reviewed local RAW fixture.");

        SyncProfileGateSupport.RequireLocal(path);
        var file = new ImageFile(path);
        var reader = new PhotoFrameFactsReader(new SourceAvailabilityService());
        var facts = Assert.IsType<PhotoCameraFacts>(reader.ReadCamera(file));
        Assert.True(facts.IsMonochrome);
        Assert.False(string.IsNullOrWhiteSpace(facts.Identity?.Normalized));

        using var decoded = new GatedBaseImageLoader(new RawBaseLoader(), new SourceAvailabilityService())
            .LoadPreviewBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(decoded);
        Assert.True(decoded.Info.IsMonochrome);
        Assert.Null(decoded.Info.CameraIdentity);
        Assert.Equal(facts, decoded.Info.CameraFacts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSerialReadRetriesAndSuccessfulReadsStayFrozen(bool failTarget)
    {
        var reader = new PhotoFrameFactsReader(new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        var source = new ImageFile("source.jpg");
        var target = new ImageFile("target.jpg");
        var camera = new PhotoCameraFacts(new("Canon", "EOS 6D"), false);
        var frame = new PhotoSensorFrame(16, 12, 1);
        reader.RememberCamera(target, camera);
        reader.RememberSensorFrame(target, frame);
        var snapshot = new PhotoSpotSnapshot(source, camera, frame);
        var attempts = new Dictionary<string, int>();
        reader.SerialReader = path =>
        {
            attempts[path] = attempts.GetValueOrDefault(path) + 1;

            if (path == (failTarget ? target.FilePath : source.FilePath) && attempts[path] == 1)
            {
                throw new IOException("Transient header failure");
            }

            return "233054000882";
        };
        Assert.Equal("facts unavailable", snapshot.Compatibility(target, reader, false, out _, out _));
        Assert.Null(snapshot.Compatibility(target, reader, false, out _, out _));
        reader.SerialReader = _ => throw new IOException("Successful reads must remain frozen");
        Assert.Null(snapshot.Compatibility(target, reader, false, out _, out _));
        Assert.Equal(failTarget ? 1 : 2, attempts[source.FilePath]);
        Assert.Equal(failTarget ? 2 : 1, attempts[target.FilePath]);
    }

    [Fact]
    public void CacheOnlyMissDoesNotFreezeSourceFacts()
    {
        var reader = new PhotoFrameFactsReader(new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        var source = new ImageFile("source.jpg");
        var target = new ImageFile("target.jpg");
        var snapshot = new PhotoSpotSnapshot(source, null, null);
        var reads = 0;
        reader.SerialReader = _ =>
        {
            reads++;

            return "233054000882";
        };
        Assert.Equal("facts unavailable", snapshot.Compatibility(target, reader, true, out _, out _));
        Assert.Equal(0, reads);
        var camera = new PhotoCameraFacts(new("Canon", "EOS 6D"), false);
        reader.RememberCamera(source, camera);
        reader.RememberSensorFrame(source, new(16, 12, 8));
        reader.RememberCamera(target, camera);
        reader.RememberSensorFrame(target, new(16, 12, 6));
        Assert.Null(snapshot.Compatibility(target, reader, false, out var from, out var to));
        Assert.Equal(8, from);
        Assert.Equal(6, to);
        Assert.Equal(2, reads);
        reader.RememberCamera(source, new(new("Nikon", "D70"), false));
        reader.RememberSensorFrame(source, new(8, 6, 1));
        Assert.Null(snapshot.Compatibility(target, reader, true, out from, out to));
        Assert.Equal(8, from);
        Assert.Equal(6, to);
        Assert.Equal(2, reads);
    }
}
