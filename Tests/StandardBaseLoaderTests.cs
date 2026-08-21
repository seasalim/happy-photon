using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class StandardBaseLoaderTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"HappyPhotonStandardLoaderTests_{Guid.NewGuid():N}");

    public StandardBaseLoaderTests() => Directory.CreateDirectory(_tempDirectory);

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.JPEG")]
    [InlineData("photo.png")]
    [InlineData("photo.bmp")]
    [InlineData("photo.gif")]
    [InlineData("photo.tif")]
    [InlineData("photo.TIFF")]
    [InlineData("photo.webp")]
    [InlineData("photo.heic")]
    [InlineData("photo.HEIF")]
    public void CanLoad_AcceptsStandardExtensions(string fileName)
    {
        var loader = new StandardBaseLoader();

        Assert.True(loader.CanLoad(new ImageFile(fileName)));
    }

    [Theory]
    [InlineData("photo.cr2")]
    [InlineData("photo.CR3")]
    [InlineData("photo.nef")]
    [InlineData("photo.nrw")]
    [InlineData("photo.arw")]
    [InlineData("photo.dng")]
    [InlineData("photo.raf")]
    [InlineData("photo.orf")]
    [InlineData("photo.rw2")]
    [InlineData("photo.pef")]
    public void CanLoad_RejectsEveryRawExtension(string fileName)
    {
        var loader = new StandardBaseLoader();

        Assert.False(loader.CanLoad(new ImageFile(fileName)));
    }

    [Fact]
    public void CanLoad_RejectsUnsupportedExtension()
    {
        var loader = new StandardBaseLoader();

        Assert.False(loader.CanLoad(new ImageFile("photo.txt")));
    }

    [Theory]
    [InlineData("display-p3-reference.jpg")]
    [InlineData("adobe-rgb-reference.jpg")]
    public void FullBase_NormalizesTaggedSourceAndRecordsProfile(string assetName)
    {
        var decode = new BaseDecodeSettings(
            HlReconstructionMode.Clip,
            FbddMode.Full);
        var loader = new StandardBaseLoader();

        using var result = loader.LoadFullBase(
            Asset(assetName),
            decode,
            CancellationToken.None);

        Assert.NotNull(result);
        AssertStandardFacts(result!, decode);
        Assert.Null(result.SourceSaturation);
        Assert.True(result!.Info.HadIccProfile);
        Assert.False(string.IsNullOrWhiteSpace(result.Info.IccDescription));
        Assert.Null(result.Pixels.GetColorProfile());
        Assert.Empty(result.Pixels.ProfileNames);
    }

    [Theory]
    [InlineData("display-p3-reference.jpg", 1.5)]
    [InlineData("adobe-rgb-reference.jpg", 2.0)]
    public void FullBase_TaggedColorSpacesMatchSrgbReference(
        string assetName,
        double meanDeltaEBound)
    {
        var loader = new StandardBaseLoader();
        using var expected = loader.LoadFullBase(
            Asset("srgb-reference.jpg"),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var actual = loader.LoadFullBase(
            Asset(assetName),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(expected);
        Assert.NotNull(actual);
        var comparison = GoldenImageComparer.Compare(
            expected!.Pixels,
            actual!.Pixels,
            GoldenComparisonDomain.LinearRec2020);

        Assert.True(
            comparison.MeanDeltaE <= meanDeltaEBound,
            $"{assetName} normalized mean ΔE {comparison.MeanDeltaE:F3}, " +
            $"p99 ΔE {comparison.P99DeltaE:F3}.");
    }

    [Fact]
    public void FullBase_PreservesSixteenBitDepthAndRemovesProfiles()
    {
        var loader = new StandardBaseLoader();

        using var result = loader.LoadFullBase(
            Asset("reference-16bit.tiff"),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(16u, result!.Pixels.Depth);
        Assert.Equal(ColorSpace.RGB, result.Pixels.ColorSpace);
        Assert.Null(result.Pixels.GetColorProfile());
    }

    [Fact]
    public void FullBase_UntaggedCmykTransformsToLinearRec2020()
    {
        var path = Path.Combine(_tempDirectory, "untagged-cmyk.jpg");
        using (var source = new MagickImage(MagickColors.Orange, 40, 20))
        {
            source.ColorSpace = ColorSpace.CMYK;
            source.RemoveProfile("icc");
            source.Write(path);
        }

        var loader = new StandardBaseLoader();
        using var result = loader.LoadFullBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result!.Info.HadIccProfile);
        Assert.Equal(ColorSpace.RGB, result.Pixels.ColorSpace);
        Assert.Null(result.Pixels.GetColorProfile());
    }

    [Fact]
    public void PreviewBase_UsesBoundedPixelsButRecordsNativeGeometry()
    {
        var path = WriteJpeg("large.jpg", 4000, 2000);
        var loader = new StandardBaseLoader();

        using var first = loader.LoadPreviewBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var second = loader.LoadPreviewBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1600u, first!.Pixels.Width);
        Assert.Equal(800u, first.Pixels.Height);
        Assert.Equal(4000, first.Info.FullWidth);
        Assert.Equal(2000, first.Info.FullHeight);
        Assert.False(first.Info.HadIccProfile);
        AssertStandardFacts(first, BaseDecodeSettings.Default);
        Assert.Equal(PixelValues(first.Pixels), PixelValues(second!.Pixels));
        Assert.Equal(first.Info, second.Info);
    }

    [Fact]
    public void PreviewPair_UsesOneDecodeAndIndependentSizeClasses()
    {
        var decodeCount = 0;
        var loader = new StandardBaseLoader((_, _) =>
        {
            decodeCount++;
            return new MagickImage(MagickColors.Orange, 4000, 2000)
            {
                Depth = 16
            };
        });

        using var outcome = ((IBaseImageLoader)loader)
            .LoadPreviewBaseWithOutcome(
                new ImageFile(Path.Combine(_tempDirectory, "pair.png")),
                BaseDecodeSettings.Default,
                CancellationToken.None).Pair;

        Assert.NotNull(outcome);
        Assert.Equal(1, decodeCount);
        Assert.Equal(1600u, outcome!.Interactive.Pixels.Width);
        Assert.Equal(800u, outcome.Interactive.Pixels.Height);
        Assert.NotNull(outcome.Large);
        Assert.Equal(3200u, outcome.Large!.Pixels.Width);
        Assert.Equal(1600u, outcome.Large.Pixels.Height);
    }

    [Fact]
    public void PreviewBase_DoesNotUpscaleJpegBelowPreviewBound()
    {
        var loader = new StandardBaseLoader();

        using var result = loader.LoadPreviewBase(
            Asset("display-p3-reference.jpg"),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(
            Math.Max(result!.Info.FullWidth, result.Info.FullHeight) <=
            BaseImage.InteractivePreviewMaxDimension);
        Assert.Equal((uint)result.Info.FullWidth, result.Pixels.Width);
        Assert.Equal((uint)result.Info.FullHeight, result.Pixels.Height);
    }

    [Fact]
    public void FullBase_AutoOrientsAndRecordsAppliedOrientation()
    {
        var path = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "srgb-exif-gps-orientation-6.jpg");
        var sourceInfo = new MagickImageInfo(path);
        using var source = new MagickImage(path);
        Assert.Contains(
            source.ProfileNames,
            name => name.Equals("exif", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(source.GetExifProfile()?.GetValue(ExifTag.GPSLatitude));
        var loader = new StandardBaseLoader();

        using var result = loader.LoadFullBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(6, result!.Info.ExifOrientationApplied);
        Assert.Equal((int)sourceInfo.Height, result.Info.FullWidth);
        Assert.Equal((int)sourceInfo.Width, result.Info.FullHeight);
        Assert.Equal((uint)result.Info.FullWidth, result.Pixels.Width);
        Assert.Equal((uint)result.Info.FullHeight, result.Pixels.Height);
        Assert.Empty(result.Pixels.ProfileNames);
    }

    [Fact]
    public void FullBase_DecodesOnlyFirstGifFrame()
    {
        var path = Path.Combine(_tempDirectory, "animated.gif");
        using (var frames = new MagickImageCollection())
        {
            frames.Add(new MagickImage(MagickColors.Red, 8, 4));
            frames.Add(new MagickImage(MagickColors.Blue, 8, 4));
            frames.Write(path);
        }

        var loader = new StandardBaseLoader();
        using var result = loader.LoadFullBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(result);
        var pixels = PixelValues(result!.Pixels);
        Assert.True(pixels[0] > pixels[2]);
    }

    [Fact]
    public void FullBase_RawIsRejectedBeforeMagickDecode()
    {
        var calls = 0;
        var loader = new StandardBaseLoader(
            (_, _) =>
            {
                calls++;
                return new MagickImage(MagickColors.Orange, 32, 16);
            });
        using var result = loader.LoadFullBase(
            new ImageFile("fallback.dng"),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void FullBase_RepeatedDecodeIsBitIdentical()
    {
        var loader = new StandardBaseLoader();
        var file = Asset("display-p3-reference.jpg");

        using var first = loader.LoadFullBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var second = loader.LoadFullBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(PixelValues(first!.Pixels), PixelValues(second!.Pixels));
        Assert.Equal(first.Info, second.Info);
    }

    [Fact]
    public void PreviewBase_HeicUsesRuntimeReaderAndHeicKind()
    {
        var format = MagickFormatInfo.Create(MagickFormat.Heic);
        Assert.SkipWhen(
            format is not { SupportsReading: true },
            "HEIC loader test skipped because this Magick.NET runtime has no HEIC reader.");
        var file = Asset("reference.heic");
        try
        {
            using var probe = new MagickImage(file.FilePath);
        }
        catch (Exception ex)
        {
            Assert.Skip($"HEIC loader test skipped because runtime decode failed: {ex.Message}");
        }

        var loader = new StandardBaseLoader();
        using var result = loader.LoadPreviewBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(BaseSourceKind.HeicPlatform, result!.Info.Kind);
        AssertStandardFacts(result, BaseDecodeSettings.Default);
    }

    [Fact]
    public void BadFileReturnsNullAndReleasesFile()
    {
        var path = Path.Combine(_tempDirectory, "bad.jpg");
        File.WriteAllText(path, "not an image");
        var loader = new StandardBaseLoader();

        var result = loader.LoadFullBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.Null(result);
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CancellationBeforeDecodeThrowsWithoutOpeningFile()
    {
        var path = WriteJpeg("cancel.jpg", 20, 10);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var loader = new StandardBaseLoader();

        Assert.Throws<OperationCanceledException>(() =>
            loader.LoadPreviewBase(
                new ImageFile(path),
                BaseDecodeSettings.Default,
                cancellation.Token));

        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CancellationAfterDecodeDisposesUnreturnedPixels()
    {
        using var cancellation = new CancellationTokenSource();
        MagickImage? decoded = null;
        var loader = new StandardBaseLoader((_, _) =>
        {
            decoded = new MagickImage(MagickColors.Orange, 20, 10);
            cancellation.Cancel();
            return decoded;
        });

        Assert.Throws<OperationCanceledException>(() =>
            loader.LoadFullBase(
                new ImageFile("cancel.png"),
                BaseDecodeSettings.Default,
                cancellation.Token));

        Assert.NotNull(decoded);
        Assert.Throws<ObjectDisposedException>(() => decoded!.Width);
    }

    [Fact]
    public void BaseOwnsPixelsWithoutRetainingSourceFile()
    {
        var path = WriteJpeg("ownership.jpg", 20, 10);
        var loader = new StandardBaseLoader();
        var result = loader.LoadFullBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(result);
        File.Delete(path);
        Assert.Equal(20u, result!.Pixels.Width);
        result.Dispose();
        Assert.Throws<ObjectDisposedException>(() => result.Pixels);
    }

    private string WriteJpeg(string fileName, uint width, uint height)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        using var image = new MagickImage(MagickColors.Orange, width, height);
        image.Format = MagickFormat.Jpeg;
        image.Write(path);
        return path;
    }

    private static ImageFile Asset(string fileName) =>
        new(Path.Combine(GoldenTestPaths.AssetDirectory, fileName));

    private static ushort[] PixelValues(MagickImage image)
    {
        using var pixels = image.GetPixels();
        return pixels.ToShortArray(PixelMapping.RGB) ??
            throw new InvalidOperationException("Could not read image pixels.");
    }

    private static void AssertStandardFacts(
        BaseImage result,
        BaseDecodeSettings decode)
    {
        Assert.Equal(16u, result.Pixels.Depth);
        Assert.Equal(ColorSpace.RGB, result.Pixels.ColorSpace);
        Assert.False(result.Info.IsRawSource);
        Assert.Same(decode, result.Info.Decode);
        Assert.Null(result.Info.CamMul);
        Assert.Null(result.Info.CamToSrgb);
        Assert.Equal(6504, result.Info.AsShotKelvin);
        Assert.Equal(0, result.Info.AsShotTint);
        Assert.True(result.Info.FullWidth > 0);
        Assert.True(result.Info.FullHeight > 0);
        Assert.Null(result.Pixels.GetColorProfile());
        Assert.Empty(result.Pixels.ProfileNames);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
