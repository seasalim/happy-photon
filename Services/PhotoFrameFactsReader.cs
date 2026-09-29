using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

internal sealed class PhotoFrameFactsReader(ISourceAvailabilityService availability)
{
    private readonly Dictionary<string, Header> _headers = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, PhotoCameraFacts> _cameras = new(StringComparer.OrdinalIgnoreCase);

    internal void RememberCamera(ImageFile file, PhotoCameraFacts facts) => _cameras[file.FilePath] = facts;

    internal PhotoCameraFacts? ReadCamera(ImageFile file, bool cachedOnly = false)
    {
        if (_cameras.TryGetValue(file.FilePath, out var facts)) return facts;
        if (!file.IsRaw || cachedOnly) return null;

        Read(file, new EditSettings { Lens = new() { Distortion = false, ChromaticAberration = false } });

        return _cameras.GetValueOrDefault(file.FilePath);
    }

    internal Action<string, string>? Opening { get; set; }

    internal Action<string>? ReadCompleted { get; set; }

    internal (int Width, int Height)? Read(ImageFile file, EditSettings settings, bool cachedOnly = false)
    {
        if (!cachedOnly && !SourceAccessPolicy.CanRead(availability.GetAvailability(file.FilePath), SourceReadIntent.Background)) return null;

        try
        {
            if (!_headers.TryGetValue(file.FilePath, out var header))
            {
                if (cachedOnly) return null;

                Opening?.Invoke(file.FilePath, "frame");

                if (file.IsRaw)
                {
                    using var raw = LibRawContext.Open(file.FilePath);
                    var dimensions = raw.GetDimensions();
                    var metadata = raw.GetMetadata();
                    var monochrome = RawBaseLoader.IsMonochromeSensor(raw.GetSensorIdentity());
                    _cameras[file.FilePath] = new(monochrome ? null : new CameraIdentity(
                        metadata.NormalizedMake ?? metadata.Make, metadata.NormalizedModel ?? metadata.Model), monochrome);
                    header = new((int)dimensions.VisibleWidth, (int)dimensions.VisibleHeight,
                        RawBaseLoader.NormalizeOrientation(dimensions.Orientation)) { IsMonochrome = monochrome };
                }
                else
                {
                    using var image = new MagickImage();
                    image.Ping(file.FilePath);
                    header = new((int)image.Width, (int)image.Height, (int)image.Orientation);
                }

                _headers[file.FilePath] = header;
            }

            LensPrescription? prescription = null;
            var lens = settings.Lens;

            if (!header.IsMonochrome && file.Extension.Equals(".dng", StringComparison.OrdinalIgnoreCase) &&
                (lens.Distortion || lens.ChromaticAberration || lens.Vignetting))
            {
                if (!header.LensRead)
                {
                    if (cachedOnly) return null;
                    if (!SourceAccessPolicy.CanRead(availability.GetAvailability(file.FilePath), SourceReadIntent.Background)) return null;

                    Opening?.Invoke(file.FilePath, "DNG output window");
                    header.Prescription = new DngLensPrescriptionReader().Read(file.FilePath).Prescription;
                    header.LensRead = true;
                }

                var candidate = header.Prescription;

                if (candidate != null && (lens.Distortion && candidate.HasDistortion ||
                    lens.ChromaticAberration && candidate.HasChromaticAberration ||
                    lens.Vignetting && candidate.HasVignetting) &&
                    LensCorrectionProcessor.CanApply(header.Width, header.Height, header.Orientation,
                        candidate, BaseDecodeSettings.From(settings)))
                {
                    prescription = candidate;
                }
            }

            var size = LensCorrectionProcessor.GetOutputSize(
                header.Width, header.Height, header.Orientation, null, prescription);

            return settings.Rotation % 180 == 0 ? size : (size.Height, size.Width);
        }
        catch (Exception ex) when (ex is IOException or MagickException or LibRawDecodeException or
            LibRawDeploymentException or LibRawBridgeException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            if (!cachedOnly) ReadCompleted?.Invoke(file.FilePath);
        }
    }

    private sealed class Header(int width, int height, int orientation)
    {
        internal int Width { get; } = width;

        internal int Height { get; } = height;

        internal int Orientation { get; } = orientation;

        internal bool IsMonochrome { get; init; }

        internal bool LensRead { get; set; }

        internal LensPrescription? Prescription { get; set; }
    }
}
