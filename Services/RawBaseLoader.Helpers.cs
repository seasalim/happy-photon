using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

public sealed partial class RawBaseLoader
{
    internal static MagickImage ImportRgb16(
        ReadOnlySpan<byte> data,
        int width,
        int height) => CameraRgbCharacterization.Passthrough.ImportRgb16(
            data,
            width,
            height);

    internal static bool ApplyOrientation(
        MagickImage image,
        int orientation,
        int sourceWidth,
        int sourceHeight)
    {
        var transform = ResolveOrientation(orientation, (int)image.Width, (int)image.Height,
            sourceWidth, sourceHeight);

        if (transform.LoaderOrientation != 1)
        {
            ImageServiceHelpers.ApplyExifOrientation(image, transform.LoaderOrientation);
        }

        return transform.AlreadyApplied;
    }

    private static (bool AlreadyApplied, int LoaderOrientation, int FrameOrientation) ResolveOrientation(
        int orientation, int decodedWidth, int decodedHeight, int sourceWidth, int sourceHeight)
    {
        var alreadyApplied = orientation is >= 5 and <= 8 &&
            DimensionsAreSwapped(decodedWidth, decodedHeight, sourceWidth, sourceHeight);
        var loaderOrientation = alreadyApplied ? 1 : orientation;
        // LibRaw's native rotations precede the loader's existing EXIF step.
        // In particular, flip 3 receives two half turns and produces an unrotated base.
        var nativeOrientation = orientation switch { 3 => 3, 5 => 8, 6 => 6, _ => 1 };

        return (alreadyApplied, loaderOrientation, RepairOrientation.Compose(nativeOrientation, loaderOrientation));
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

    internal static int ResolveLensOrientation(
        int processedWidth,
        int processedHeight,
        int sourceWidth,
        int sourceHeight,
        int orientation) =>
        ResolveOrientation(orientation, processedWidth, processedHeight,
            sourceWidth, sourceHeight).LoaderOrientation;

    private static (int Width, int Height) GetOrientedSize(
        int width,
        int height,
        int orientation) =>
        orientation is >= 5 and <= 8
            ? (height, width)
            : (width, height);

    internal static int NormalizeOrientation(int orientation) =>
        orientation is >= 1 and <= 8 ? orientation : 1;

    internal static int RepairFrameOrientation(int orientation, int width, int height)
    {
        var swapped = orientation is 5 or 6;

        return ResolveOrientation(orientation, swapped ? height : width, swapped ? width : height,
            width, height).FrameOrientation;
    }

    private byte[]? ReadThumbnail(LibRawContext context, string filePath)
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

    private static (DcpCameraData Data, string? Error) TryReadDngCameraData(
        string path)
    {
        try
        {
            return (new DcpProfileReader().ReadCameraData(path), null);
        }
        catch (Exception exception)
        {
            ImageServiceHelpers.LogDebug(
                nameof(RawBaseLoader),
                $"DNG camera profile facts were rejected: {exception.Message}",
                path);
            return (
                DcpCameraData.Defaults,
                $"DNG camera calibration tags are invalid: {exception.Message}");
        }
    }
}
