using System.Collections.Concurrent;
using System.Diagnostics;
using ImageMagick;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

/// <summary>
/// Extracts embedded JPEG previews from RAW files.
/// </summary>
public static class EmbeddedJpegExtractor
{
    private static readonly ConcurrentDictionary<string, Lazy<byte[]?>> _embeddedJpegCache = new();
    private static readonly TimeSpan EmbeddedJpegCacheExpiry = TimeSpan.FromSeconds(10);
    private static DateTime _lastCacheCleanup = DateTime.UtcNow;

    /// <summary>
    /// Extracts embedded JPEG preview from RAW files by scanning for JPEG markers.
    /// Uses a short-lived cache to avoid duplicate file scans during parallel operations.
    /// </summary>
    public static byte[]? ExtractEmbeddedJpeg(string filePath)
    {
        return GetCachedEmbeddedJpeg(filePath);
    }

    private static byte[]? GetCachedEmbeddedJpeg(string filePath)
    {
        CleanupEmbeddedJpegCache();

        var lazy = _embeddedJpegCache.GetOrAdd(filePath, path =>
            new Lazy<byte[]?>(() => ExtractEmbeddedJpegCore(path)));

        return lazy.Value;
    }

    private static void CleanupEmbeddedJpegCache()
    {
        var now = DateTime.UtcNow;
        if (now - _lastCacheCleanup < EmbeddedJpegCacheExpiry)
            return;

        _lastCacheCleanup = now;
        _embeddedJpegCache.Clear();

        if (DebugLoggingEnabled)
            LogDebug(nameof(CleanupEmbeddedJpegCache), "Cleared embedded JPEG cache");
    }

    private static byte[]? ExtractEmbeddedJpegCore(string filePath)
    {
        try
        {
            var fileBytes = File.ReadAllBytes(filePath);
            LogDebug(nameof(ExtractEmbeddedJpeg), $"Scanning file ({fileBytes.Length / 1024}KB, {fileBytes.Length} bytes) for embedded JPEGs", filePath);

            var candidates = new List<(byte[] Data, uint Width, uint Height, int Offset)>();
            int markersFound = 0;

            for (int i = 0; i < fileBytes.Length - 3; i++)
            {
                if (fileBytes[i] == 0xFF && fileBytes[i + 1] == 0xD8 && fileBytes[i + 2] == 0xFF)
                {
                    markersFound++;
                    int markerNum = markersFound;

                    var segmentType = fileBytes[i + 3];
                    var segmentName = segmentType switch
                    {
                        0xE0 => "APP0/JFIF",
                        0xE1 => "APP1/EXIF",
                        0xE2 => "APP2",
                        0xDB => "DQT",
                        0xC0 => "SOF0",
                        0xC2 => "SOF2",
                        _ => $"0x{segmentType:X2}"
                    };

                    LogDebug(nameof(ExtractEmbeddedJpeg), $"Marker #{markerNum} at offset {i} (0x{i:X}): segment={segmentName}", filePath);

                    int lastEnd = -1;
                    int firstEnd = -1;
                    int endMarkersFound = 0;
                    bool stoppedAtNextJpeg = false;
                    int nextJpegOffset = -1;

                    for (int j = i + 3; j < fileBytes.Length - 1; j++)
                    {
                        if (fileBytes[j] == 0xFF && fileBytes[j + 1] == 0xD9)
                        {
                            endMarkersFound++;
                            if (firstEnd == -1) firstEnd = j;
                            lastEnd = j;
                        }
                        if (j > i + 100 && fileBytes[j] == 0xFF && fileBytes[j + 1] == 0xD8 && fileBytes[j + 2] == 0xFF)
                        {
                            stoppedAtNextJpeg = true;
                            nextJpegOffset = j;
                            break;
                        }
                    }

                    if (lastEnd > i)
                    {
                        int length = lastEnd - i + 2;
                        int firstLength = firstEnd > i ? firstEnd - i + 2 : 0;

                        LogDebug(nameof(ExtractEmbeddedJpeg), $"  -> Found {endMarkersFound} FFD9 markers, firstEnd at {firstEnd} ({firstLength / 1024}KB), lastEnd at {lastEnd} ({length / 1024}KB){(stoppedAtNextJpeg ? $", stopped at next JPEG at {nextJpegOffset}" : "")}", filePath);

                        if (length > 51200)
                        {
                            var jpegData = new byte[length];
                            Array.Copy(fileBytes, i, jpegData, 0, length);

                            try
                            {
                                using var image = new MagickImage(jpegData);
                                if (image.Width > 0 && image.Height > 0)
                                {
                                    candidates.Add((jpegData, image.Width, image.Height, i));
                                    LogDebug(nameof(ExtractEmbeddedJpeg), $"  -> VALID: {image.Width}x{image.Height} ({length / 1024}KB, {(long)image.Width * image.Height} pixels)", filePath);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogDebug(nameof(ExtractEmbeddedJpeg), $"  -> INVALID JPEG: {ex.Message}", filePath);

                                if (firstEnd != lastEnd && firstLength > 51200)
                                {
                                    LogDebug(nameof(ExtractEmbeddedJpeg), $"  -> Trying with first FFD9 at {firstEnd} ({firstLength / 1024}KB)...", filePath);
                                    var altJpegData = new byte[firstLength];
                                    Array.Copy(fileBytes, i, altJpegData, 0, firstLength);
                                    try
                                    {
                                        using var altImage = new MagickImage(altJpegData);
                                        if (altImage.Width > 0 && altImage.Height > 0)
                                        {
                                            candidates.Add((altJpegData, altImage.Width, altImage.Height, i));
                                            LogDebug(nameof(ExtractEmbeddedJpeg), $"  -> ALT VALID: {altImage.Width}x{altImage.Height} ({firstLength / 1024}KB)", filePath);
                                        }
                                    }
                                    catch
                                    {
                                        LogDebug(nameof(ExtractEmbeddedJpeg), $"  -> ALT also invalid", filePath);
                                    }
                                }
                            }
                        }
                        else
                        {
                            LogDebug(nameof(ExtractEmbeddedJpeg), $"  -> SKIPPED: {length / 1024}KB < 50KB threshold", filePath);
                        }
                    }
                    else
                    {
                        LogDebug(nameof(ExtractEmbeddedJpeg), $"  -> No FFD9 end marker found{(stoppedAtNextJpeg ? $", stopped at next JPEG at {nextJpegOffset}" : "")}", filePath);
                    }
                }
            }

            LogDebug(nameof(ExtractEmbeddedJpeg), $"Summary: {markersFound} markers scanned, {candidates.Count} valid candidates", filePath);

            var selected = candidates
                .OrderByDescending(c => (long)c.Width * c.Height)
                .ThenByDescending(c => c.Data.Length)
                .FirstOrDefault();

            if (selected.Data != null)
            {
                LogDebug(nameof(ExtractEmbeddedJpeg), $"Selected: {selected.Width}x{selected.Height} ({selected.Data.Length / 1024}KB) from offset {selected.Offset}", filePath);
                return selected.Data;
            }

            LogDebug(nameof(ExtractEmbeddedJpeg), "No suitable embedded JPEG found", filePath);
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HappyPhoton] ExtractEmbeddedJpeg failed: {ex.Message}");
            LogDebug(nameof(ExtractEmbeddedJpeg), $"Failed: {ex.Message}", filePath);
            return null;
        }
    }
}
