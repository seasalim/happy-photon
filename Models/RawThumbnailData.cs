namespace HappyPhoton.Models;

/// <summary>
/// An encoded RAW preview and the visible source geometry reported by the decoder.
/// </summary>
public sealed record RawThumbnailData(
    byte[] EncodedBytes,
    int? VisibleSourceWidth,
    int? VisibleSourceHeight);
