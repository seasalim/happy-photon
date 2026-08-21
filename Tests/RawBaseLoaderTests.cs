using System.Runtime.InteropServices;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;
using static HappyPhoton.Tests.RawBaseLoaderTestSupport;

namespace HappyPhoton.Tests;

public sealed class RawBaseLoaderTests
{
    private static readonly string[] RawAssets =
    [
        "canon-eos-350d.cr2",
        "nikon-d70-burst-1.nef",
        "fujifilm-x30.raf",
        "pentax-k-r.dng"
    ];

    private readonly ITestOutputHelper _output;

    public RawBaseLoaderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(HlReconstructionMode.Blend, FbddMode.Off, true, 2, 0)]
    [InlineData(HlReconstructionMode.Blend, FbddMode.Light, false, 2, 1)]
    [InlineData(HlReconstructionMode.Blend, FbddMode.Full, true, 2, 2)]
    [InlineData(HlReconstructionMode.Clip, FbddMode.Off, false, 0, 0)]
    [InlineData(HlReconstructionMode.Clip, FbddMode.Light, true, 0, 1)]
    [InlineData(HlReconstructionMode.Clip, FbddMode.Full, false, 0, 2)]
    public void ConfigureOutput_PinsDeterministicLinearParameters(
        HlReconstructionMode highlight,
        FbddMode noiseReduction,
        bool preview,
        int expectedHighlight,
        int expectedFbdd)
    {
        var parameters = RawBaseLoader.ConfigureOutput(
            new BaseDecodeSettings(highlight, noiseReduction),
            preview);

        Assert.Equal(16, parameters.OutputBits);
        Assert.Equal(1.0, parameters.GammaPower);
        Assert.Equal(1.0, parameters.GammaSlope);
        Assert.True(parameters.NoAutoBright);
        Assert.False(parameters.UseAutoWhiteBalance);
        Assert.True(parameters.UseCameraWhiteBalance);
        Assert.True(parameters.UseCameraMatrix);
        Assert.Equal(0, parameters.OutputColor);
        Assert.Equal(expectedHighlight, parameters.HighlightMode);
        Assert.Equal(expectedFbdd, parameters.FbddNoiseReduction);
        Assert.Equal(preview, parameters.HalfSize);
    }

    [Fact]
    public void ImportRgb16_PreservesNativeEndianSamples()
    {
        ushort[] expected =
        [
            0x0001, 0x1234, 0xFEDC,
            0x0102, 0x7FFF, 0xFFFF
        ];
        var bytes = MemoryMarshal.AsBytes(expected.AsSpan()).ToArray();

        using var image = RawBaseLoader.ImportRgb16(bytes, width: 2, height: 1);
        using var pixels = image.GetPixels();
        var actual = pixels.ToShortArray(PixelMapping.RGB);

        Assert.Equal(ColorSpace.RGB, image.ColorSpace);
        Assert.Equal(expected, actual);
        Assert.Contains(actual!, value => value % 257 != 0);

        image.ColorSpace = ColorSpace.RGB;
        using var linearPixels = image.GetPixels();
        Assert.Equal(
            expected,
            linearPixels.ToShortArray(PixelMapping.RGB));
    }

    [Fact]
    public void CanLoad_RequiresRawClassificationAndCapability()
    {
        var available = new RawBaseLoader(isAvailable: true);
        var unavailable = new RawBaseLoader(isAvailable: false);
        var raw = new ImageFile("sample.CR2");
        var standard = new ImageFile("sample.jpg");

        Assert.True(available.CanLoad(raw));
        Assert.False(available.CanLoad(standard));
        Assert.False(unavailable.CanLoad(raw));
    }

    [Fact]
    public void NativeRuntime_IsAvailableAndVersionMatched()
    {
        var runtime = LibRawContext.Runtime;

        Assert.Equal(LibRawOutputConfiguration.Version, runtime.BridgeAbiVersion);
        Assert.Equal(0x001602u, runtime.LibRawVersionNumber);
        Assert.StartsWith("0.22.2", runtime.LibRawVersion);
        Assert.NotEqual(0u, runtime.Capabilities & LibRawCapabilities.Jpeg);
        Assert.NotEqual(0u, runtime.Capabilities & LibRawCapabilities.Zlib);
        Assert.True(new RawBaseLoader().CanLoad(new ImageFile("sample.CR2")));
    }

    [Fact]
    public void BadFile_ReturnsNullWithoutChangingSourceOrCreatingTemps()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "bad.cr2");
        var source = new byte[] { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(path, source);
        var before = Directory.GetFiles(directory.Path);
        var loader = new RawBaseLoader(isAvailable: true);

        var result = loader.LoadFullBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(source, File.ReadAllBytes(path));
        Assert.Equal(before, Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void PreCanceledDecode_ThrowsBeforeOpeningSource()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var loader = new RawBaseLoader(isAvailable: true);

        Assert.Throws<OperationCanceledException>(() =>
            loader.LoadPreviewBase(
                new ImageFile("missing.cr2"),
                BaseDecodeSettings.Default,
                cancellation.Token));
    }

    [Fact]
    public void CancellationDuringDecode_DiscardsPartialResultAndAllowsRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var cancelOnce = true;
        var loader = new RawBaseLoader(
            isAvailable: true,
            thumbnailReader: _ =>
            {
                if (cancelOnce)
                {
                    cancelOnce = false;
                    cancellation.Cancel();
                }
                return null;
            });
        var file = new ImageFile(Asset("canon-eos-350d.cr2"));

        Assert.Throws<OperationCanceledException>(() =>
            loader.LoadFullBase(
                file,
                BaseDecodeSettings.Default,
                cancellation.Token));

        using var retry = loader.LoadPreviewBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);
        Assert.NotNull(retry);
    }

    [Theory]
    [MemberData(nameof(GetRawAssets))]
    public void PreviewBase_IsLinearSixteenBitAndCarriesRawFacts(string fileName)
    {
        var loader = new RawBaseLoader();

        using var image = loader.LoadPreviewBase(
            new ImageFile(Asset(fileName)),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(image);
        AssertCanonicalBase(image!, BaseDecodeSettings.Default);
        Assert.True(
            image.Pixels.Width <= BaseImage.InteractivePreviewMaxDimension &&
            image.Pixels.Height <= BaseImage.InteractivePreviewMaxDimension);
        Assert.True(
            image.Info.FullWidth >= image.Pixels.Width &&
            image.Info.FullHeight >= image.Pixels.Height);
        _output.WriteLine(
            $"{fileName}: preview {image.Pixels.Width}x{image.Pixels.Height}, " +
            $"full {image.Info.FullWidth}x{image.Info.FullHeight}, " +
            $"camera channels {image.Info.CamMul!.Length}, " +
            $"as-shot {image.Info.AsShotKelvin:R} K/{image.Info.AsShotTint:R}");
    }

    [Fact]
    public void PreviewBase_ClampsPreviewEstimateIntoFujiMetadataBand()
    {
        var loader = new RawBaseLoader();

        using var fuji = loader.LoadPreviewBase(
            new ImageFile(Asset("fujifilm-x30.raf")),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var canon = loader.LoadPreviewBase(
            new ImageFile(Asset("canon-eos-350d.cr2")),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(fuji);
        Assert.NotNull(canon);
        // The x30 thumbnail measures brighter than its 0.58 EV MakerNote bias
        // by more than the trust band, so the selected bias sits at the
        // metadata + 0.5 EV clamp ceiling (small demosaic variance allowed).
        Assert.InRange(fuji!.Info.SourceExposureBiasEv, 1.02, 1.09);
        Assert.InRange(
            Math.Abs(fuji.Info.SourceExposureBiasEv - 0.58),
            0,
            0.5);
        Assert.InRange(
            canon!.Info.SourceExposureBiasEv,
            -RawExposureBias.MaxAbsEv,
            RawExposureBias.MaxAbsEv);
    }

    [Fact]
    public void PreviewBase_FallsBackWhenThumbnailIsMissingOrCorrupt()
    {
        var file = new ImageFile(Asset("fujifilm-x30.raf"));
        var missing = new RawBaseLoader(
            isAvailable: true,
            thumbnailReader: _ => null);
        var corrupt = new RawBaseLoader(
            isAvailable: true,
            thumbnailReader: _ => [1, 2, 3, 4]);

        using var missingBase = missing.LoadPreviewBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var corruptBase = corrupt.LoadPreviewBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(missingBase);
        Assert.NotNull(corruptBase);
        Assert.Equal(0.58, missingBase!.Info.SourceExposureBiasEv, 3);
        Assert.Equal(0.58, corruptBase!.Info.SourceExposureBiasEv, 3);
    }

    [Theory]
    [MemberData(nameof(GetRawAssets))]
    public void PreviewAndFull_EstimatesAgreeWithinTolerance(string fileName)
    {
        var loader = new RawBaseLoader();
        var file = new ImageFile(Asset(fileName));

        using var preview = loader.LoadPreviewBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var full = loader.LoadFullBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(preview);
        Assert.NotNull(full);
        Assert.InRange(
            preview!.Info.SourceExposureBiasEv,
            -RawExposureBias.MaxAbsEv,
            RawExposureBias.MaxAbsEv);
        Assert.InRange(
            full!.Info.SourceExposureBiasEv,
            -RawExposureBias.MaxAbsEv,
            RawExposureBias.MaxAbsEv);
        Assert.InRange(
            Math.Abs(
                preview.Info.SourceExposureBiasEv -
                full.Info.SourceExposureBiasEv),
            0,
            0.05);
        _output.WriteLine(
            $"{fileName}: preview bias {preview.Info.SourceExposureBiasEv:F4} EV, " +
            $"full bias {full.Info.SourceExposureBiasEv:F4} EV");
    }

    [Fact]
    public async Task CanonPreviewAndFull_AreDeterministicAndMeasured()
    {
        var loader = new RawBaseLoader();
        var file = new ImageFile(Asset("canon-eos-350d.cr2"));

        var previewMeasurement = await MeasureAsync(() =>
            loader.LoadPreviewBase(
                file,
                BaseDecodeSettings.Default,
                CancellationToken.None));
        using var preview = previewMeasurement.Image;
        Assert.NotNull(preview);
        AssertCanonicalBase(preview!, BaseDecodeSettings.Default);

        var fullMeasurement = await MeasureAsync(() =>
            loader.LoadFullBase(
                file,
                BaseDecodeSettings.Default,
                CancellationToken.None));
        using var full = fullMeasurement.Image;
        Assert.NotNull(full);
        AssertCanonicalBase(full!, BaseDecodeSettings.Default);
        Assert.Equal((uint)full.Info.FullWidth, full.Pixels.Width);
        Assert.Equal((uint)full.Info.FullHeight, full.Pixels.Height);

        using var repeated = loader.LoadFullBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);
        Assert.NotNull(repeated);
        Assert.Equal(PixelHash(full.Pixels), PixelHash(repeated!.Pixels));

        _output.WriteLine(
            $"Canon CR2 preview: {previewMeasurement.Elapsed.TotalMilliseconds:F0} ms, " +
            $"peak managed delta {previewMeasurement.PeakManagedBytes / 1048576.0:F1} MiB");
        _output.WriteLine(
            $"Canon CR2 full: {fullMeasurement.Elapsed.TotalMilliseconds:F0} ms, " +
            $"peak managed delta {fullMeasurement.PeakManagedBytes / 1048576.0:F1} MiB");
    }

    [Fact]
    public void FullDecode_DoesNotAllocateManagedFullImageCopy()
    {
        var loader = new RawBaseLoader(isAvailable: true, thumbnailReader: _ => null);
        var file = new ImageFile(Asset("canon-eos-350d.cr2"));
        using var warm = loader.LoadFullBase(
            file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(warm);

        var before = GC.GetAllocatedBytesForCurrentThread();
        using var image = loader.LoadFullBase(
            file, BaseDecodeSettings.Default, CancellationToken.None);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.NotNull(image);
        var decodedBytes = checked(
            (long)image!.Pixels.Width * image.Pixels.Height * 3 * sizeof(ushort));
        Assert.True(allocated < decodedBytes,
            $"Managed allocations {allocated} unexpectedly include a {decodedBytes}-byte image copy.");
    }

    [Fact]
    public void ByteIdenticalBurstFiles_ProduceIdenticalPreviewBases()
    {
        var loader = new RawBaseLoader();

        using var first = loader.LoadPreviewBase(
            new ImageFile(Asset("nikon-d70-burst-1.nef")),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var second = loader.LoadPreviewBase(
            new ImageFile(Asset("nikon-d70-burst-2.nef")),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(PixelHash(first!.Pixels), PixelHash(second!.Pixels));
        Assert.Equal(first.Info.Kind, second.Info.Kind);
        Assert.Equal(first.Info.IsRawSource, second.Info.IsRawSource);
        Assert.Equal(first.Info.Decode, second.Info.Decode);
        Assert.Equal(first.Info.CamMul, second.Info.CamMul);
        Assert.Equal(
            Flatten(first.Info.CamToSrgb!),
            Flatten(second.Info.CamToSrgb!));
        Assert.Equal(first.Info.FullWidth, second.Info.FullWidth);
        Assert.Equal(first.Info.FullHeight, second.Info.FullHeight);
        Assert.Equal(
            first.Info.SourceExposureBiasEv,
            second.Info.SourceExposureBiasEv);
    }

    [Fact]
    public void Orientation_AppliesOnlyWhenLibRawHasNotSwappedDimensions()
    {
        using var unoriented = CreateTwoPixelImage(width: 2, height: 1);
        var alreadyApplied = RawBaseLoader.ApplyOrientation(
            unoriented,
            orientation: 6,
            sourceWidth: 2,
            sourceHeight: 1);
        Assert.False(alreadyApplied);
        Assert.Equal(1u, unoriented.Width);
        Assert.Equal(2u, unoriented.Height);

        using var oriented = CreateTwoPixelImage(width: 1, height: 2);
        alreadyApplied = RawBaseLoader.ApplyOrientation(
            oriented,
            orientation: 6,
            sourceWidth: 2,
            sourceHeight: 1);
        Assert.True(alreadyApplied);
        Assert.Equal(1u, oriented.Width);
        Assert.Equal(2u, oriented.Height);
    }

    public static TheoryData<string> GetRawAssets()
    {
        var data = new TheoryData<string>();
        foreach (var asset in RawAssets)
        {
            data.Add(asset);
        }

        return data;
    }

    private static void AssertCanonicalBase(
        BaseImage image,
        BaseDecodeSettings expectedDecode)
    {
        Assert.Equal(BaseSourceKind.RawLibRaw, image.Info.Kind);
        Assert.True(image.Info.IsRawSource);
        Assert.Equal(expectedDecode, image.Info.Decode);
        Assert.InRange(
            image.Info.AsShotKelvin,
            WhiteBalanceModel.MinimumKelvin,
            WhiteBalanceModel.MaximumKelvin);
        Assert.InRange(
            image.Info.AsShotTint,
            WhiteBalanceModel.MinimumTint,
            WhiteBalanceModel.MaximumTint);
        Assert.NotEqual(5500, image.Info.AsShotKelvin);
        Assert.False(image.Info.HadIccProfile);
        Assert.Null(image.Info.IccDescription);
        Assert.InRange(image.Info.ExifOrientationApplied, 1, 8);
        Assert.Equal(16u, image.Pixels.Depth);
        Assert.Equal(ColorSpace.RGB, image.Pixels.ColorSpace);
        Assert.Null(image.Pixels.GetColorProfile());
        Assert.Null(image.Pixels.GetExifProfile());

        Assert.NotNull(image.Info.CamMul);
        Assert.Contains(image.Info.CamMul!.Length, new[] { 3, 4 });
        Assert.All(
            image.Info.CamMul,
            value => Assert.True(double.IsFinite(value) && value > 0));
        Assert.NotNull(image.Info.CamToSrgb);
        Assert.Equal(3, image.Info.CamToSrgb.GetLength(0));
        Assert.Equal(
            image.Info.CamMul.Length,
            image.Info.CamToSrgb.GetLength(1));
        Assert.All(
            Flatten(image.Info.CamToSrgb),
            value => Assert.True(double.IsFinite(value)));
        for (var row = 0; row < 3; row++)
        {
            var sum = Enumerable.Range(0, image.Info.CamMul.Length)
                .Sum(column => image.Info.CamToSrgb[row, column]);
            Assert.InRange(sum, 1 - 1e-5, 1 + 1e-5);
        }

        using var pixels = image.Pixels.GetPixels();
        var samples = pixels.ToShortArray(PixelMapping.RGB);
        Assert.NotNull(samples);
        Assert.Contains(samples!, value => value % 257 != 0);
    }

    [Fact]
    public void IdentityCameraTransform_IsUnavailableSentinel()
    {
        Assert.True(RawCameraFactSnapshot.IsIdentityTransform(
            ChromaticAdaptation.Identity()));
        Assert.True(RawCameraFactSnapshot.IsIdentityTransform(new double[,]
        {
            { 1, 0, 0, 0 },
            { 0, 1, 0, 0 },
            { 0, 0, 1, 0 }
        }));

        var calibrated = ChromaticAdaptation.Identity();
        calibrated[0, 1] = 0.01;
        Assert.False(RawCameraFactSnapshot.IsIdentityTransform(calibrated));
    }

    private static MagickImage CreateTwoPixelImage(int width, int height)
    {
        var samples = new ushort[width * height * 3];
        samples[0] = ushort.MaxValue;
        return RawBaseLoader.ImportRgb16(
            MemoryMarshal.AsBytes(samples.AsSpan()),
            width,
            height);
    }
}
