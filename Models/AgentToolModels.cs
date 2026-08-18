using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HappyPhoton.Models;

/// <summary>Thrown for invalid agent requests with a message safe to return to the client.</summary>
public class AgentToolException : Exception
{
    public AgentToolException(string message) : base(message)
    {
    }
}

public static class AgentToolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public static class AgentToolValidation
{
    public static string? CheckBatchCap(IReadOnlyCollection<string> ids, int cap) =>
        ids.Count > cap ? $"Batch too large: {ids.Count} ids (cap {cap})." : null;

    public static ImageFlag ParseFlag(string value) => value.ToLowerInvariant() switch
    {
        "picked" => ImageFlag.Picked,
        "rejected" => ImageFlag.Rejected,
        "unflagged" => ImageFlag.Unflagged,
        _ => throw new AgentToolException(
            $"Unknown flag '{value}'. Use picked, rejected, or unflagged.")
    };

    public static ColorLabel ParseColorLabel(string value) =>
        value.ToLowerInvariant() switch
        {
            "none" => ColorLabel.None,
            "red" => ColorLabel.Red,
            "yellow" => ColorLabel.Yellow,
            "green" => ColorLabel.Green,
            "blue" => ColorLabel.Blue,
            "purple" => ColorLabel.Purple,
            _ => throw new AgentToolException(
                $"Unknown color label '{value}'. Use none, red, yellow, green, blue, or purple.")
        };

    public static ExportFormat ParseExportFormat(string value) => value.ToLowerInvariant() switch
    {
        "jpeg" or "jpg" => ExportFormat.Jpeg,
        "png" => ExportFormat.Png,
        "webp" => ExportFormat.Webp,
        _ => throw new AgentToolException(
            $"Unknown format '{value}'. Use jpeg, png, or webp.")
    };

    public static OutputColorSpace ParseOutputColorSpace(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "srgb" => OutputColorSpace.Srgb,
            "displayp3" or "display-p3" or "p3" => OutputColorSpace.DisplayP3,
            _ => throw new AgentToolException(
                $"Unknown output color space '{value}'. Use srgb or displayP3.")
        };

    public static string SanitizeVariantName(string name)
    {
        var sanitized = new string(name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray());
        if (sanitized.Length == 0)
            throw new AgentToolException("Variant name must not be empty after sanitization.");
        return sanitized;
    }

    public static string FlagToString(ImageFlag flag) => flag switch
    {
        ImageFlag.Picked => "picked",
        ImageFlag.Rejected => "rejected",
        _ => "unflagged"
    };

    public static string ColorLabelToString(ColorLabel colorLabel) =>
        colorLabel.ToString().ToLowerInvariant();
}

public record AgentLibraryState(
    string? FolderPath,
    int TotalCount,
    int VisibleCount,
    AgentFilterState Filters,
    string? SelectedImageId,
    bool BurstsComputed = false);

public record AgentFilterState(
    string FileType,
    string Flag,
    int MinimumRating,
    string ColorLabel = "all");

public record ListImagesRequest(
    string? Flag = null,
    int? MinRating = null,
    string? FileType = null,
    int Offset = 0,
    int Limit = 100,
    bool LoadMetadata = true);

public record AgentImageSummary(
    string Id,
    string FileName,
    int Rating,
    string Flag,
    bool HasEdits,
    bool MetadataLoaded,
    int PixelWidth,
    int PixelHeight,
    DateTime? DateTaken,
    string? Camera,
    int? Iso,
    double? FNumber,
    string? ExposureTime,
    double? FocalLength,
    string? LensModel,
    string? BurstId = null,
    int? BurstIndex = null,
    int? BurstSize = null)
{
    public string SourceAvailability { get; init; } = "unknown";
    public string ColorLabel { get; init; } = "none";
}

public record AgentImageStats(
    string Id,
    double Sharpness,
    double ClippedHighlightsPct,
    double ClippedShadowsPct,
    double MeanLuminance);

public record AgentImageStatsResult(
    List<AgentImageStats> Images,
    List<AgentBatchFailure> Failed);

public record AgentBatchFailure(
    string Id,
    string Reason,
    string? Code = null);

public record AgentBatchResult(List<string> Succeeded, List<AgentBatchFailure> Failed);

public record AgentPresetInfo(string Id, string Name, string Category, bool IsUserPreset);

public record AgentEditSettingsInput(
    double Exposure,
    int Brightness,
    int Contrast,
    int Saturation,
    int Vibrance,
    int Shadows,
    int Highlights,
    int Version = EditSettings.CurrentVersion,
    AgentWhiteBalanceInput? Wb = null,
    bool? BaseLook = null,
    string? HlReconstruction = null);

public record AgentWhiteBalanceInput(
    string Mode = "asShot",
    double? Kelvin = null,
    double? Tint = null,
    List<double>? Gains = null,
    string? Preset = null);

public record AgentExportOptions(
    string? OutputFolder = null,
    int Quality = 85,
    int? MaxDimension = null,
    string NamingPattern = "{name}",
    string Format = "jpeg",
    string OutputColorSpace = "srgb",
    List<AgentExportVariant>? Variants = null);

public record AgentExportVariant(string Name, int? MaxDimension);

public record AgentExportResult(
    List<string> Exported,
    List<string> Skipped,
    List<AgentBatchFailure> Failed);
