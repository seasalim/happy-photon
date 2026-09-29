using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncPhotoFrameContractTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("pentax-k-r.dng", 1)]
    [InlineData("synthetic", 1)]
    [InlineData("synthetic", 6)]
    [InlineData("rejected-warp", 1)]
    [InlineData("rejected-warp", 6)]
    [InlineData(SyncPhotoGateSupport.Raw, 1)]
    [InlineData(SyncPhotoGateSupport.Heic, 1)]
    [InlineData("reference.heic", 1)]
    public void HeaderFrameMatchesLoadedCorrectedFrame(string fixture, int orientation)
    {
        using var directory = new TemporaryDirectory();
        var path = fixture is "synthetic" or "rejected-warp"
            ? SyntheticRawDngFactory.Write(directory.Path, new SyntheticRawDngOptions
            {
                Orientation = (ushort)orientation,
                WarpKr1 = fixture == "rejected-warp" ? 1000 : 0,
                VignetteK0 = 0
            })
            : GoldenTestPaths.Asset(fixture);
        var availability = new SourceAvailabilityService();
        Assert.Equal(SourceAvailability.AvailableLocally, availability.GetAvailability(path));
        var file = new ImageFile(path);
        var reader = new PhotoFrameFactsReader(availability);
        var opens = new List<string>();
        reader.Opening = (_, reason) => opens.Add(reason);

        foreach (var enabled in new[] { false, true })
        {
            var settings = new EditSettings
            {
                Lens = new() { Distortion = enabled, ChromaticAberration = false, Vignetting = false }
            };
            var size = reader.Read(file, settings)!.Value;
            using var loaded = new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader())
                .LoadFullBase(file, BaseDecodeSettings.From(settings), CancellationToken.None);
            Assert.NotNull(loaded);
            output.WriteLine($"FRAME fixture={fixture} enabled={enabled} facts={size} render={loaded.Pixels.Width}x{loaded.Pixels.Height}");
            Assert.InRange(Math.Abs(loaded.Pixels.Height * size.Width / (double)size.Height - loaded.Pixels.Width), 0, 1);

            if (file.IsRaw && !enabled)
            {
                using var native = LibRawContext.Open(path);
                var frame = native.GetDimensions();
                var expected = frame.Orientation is >= 5 and <= 8
                    ? ((int)frame.VisibleHeight, (int)frame.VisibleWidth)
                    : ((int)frame.VisibleWidth, (int)frame.VisibleHeight);
                Assert.Equal(expected, size);
            }

            if (fixture == "rejected-warp")
            {
                Assert.Equal((loaded.Pixels.Width, loaded.Pixels.Height), ((uint)size.Width, (uint)size.Height));
            }

            settings.Rotation = 90;
            Assert.Equal((size.Height, size.Width), reader.Read(file, settings));
        }

        Assert.Equal(file.Extension == ".dng" ? new[] { "frame", "DNG output window" } : ["frame"], opens);
    }
}
