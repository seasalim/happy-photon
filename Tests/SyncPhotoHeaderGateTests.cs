using System.Diagnostics;
using System.Security.Cryptography;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncPhotoHeaderGateTests(ITestOutputHelper output)
{
    [Fact]
    public void G4()
    {
        SyncPhotoGateSupport.RequirePerformance();
        AssertFileHash("Services/LibRawProcessingService.cs", "26E710E33CCA2899BA7EEAC4463236965034FD6DC895096605C162646CFBFADE");
        AssertFileHash("Interop/HappyPhoton.LibRaw.Interop/LibRawContext.cs", "26236B8D347B07E6D2E7BED7C9A0655C4D0742B2C0CE15D29067FFFC8E54377E");
        using var directory = new TemporaryDirectory();
        var rawSource = SyncPhotoGateSupport.LocalFixture(SyncPhotoGateSupport.Raw);
        var heicSource = SyncPhotoGateSupport.LocalFixture(SyncPhotoGateSupport.Heic);
        var otherHeic = SyncPhotoGateSupport.LocalFixture("reference.heic");
        var paths = new string[200];
        var availability = new SourceAvailabilityService();

        for (var index = 0; index < paths.Length; index++)
        {
            var source = index < 100 ? rawSource : index % 2 == 0 ? heicSource : otherHeic;
            Assert.Equal(SourceAvailability.AvailableLocally, availability.GetAvailability(source));
            paths[index] = Path.Combine(directory.Path, $"cold-{index:D3}{Path.GetExtension(source)}");
            File.Copy(source, paths[index]);
        }

        // Cold means no prior header-reader call for each distinct path; OS cache
        // eviction is not claimed (copying necessarily touches source bytes).
        var raw = new LibRawProcessingService();
        Assert.True(raw.IsAvailable);
        var samples = new double[200];
        var headerCalls = 0;
        var portrait = 0;

        for (var index = 0; index < paths.Length; index++)
        {
            Assert.Equal(SourceAvailability.AvailableLocally, availability.GetAvailability(paths[index]));
            var start = Stopwatch.GetTimestamp();
            headerCalls++;

            if (index < 100)
            {
                var metadata = raw.ExtractMetadata(paths[index]);
                samples[index] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Assert.NotNull(metadata);
                Assert.True(metadata.PixelWidth > metadata.PixelHeight);
            }
            else
            {
                using var image = new MagickImage();
                image.Ping(paths[index]);
                samples[index] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Assert.True(image.Width > 0 && image.Height > 0);
                var turned = image.Orientation is OrientationType.LeftTop or OrientationType.RightTop
                    or OrientationType.RightBottom or OrientationType.LeftBottom;
                var isPortrait = turned ? image.Width > image.Height : image.Height > image.Width;
                if (isPortrait) portrait++;

                output.WriteLine($"HEADER fixture={Path.GetFileName(index % 2 == 0 ? heicSource : otherHeic)} width={image.Width} height={image.Height} orientation={image.Orientation}");
            }
        }

        Assert.Equal(200, headerCalls);
        Assert.True(portrait > 0, "The frozen HEIC set must contain portrait-oriented frames.");
        output.WriteLine($"SYNC_PHOTO gate=G4 pid={Environment.ProcessId} rawMedianMs={Median(samples[..100]):R} heicMedianMs={Median(samples[100..]):R} headerCalls={headerCalls} portrait={portrait} nativeOpenCount=unobserved nativeDecodeCount=unobserved");
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);

        return (values[49] + values[50]) / 2;
    }

    private static void AssertFileHash(string relative, string expected)
    {
        using var stream = File.OpenRead(Path.Combine(GoldenTestPaths.RepositoryRoot, relative));
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(stream)));
    }
}
