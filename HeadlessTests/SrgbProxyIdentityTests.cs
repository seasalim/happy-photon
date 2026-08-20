using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.HeadlessTests;

// The sRGB proxy identity gate (TONE_ENGINE.md §1): the crossing-off proxy
// path renders an unedited sRGB bitmap back within one 8-bit code. Lives in
// the headless suite because ThumbnailRenderer operates on Avalonia bitmaps,
// which need the platform render interface CI's plain test hosts lack.
public sealed class SrgbProxyIdentityTests
{
    [AvaloniaFact]
    public void UneditedSrgbProxy_RoundTripsToDisplayWithinOneEightBitCode()
    {
        var path = FindAsset("srgb-reference.jpg");
        using var sourceImage = new MagickImage(path);
        if (sourceImage.GetColorProfile() is { } sourceProfile)
        {
            sourceImage.TransformColorSpace(sourceProfile, ColorProfiles.SRGB);
        }
        sourceImage.AutoOrient();
        using var source = BitmapConversionService.ConvertToBitmap(sourceImage) ??
            throw new InvalidOperationException("Could not create sRGB proxy fixture.");
        using var rendered = new ThumbnailRenderer(new RenderPipeline())
            .RenderStandardEdits(
                source,
                new EditSettings(),
                Math.Max(source.PixelSize.Width, source.PixelSize.Height));
        using var renderedImage = BitmapConversionService.ConvertToMagickImage(rendered);
        var expected = sourceImage.GetPixelsUnsafe()
            .ToByteArray(PixelMapping.RGB) ?? [];
        var actual = renderedImage.GetPixelsUnsafe()
            .ToByteArray(PixelMapping.RGB) ?? [];

        Assert.Equal(expected.Length, actual.Length);
        var maximum = expected.Zip(actual, (left, right) =>
            Math.Abs(left - right)).Max();
        Assert.InRange(maximum, 0, 1);
    }

    private static string FindAsset(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "Tests", "assets", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate Tests/assets/{fileName} above " +
            AppContext.BaseDirectory);
    }
}
