using HappyPhoton.LibRaw.Interop;
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

    internal static (bool AlreadyApplied, int LoaderOrientation, int FrameOrientation) ResolveOrientation(
        int orientation) => (true, 1, NormalizeOrientation(orientation));

    private static (int Width, int Height) GetOrientedSize(
        int width,
        int height,
        int orientation) =>
        orientation is >= 5 and <= 8
            ? (height, width)
            : (width, height);

    internal static int NormalizeOrientation(int orientation) =>
        orientation is >= 1 and <= 8 ? orientation : 1;

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

    internal static double[]? PrepareAsShotWhiteXy(
        string path,
        DcpProfileResolution resolution,
        DcpCameraData cameraData)
    {
        if (cameraData.AsShotNeutral != null || cameraData.AsShotWhiteXy == null) return null;

        try
        {
            var profile = resolution.Profile ?? new DcpProfileReader().ReadEmbeddedWhiteBalanceProfile(path);
            var neutral = DcpMatrixCalculator.DeriveAsShotNeutral(profile, cameraData)!;

            return neutral.Select(value => 1 / value).ToArray();
        }
        catch (Exception exception)
        {
            ImageServiceHelpers.LogDebug(nameof(RawBaseLoader),
                $"As-shot xy balancing was rejected: {exception.Message}", path);

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
