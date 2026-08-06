using System.Diagnostics;
using HappyPhoton.Models;
using ImageMagick;
using Sdcb.LibRaw;

namespace HappyPhoton.Services;

public sealed class RawBaseLoader : IBaseImageLoader
{
    private readonly bool _isAvailable;
    private readonly Func<RawContext, byte[]?> _thumbnailReader;

    public RawBaseLoader()
        : this(LibRawNativeSupport.IsAvailable)
    {
    }

    internal RawBaseLoader(
        bool isAvailable,
        Func<RawContext, byte[]?>? thumbnailReader = null)
    {
        _isAvailable = isAvailable;
        _thumbnailReader = thumbnailReader ?? RawThumbnailReader.Read;
    }

    public bool CanLoad(ImageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return _isAvailable && file.IsRaw;
    }

    public BaseImage? LoadPreviewBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        Load(file, decode, preview: true, cancellationToken);

    public BaseImage? LoadFullBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        Load(file, decode, preview: false, cancellationToken);

    private BaseImage? Load(
        ImageFile file,
        BaseDecodeSettings decode,
        bool preview,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(decode);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanLoad(file))
        {
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        MagickImage? pixels = null;
        try
        {
            using var context = RawContext.OpenFile(file.FilePath);
            cancellationToken.ThrowIfCancellationRequested();

            var fullWidth = context.Width;
            var fullHeight = context.Height;
            var orientation = NormalizeOrientation(
                ImageServiceHelpers.GetExifOrientation(file.FilePath));
            var metadataExposureBiasEv = RawExposureBias.Read(
                context,
                file.FilePath);
            var thumbnailStopwatch = Stopwatch.StartNew();
            var thumbnailBytes = ReadThumbnail(context, file.FilePath);
            var thumbnailElapsed = thumbnailStopwatch.ElapsedMilliseconds;
            cancellationToken.ThrowIfCancellationRequested();

            context.Unpack();
            cancellationToken.ThrowIfCancellationRequested();
            var (camMul, camToSrgb) = CopyCameraFacts(context);

            context.DcrawProcess(parameters =>
                ConfigureOutput(parameters, decode, preview));
            cancellationToken.ThrowIfCancellationRequested();

            using var processed = context.MakeDcrawMemoryImage();
            cancellationToken.ThrowIfCancellationRequested();
            if (processed.Bits != 16 || processed.Channels != 3 ||
                processed.Width <= 0 || processed.Height <= 0)
            {
                return null;
            }

            pixels = ImportRgb16(
                processed.AsSpan<byte>(),
                processed.Width,
                processed.Height);
            cancellationToken.ThrowIfCancellationRequested();

            ApplyOrientation(
                pixels,
                orientation,
                fullWidth,
                fullHeight);
            var estimateStopwatch = Stopwatch.StartNew();
            var sourceExposureBiasEv = PreviewExposureEstimator.Estimate(
                thumbnailBytes,
                pixels,
                metadataExposureBiasEv,
                file.FilePath);
            var estimateElapsed = estimateStopwatch.ElapsedMilliseconds;
            ImageServiceHelpers.LogPerformance(
                nameof(RawBaseLoader),
                "SourceExposureBias",
                thumbnailElapsed + estimateElapsed,
                file.FilePath,
                $"thumbnail={thumbnailElapsed};estimate={estimateElapsed}");
            if (preview)
            {
                BitmapConversionService.ResizeToMaxDimension(
                    pixels,
                    BaseImage.PreviewMaxDimension);
            }

            pixels.Depth = 16;
            pixels.Strip();
            cancellationToken.ThrowIfCancellationRequested();

            var orientedFullSize = GetOrientedSize(
                fullWidth,
                fullHeight,
                orientation);
            var asShot = WhiteBalanceModel.EstimateAsShot(
                camMul,
                camToSrgb);
            var info = new BaseImageInfo(
                BaseSourceKind.RawLibRaw,
                IsRawSource: true,
                decode,
                camMul,
                camToSrgb,
                AsShotKelvin: asShot.kelvin,
                AsShotTint: asShot.tint,
                HadIccProfile: false,
                IccDescription: null,
                ExifOrientationApplied: orientation,
                orientedFullSize.Width,
                orientedFullSize.Height,
                SourceExposureBiasEv: sourceExposureBiasEv);
            var result = new BaseImage(pixels, info);
            pixels = null;

            ImageServiceHelpers.LogPerformance(
                nameof(RawBaseLoader),
                preview ? nameof(LoadPreviewBase) : nameof(LoadFullBase),
                stopwatch.ElapsedMilliseconds,
                file.FilePath,
                $"size={result.Pixels.Width}x{result.Pixels.Height}");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ImageServiceHelpers.LogDebug(
                nameof(RawBaseLoader),
                $"Decode failed: {exception.Message}",
                file.FilePath);
            return null;
        }
        finally
        {
            pixels?.Dispose();
        }
    }

    internal static void ConfigureOutput(
        OutputParams parameters,
        BaseDecodeSettings decode,
        bool preview)
    {
        parameters.OutputBps = 16;
        parameters.Gamma[0] = 1.0;
        parameters.Gamma[1] = 1.0;
        parameters.NoAutoBright = true;
        parameters.UseAutoWb = false;
        parameters.UseCameraWb = true;
        parameters.UseCameraMatrix = true;
        parameters.OutputColor = LibRawColorSpace.SRGB;
        parameters.HighlightMode = decode.HlReconstruction switch
        {
            HlReconstructionMode.Blend => 2,
            HlReconstructionMode.Clip => 0,
            _ => throw new InvalidOperationException(
                $"Unsupported highlight reconstruction mode: {decode.HlReconstruction}.")
        };
        parameters.FbddNoiserd = decode.NoiseReduction switch
        {
            FbddMode.Off => 0,
            FbddMode.Light => 1,
            FbddMode.Full => 2,
            _ => throw new InvalidOperationException(
                $"Unsupported FBDD mode: {decode.NoiseReduction}.")
        };
        parameters.HalfSize = preview;
    }

    internal static MagickImage ImportRgb16(
        ReadOnlySpan<byte> data,
        int width,
        int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "RGB16 dimensions must be positive.");
        }

        var expectedLength = checked(width * height * 3 * sizeof(ushort));
        if (data.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Expected {expectedLength} bytes for a {width}x{height} RGB16 image.",
                nameof(data));
        }

        var image = new MagickImage(
            MagickColors.Black,
            (uint)width,
            (uint)height);
        try
        {
            image.ColorSpace = ColorSpace.RGB;
            var settings = new PixelImportSettings(
                (uint)width,
                (uint)height,
                StorageType.Short,
                PixelMapping.RGB);
            image.ImportPixels(data, settings);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    internal static bool ApplyOrientation(
        MagickImage image,
        int orientation,
        int sourceWidth,
        int sourceHeight)
    {
        var alreadyApplied = orientation is >= 5 and <= 8 &&
            DimensionsAreSwapped(
                (int)image.Width,
                (int)image.Height,
                sourceWidth,
                sourceHeight);
        if (orientation != 1 && !alreadyApplied)
        {
            ImageServiceHelpers.ApplyExifOrientation(image, orientation);
        }

        return alreadyApplied;
    }

    private static bool DimensionsAreSwapped(
        int decodedWidth,
        int decodedHeight,
        int sourceWidth,
        int sourceHeight)
    {
        var sameDelta = Math.Abs(
            (long)decodedWidth * sourceHeight -
            (long)decodedHeight * sourceWidth);
        var swappedDelta = Math.Abs(
            (long)decodedWidth * sourceWidth -
            (long)decodedHeight * sourceHeight);
        return swappedDelta < sameDelta;
    }

    private static (int Width, int Height) GetOrientedSize(
        int width,
        int height,
        int orientation) =>
        orientation is >= 5 and <= 8
            ? (height, width)
            : (width, height);

    private static int NormalizeOrientation(int orientation) =>
        orientation is >= 1 and <= 8 ? orientation : 1;

    private byte[]? ReadThumbnail(RawContext context, string filePath)
    {
        try
        {
            return _thumbnailReader(context);
        }
        catch (Exception exception)
        {
            ImageServiceHelpers.LogDebug(
                nameof(RawBaseLoader),
                $"Thumbnail read failed: {exception.Message}",
                filePath);
            return null;
        }
    }

    private static (double[]? CamMul, double[,]? CamToSrgb) CopyCameraFacts(
        RawContext context)
    {
        var multipliers = context.CameraMultipler;
        var matrix = context.RgbCamera;
        var availableColumns = Math.Min(
            4,
            Math.Min(multipliers.Count, matrix.Width));
        if (matrix.Height < 3)
        {
            return (null, null);
        }

        var channelCount = HasUsableChannel(multipliers, matrix, 3)
            ? 4
            : Math.Min(3, availableColumns);
        if (channelCount < 3)
        {
            return (null, null);
        }

        var camMul = new double[channelCount];
        var camToSrgb = new double[3, channelCount];
        for (var channel = 0; channel < channelCount; channel++)
        {
            var multiplier = multipliers[channel];
            if (!float.IsFinite(multiplier) || multiplier <= 0)
            {
                return (null, null);
            }

            camMul[channel] = multiplier;
            for (var row = 0; row < 3; row++)
            {
                var value = matrix[row, channel];
                if (!float.IsFinite(value))
                {
                    return (null, null);
                }

                camToSrgb[row, channel] = value;
            }
        }

        if (IsIdentityCameraTransform(camToSrgb))
        {
            return (camMul, null);
        }

        return (camMul, camToSrgb);
    }

    internal static bool IsIdentityCameraTransform(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.GetLength(0) != 3 ||
            matrix.GetLength(1) is not (3 or 4))
        {
            return false;
        }

        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < matrix.GetLength(1); column++)
            {
                var expected = column < 3 && row == column ? 1.0 : 0.0;
                if (Math.Abs(matrix[row, column] - expected) > 1e-6)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasUsableChannel(
        IReadOnlyList<float> multipliers,
        IReadOnly2DIndexer<float> matrix,
        int channel)
    {
        if (multipliers.Count <= channel ||
            matrix.Width <= channel ||
            !float.IsFinite(multipliers[channel]) ||
            multipliers[channel] <= 0)
        {
            return false;
        }

        for (var row = 0; row < Math.Min(3, matrix.Height); row++)
        {
            if (float.IsFinite(matrix[row, channel]) &&
                Math.Abs(matrix[row, channel]) > float.Epsilon)
            {
                return true;
            }
        }

        return false;
    }
}
