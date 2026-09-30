using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed class PhotoSpotSnapshot(ImageFile file, PhotoCameraFacts? camera, PhotoSensorFrame? frame)
{
    private (bool Available, string? Serial)? _serial;

    internal string? Compatibility(ImageFile target, PhotoFrameFactsReader reader, bool cachedOnly,
        out int sourceOrientation, out int targetOrientation)
    {
        sourceOrientation = frame?.Orientation ?? 1;
        targetOrientation = sourceOrientation;
        if (string.Equals(file.FilePath, target.FilePath, StringComparison.OrdinalIgnoreCase)) return null;

        var sourceSerial = _serial ?? reader.ReadSerial(file, cachedOnly);
        if (!sourceSerial.Available) return "facts unavailable";

        _serial = sourceSerial;
        frame ??= reader.ReadSensorFrame(file, cachedOnly);
        camera ??= reader.ReadCamera(file, cachedOnly: true);
        if (frame == null || string.IsNullOrWhiteSpace(camera?.Identity?.Normalized)) return "facts unavailable";

        var targetFrame = reader.ReadSensorFrame(target, cachedOnly);
        var targetCamera = reader.ReadCamera(target, cachedOnly: true);
        if (targetFrame == null || string.IsNullOrWhiteSpace(targetCamera?.Identity?.Normalized)) return "facts unavailable";
        if (camera.Identity.Normalized != targetCamera.Identity.Normalized) return "different camera";

        var serial = reader.ReadSerial(target, cachedOnly);
        if (!serial.Available) return "facts unavailable";
        if (_serial.Value.Serial == null || serial.Serial == null) return "camera body unknown";
        if (_serial.Value.Serial != serial.Serial) return "different camera body";
        if (frame.Width != targetFrame.Width || frame.Height != targetFrame.Height) return "different sensor size";

        sourceOrientation = frame.Orientation;
        targetOrientation = targetFrame.Orientation;

        return null;
    }
}
