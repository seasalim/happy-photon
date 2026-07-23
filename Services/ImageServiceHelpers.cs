using System.Diagnostics;
using System.Runtime.CompilerServices;
using ImageMagick;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Static helper methods for image service operations including EXIF handling,
/// error detection, and logging.
/// </summary>
public static class ImageServiceHelpers
{
    private const string PerfEnvironmentVariable = "HAPPY_PHOTON_PERF";
    private const string DebugEnvironmentVariable = "HAPPY_PHOTON_DEBUG";
    private static readonly (bool Perf, bool Debug) DiagnosticFlags =
        ReadDiagnosticFlags(Environment.GetEnvironmentVariable);

    public static readonly bool PerfLoggingEnabled = DiagnosticFlags.Perf;

    public static readonly bool DebugLoggingEnabled = DiagnosticFlags.Debug;

    internal static (bool Perf, bool Debug) ReadDiagnosticFlags(
        Func<string, string?> readVariable)
    {
        ArgumentNullException.ThrowIfNull(readVariable);
        return (
            !string.IsNullOrWhiteSpace(readVariable(PerfEnvironmentVariable)),
            !string.IsNullOrWhiteSpace(readVariable(DebugEnvironmentVariable)));
    }

    /// <summary>
    /// Reads the EXIF orientation value from a file without fully decoding it.
    /// </summary>
    /// <returns>EXIF orientation value (1-8), or 1 if not found</returns>
    public static int GetExifOrientation(string filePath)
    {
        try
        {
            using var image = new MagickImage();
            image.Ping(filePath);
            var orientation = image.Orientation;
            return (int)orientation;
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>
    /// Manually applies EXIF orientation to an image that lacks EXIF metadata.
    /// Used for LibRaw-decoded images which are returned as raw PPM without EXIF.
    /// </summary>
    public static void ApplyExifOrientation(MagickImage image, int orientation)
    {
        switch (orientation)
        {
            case 2:
                image.Flop();
                break;
            case 3:
                image.Rotate(180);
                break;
            case 4:
                image.Flip();
                break;
            case 5:
                image.Rotate(90);
                image.Flop();
                break;
            case 6:
                image.Rotate(90);
                break;
            case 7:
                image.Rotate(270);
                image.Flop();
                break;
            case 8:
                image.Rotate(270);
                break;
        }
    }

    /// <summary>
    /// Checks if an exception is related to missing HEIC/HEIF codec support.
    /// </summary>
    public static bool IsHeicDelegateError(Exception ex, string filePath)
    {
        var ext = Path.GetExtension(filePath).ToUpperInvariant();
        if (ext != ".HEIC" && ext != ".HEIF") return false;

        var message = ex.Message.ToLowerInvariant();
        return message.Contains("delegate") ||
               message.Contains("no decode delegate") ||
               message.Contains("heic") ||
               message.Contains("heif") ||
               message.Contains("unable to load");
    }

    /// <summary>
    /// Logs a helpful message when HEIC support is missing.
    /// </summary>
    public static void LogHeicSupportError(string filePath)
    {
        var isWindows = OperatingSystem.IsWindows();
        var message = isWindows
            ? $"HEIC decoding failed for '{Path.GetFileName(filePath)}'. " +
              "On Windows, install 'HEIF Image Extensions' from the Microsoft Store, " +
              "or ensure your ImageMagick installation includes libheif support."
            : $"HEIC decoding failed for '{Path.GetFileName(filePath)}'. " +
              "Install libheif: apt install libheif1 (Debian/Ubuntu) or dnf install libheif (Fedora).";

        LogError(message);
    }

    /// <summary>
    /// Checks if an exception is related to missing RAW delegate support.
    /// </summary>
    public static bool IsRawDelegateError(Exception ex, string filePath)
    {
        var ext = Path.GetExtension(filePath).ToUpperInvariant();
        var rawExtensions = new[] { ".RAF", ".CR2", ".CR3", ".NEF", ".NRW", ".ARW", ".SRF", ".SR2", ".DNG", ".ORF", ".RW2", ".PEF" };
        if (!rawExtensions.Contains(ext)) return false;

        var message = ex.Message.ToLowerInvariant();
        return message.Contains("delegate") ||
               message.Contains("no decode delegate") ||
               message.Contains("unable to open") ||
               message.Contains("unable to read") ||
               message.Contains("not authorized");
    }

    /// <summary>
    /// Logs a helpful message when RAW delegate support is missing.
    /// </summary>
    public static void LogRawDelegateError(string filePath, Exception ex)
    {
        var ext = Path.GetExtension(filePath).ToUpperInvariant();
        var message = $"RAW decoding failed for '{Path.GetFileName(filePath)}' ({ext}): {ex.Message}";

        if (OperatingSystem.IsWindows())
        {
            message += "\nOn Windows, RAW support may require installing ImageMagick with RAW delegates, " +
                       "or the specific camera format may not be fully supported.";
        }
        else
        {
            message += "\nInstall dcraw or libraw: apt install dcraw libraw-bin (Debian/Ubuntu) " +
                       "or dnf install dcraw LibRaw (Fedora).";
        }

        LogError(message);
    }

    /// <summary>
    /// Handles image load errors by logging appropriate messages for HEIC/RAW delegate issues.
    /// </summary>
    public static void HandleImageLoadError(Exception ex, string filePath)
    {
        if (IsHeicDelegateError(ex, filePath))
            LogHeicSupportError(filePath);
        else if (IsRawDelegateError(ex, filePath))
            LogRawDelegateError(filePath, ex);
    }

    /// <summary>
    /// Ensures the ImageFile has a valid CatalogId, creating one if needed.
    /// </summary>
    public static async Task EnsureCatalogIdAsync(this ImageFile imageFile, ICatalogService catalogService)
    {
        if (imageFile.CatalogId == 0)
            imageFile.CatalogId = await catalogService.GetOrCreateImageAsync(imageFile.FilePath);
    }

    public static void LogPerformance(string method, string step, long elapsedMs, string? filePath = null, string? extra = null)
    {
        if (!PerfLoggingEnabled)
            return;

        var fileName = filePath != null ? Path.GetFileName(filePath) : "n/a";
        var message = $"[Performance] {method} - {step}: {elapsedMs}ms file={fileName}";
        if (!string.IsNullOrWhiteSpace(extra))
            message += $" {extra}";
        Debug.WriteLine(message);
        Console.WriteLine(message);
    }

    public static void LogDebug(string method, string message, string? filePath = null)
    {
        if (!DebugLoggingEnabled)
            return;

        var fileName = filePath != null ? Path.GetFileName(filePath) : "";
        var fileStr = string.IsNullOrEmpty(fileName) ? "" : $" [{fileName}]";
        var fullMessage = $"[Debug] {method}{fileStr}: {message}";
        Debug.WriteLine(fullMessage);
        Console.WriteLine(fullMessage);
    }

    public static void LogDebug(
        string method,
        ref DebugLogInterpolatedStringHandler message,
        string? filePath = null)
    {
        if (!DebugLoggingEnabled) return;
        LogDebug(method, message.GetFormattedText(), filePath);
    }

    public static void LogPerformance(
        string method,
        string step,
        long elapsedMs,
        string? filePath,
        ref PerformanceLogInterpolatedStringHandler extra)
    {
        if (!PerfLoggingEnabled) return;
        LogPerformance(method, step, elapsedMs, filePath, extra.GetFormattedText());
    }

    public static void LogError(string message)
    {
        Debug.WriteLine($"[HappyPhoton] {message}");
        Console.Error.WriteLine(message);
    }

    [InterpolatedStringHandler]
    public ref struct DebugLogInterpolatedStringHandler
    {
        private DefaultInterpolatedStringHandler _builder;

        public DebugLogInterpolatedStringHandler(
            int literalLength,
            int formattedCount,
            out bool shouldAppend)
        {
            shouldAppend = DebugLoggingEnabled;
            _builder = shouldAppend
                ? new DefaultInterpolatedStringHandler(literalLength, formattedCount)
                : default;
        }

        public void AppendLiteral(string value) => _builder.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => _builder.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string? format) =>
            _builder.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment) =>
            _builder.AppendFormatted(value, alignment);
        public void AppendFormatted<T>(T value, int alignment, string? format) =>
            _builder.AppendFormatted(value, alignment, format);
        public string GetFormattedText() => _builder.ToStringAndClear();
    }

    [InterpolatedStringHandler]
    public ref struct PerformanceLogInterpolatedStringHandler
    {
        private DefaultInterpolatedStringHandler _builder;

        public PerformanceLogInterpolatedStringHandler(
            int literalLength,
            int formattedCount,
            out bool shouldAppend)
        {
            shouldAppend = PerfLoggingEnabled;
            _builder = shouldAppend
                ? new DefaultInterpolatedStringHandler(literalLength, formattedCount)
                : default;
        }

        public void AppendLiteral(string value) => _builder.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => _builder.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string? format) =>
            _builder.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment) =>
            _builder.AppendFormatted(value, alignment);
        public void AppendFormatted<T>(T value, int alignment, string? format) =>
            _builder.AppendFormatted(value, alignment, format);
        public string GetFormattedText() => _builder.ToStringAndClear();
    }
}
