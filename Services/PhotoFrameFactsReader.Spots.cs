using HappyPhoton.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace HappyPhoton.Services;

internal sealed record PhotoSensorFrame(int Width, int Height, int Orientation);

internal sealed partial class PhotoFrameFactsReader
{
    private readonly Dictionary<string, (bool Available, string? Serial)> _serials = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, PhotoSensorFrame> _sensorFrames = new(StringComparer.OrdinalIgnoreCase);

    internal Func<string, string?> SerialReader { get; set; } = path => ImageMetadataReader.ReadMetadata(path)
        .OfType<ExifSubIfdDirectory>().Select(directory => directory.GetString(0xA431)).FirstOrDefault(value => value != null);

    internal void RememberSensorFrame(ImageFile file, PhotoSensorFrame frame) => _sensorFrames[file.FilePath] = frame;

    internal PhotoSensorFrame? ReadSensorFrame(ImageFile file, bool cachedOnly = false)
    {
        if (_sensorFrames.TryGetValue(file.FilePath, out var frame)) return frame;

        Read(file, new EditSettings { Lens = new() { Distortion = false, ChromaticAberration = false } }, cachedOnly);
        if (!_headers.TryGetValue(file.FilePath, out var header)) return null;

        return new(header.Width, header.Height,
            file.IsRaw ? RawBaseLoader.RepairFrameOrientation(header.Orientation, header.Width, header.Height) : header.Orientation);
    }

    internal (bool Available, string? Serial) ReadSerial(ImageFile file, bool cachedOnly)
    {
        if (_serials.TryGetValue(file.FilePath, out var facts)) return facts;
        if (cachedOnly || !SourceAccessPolicy.CanRead(availability.GetAvailability(file.FilePath), SourceReadIntent.Background))
            return (false, null);

        try
        {
            Opening?.Invoke(file.FilePath, "body serial");
            var serial = SerialReader(file.FilePath)?.Trim().Trim('\0').Trim();
            facts = (true, string.IsNullOrEmpty(serial) || serial.All(character => character == '0') ? null : serial);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImageProcessingException)
        {
            return (false, null);
        }

        _serials[file.FilePath] = facts;

        return facts;
    }
}
