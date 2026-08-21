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
    private const string DisplayTraceEnvironmentVariable =
        "HAPPY_PHOTON_DISPLAY_TRACE";
    private static readonly (bool Perf, bool Debug, bool DisplayTrace) DiagnosticFlags =
        ReadDiagnosticFlags(Environment.GetEnvironmentVariable);
    private static readonly object DisplayTraceSync = new();
    private static int _displayTraceEnabledOverride = -1;
    private static Action<string>? _displayTraceSinkOverride;
    private static string? _displayTraceFilePathOverride;
    private static bool _displayTraceFileStarted;

    public static readonly bool PerfLoggingEnabled = DiagnosticFlags.Perf;

    public static readonly bool DebugLoggingEnabled = DiagnosticFlags.Debug;

    public static bool DisplayTraceLoggingEnabled =>
        Volatile.Read(ref _displayTraceEnabledOverride) switch
        {
            0 => false,
            1 => true,
            _ => DiagnosticFlags.DisplayTrace
        };

    internal static (bool Perf, bool Debug, bool DisplayTrace) ReadDiagnosticFlags(
        Func<string, string?> readVariable)
    {
        ArgumentNullException.ThrowIfNull(readVariable);
        return (
            !string.IsNullOrWhiteSpace(readVariable(PerfEnvironmentVariable)),
            !string.IsNullOrWhiteSpace(readVariable(DebugEnvironmentVariable)),
            readVariable(DisplayTraceEnvironmentVariable) == "1");
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
        var platform = OperatingSystem.IsWindows()
            ? "Windows"
            : OperatingSystem.IsMacOS() ? "macOS" : "Linux";
        LogError(
            $"HEIC decoding failed for '{Path.GetFileName(filePath)}' on {platform}.");
    }

    /// <summary>
    /// Checks if an exception is related to missing RAW delegate support.
    /// </summary>
    public static bool IsRawDelegateError(Exception ex, string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!ImageFile.RawExtensions.Contains(ext)) return false;

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
        LogError(
            $"RAW decoding failed for '{Path.GetFileName(filePath)}' " +
            $"({ext}): {ex.Message}");
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
    public static async Task EnsureCatalogIdAsync(
        this ImageFile imageFile,
        CatalogService catalogService)
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

    public static void LogDisplayTrace(
        ref DisplayTraceLogInterpolatedStringHandler message)
    {
        if (!DisplayTraceLoggingEnabled) return;
        WriteDisplayTrace(message.GetFormattedText());
    }

    internal static IDisposable OverrideDisplayTraceForTesting(
        bool enabled,
        Action<string>? sink)
    {
        lock (DisplayTraceSync)
        {
            var previousEnabled = _displayTraceEnabledOverride;
            var previousSink = _displayTraceSinkOverride;
            Volatile.Write(ref _displayTraceEnabledOverride, enabled ? 1 : 0);
            _displayTraceSinkOverride = sink;
            return new DisplayTraceOverrideScope(
                previousEnabled,
                previousSink);
        }
    }

    public static void LogError(string message)
    {
        Debug.WriteLine($"[HappyPhoton] {message}");
        Console.Error.WriteLine(message);
    }

    private static void WriteDisplayTrace(string message)
    {
        var sink = Volatile.Read(ref _displayTraceSinkOverride);

        var fullMessage = $"[DisplayChain] {message}";
        if (sink != null)
        {
            sink(fullMessage);
            return;
        }

        Debug.WriteLine(fullMessage);
        Console.WriteLine(fullMessage);
        AppendDisplayTraceToFile(fullMessage);
    }

    private static void AppendDisplayTraceToFile(string fullMessage)
    {
        try
        {
            // The file log carries wall-clock timestamps for correlating a
            // reproduction; the in-memory test sink stays timestamp-free.
            fullMessage =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {fullMessage}";
            lock (DisplayTraceSync)
            {
                var path = _displayTraceFilePathOverride
                    ?? GetDefaultDisplayTraceFilePath();
                if (path == null) return;

                if (!_displayTraceFileStarted)
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);
                    // The header makes every capture self-identifying: a trace
                    // is only meaningful against the build that produced it.
                    var header =
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} " +
                        $"[DisplayChain] trace start " +
                        $"version={AppBuildInfo.Identity.FriendlyVersion} " +
                        $"revision={AppBuildInfo.Identity.SourceRevision ?? "unknown"}";
                    File.WriteAllText(
                        path,
                        header + Environment.NewLine +
                        fullMessage + Environment.NewLine);
                    _displayTraceFileStarted = true;
                }
                else
                {
                    File.AppendAllText(path, fullMessage + Environment.NewLine);
                }
            }
        }
        catch
        {
            // The trace is a diagnostic; file failures must never surface.
        }
    }

    private static string? GetDefaultDisplayTraceFilePath()
    {
        var local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(local)
            ? null
            : Path.Combine(local, "Happy Photon", "logs", "display-trace.log");
    }

    internal static IDisposable OverrideDisplayTraceFileForTesting(string path)
    {
        lock (DisplayTraceSync)
        {
            var previousPath = _displayTraceFilePathOverride;
            var previousStarted = _displayTraceFileStarted;
            _displayTraceFilePathOverride = path;
            _displayTraceFileStarted = false;
            return new DisplayTraceFileOverrideScope(previousPath, previousStarted);
        }
    }

    private sealed class DisplayTraceFileOverrideScope(
        string? previousPath,
        bool previousStarted) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            lock (DisplayTraceSync)
            {
                if (_disposed) return;
                _displayTraceFilePathOverride = previousPath;
                _displayTraceFileStarted = previousStarted;
                _disposed = true;
            }
        }
    }

    private sealed class DisplayTraceOverrideScope(
        int previousEnabled,
        Action<string>? previousSink) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            lock (DisplayTraceSync)
            {
                if (_disposed) return;
                Volatile.Write(
                    ref _displayTraceEnabledOverride,
                    previousEnabled);
                _displayTraceSinkOverride = previousSink;
                _disposed = true;
            }
        }
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

    [InterpolatedStringHandler]
    public ref struct DisplayTraceLogInterpolatedStringHandler
    {
        private DefaultInterpolatedStringHandler _builder;

        public DisplayTraceLogInterpolatedStringHandler(
            int literalLength,
            int formattedCount,
            out bool shouldAppend)
        {
            shouldAppend = DisplayTraceLoggingEnabled;
            _builder = shouldAppend
                ? new DefaultInterpolatedStringHandler(literalLength, formattedCount)
                : default;
        }

        public void AppendLiteral(string value) => _builder.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => _builder.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string? format) =>
            _builder.AppendFormatted(value, format);
        public string GetFormattedText() => _builder.ToStringAndClear();
    }
}
