using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class ViewportRestingPreviewTests
{
    private readonly ITestOutputHelper _output;
    public ViewportRestingPreviewTests(ITestOutputHelper output) => _output = output;

    [WindowsFact]
    public async Task RestingRender_PreservesLocalCoordinatesAcrossRightHalfCrop()
    {
        var settings = new EditSettings
        {
            Crop = new CropRegion { Left = .5, Right = 1, Top = 0, Bottom = 1 },
            Detail = new DetailSettings { CaptureSharpen = 0 },
            Locals = [new() { Cu = .25, Angle = 0, Feather = .001, Exposure = 2 }]
        };
        await using var service = CreateService(new CountingPairLoader());
        var (interactive, _) = await service.ApplyEditsToPreviewAsync(
            _image, settings, skipHistogram: true);
        using var ownedInteractive = interactive;
        var identity = service.TryGetPreviewRenderIdentity(interactive!);
        using var resting = await service.RenderRestingPreviewAsync(
            _image, settings, 120, identity!, CancellationToken.None);
        Assert.NotNull(resting);
        using var stream = new MemoryStream();
        resting.Bitmap.Save(stream);
        stream.Position = 0;
        using var actual = new MagickImage(stream);
        // Same large pixels and metadata as CountingPairLoader's retained base.
        using var large = new BaseImage(new MagickImage(MagickColors.Orange, 320, 160),
            new BaseImageInfo(BaseSourceKind.Standard, false, BaseDecodeSettings.From(settings),
                null, null, 6504, 0, false, null, 1, 400, 200));
        var pipeline = new RenderPipeline();
        using var expected = pipeline.Render(new RenderRequest(large, settings,
            RenderIntent.Preview, null, new RenderOptions(false)));
        WysiwygTests.AlignForComparison(expected.Image, actual);
        var comparison = GoldenImageComparer.Compare(expected.Image, actual, GoldenComparisonDomain.DisplaySrgb);
        _output.WriteLine($"Resting delta-E mean={comparison.MeanDeltaE:F3}, p99={comparison.P99DeltaE:F3}");
        Assert.True(comparison.MeanDeltaE <= .5 && comparison.P99DeltaE <= 1,
            $"Resting delta-E mean={comparison.MeanDeltaE:F3}, p99={comparison.P99DeltaE:F3}");
        var neutral = settings.Clone();
        neutral.Locals = null;
        using var off = pipeline.Render(new RenderRequest(large, neutral,
            RenderIntent.Preview, null, new RenderOptions(false)));
        WysiwygTests.AlignForComparison(off.Image, actual);
        var quarter = new MagickGeometry(0, 0, actual.Width / 4, actual.Height);
        actual.Crop(quarter);
        off.Image.Crop(quarter);
        var left = GoldenImageComparer.Compare(off.Image, actual, GoldenComparisonDomain.DisplaySrgb);
        _output.WriteLine($"Unmasked left quarter delta-E mean={left.MeanDeltaE:F3}, p99={left.P99DeltaE:F3}");
        Assert.True(left.MeanDeltaE <= .5 && left.P99DeltaE <= 1,
            $"Unmasked left quarter delta-E mean={left.MeanDeltaE:F3}, p99={left.P99DeltaE:F3}");
    }
}
