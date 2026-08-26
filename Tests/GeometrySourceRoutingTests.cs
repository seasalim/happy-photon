using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(CheckpointCRenderGateCollection.Name)]
public sealed class GeometrySourceRoutingTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();

    public static TheoryData<string> Formats => new()
    {
        "canon-eos-350d.cr2",
        "srgb-reference.jpg",
        "reference.heic",
        "reference-16bit.tiff",
        "synthetic.png"
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public void EverySupportedSourceFormatUsesTheSameGeometryStage(string fileName)
    {
        if (Path.GetExtension(fileName).Equals(".heic", StringComparison.OrdinalIgnoreCase))
        {
            var heic = MagickFormatInfo.Create(MagickFormat.Heic);
            Assert.SkipWhen(heic is not { SupportsReading: true },
                "HEIC geometry routing skipped because this build has no HEIC reader.");
        }
        var path = fileName == "synthetic.png"
            ? WritePng()
            : GoldenTestPaths.Asset(fileName);
        var loader = new BaseLoaderRouter(
            new RawBaseLoader(),
            new StandardBaseLoader());
        using var baseImage = loader.LoadFullBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        Assert.NotNull(baseImage);
        var passes = 0;
        GeometryWarpProcessor.SamplingPassStarted = () => passes++;
        try
        {
            using var corrected = RenderGeometry.Apply(
                baseImage!.Pixels,
                new EditSettings
                {
                    HorizonRotation = 3,
                    Geometry = new GeometrySettings
                    {
                        Vertical = 35,
                        Horizontal = -28,
                        Aspect = 22,
                        Distortion = -45
                    }
                },
                out var trace);

            Assert.Equal(1, passes);
            Assert.Equal((int)corrected.Width, trace.Width);
            Assert.Equal((int)corrected.Height, trace.Height);
            Assert.True(corrected.Width <= baseImage.Pixels.Width);
            Assert.True(corrected.Height <= baseImage.Pixels.Height);
        }
        finally
        {
            GeometryWarpProcessor.SamplingPassStarted = null;
        }
    }

    private string WritePng()
    {
        var path = Path.Combine(_directory.Path, "synthetic.png");
        if (File.Exists(path)) return path;
        using var image = new MagickImage(MagickColors.CornflowerBlue, 64, 48);
        image.Write(path);
        return path;
    }

    public void Dispose() => _directory.Dispose();
}
