using System.Globalization;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Service for loading and saving application settings via the catalog.
/// </summary>
public partial class AppSettingsService
{
    private readonly CatalogService _catalogService;

    private const string RootFolderPathKey = "RootFolderPath";
    private const string SelectedFolderPathKey = "SelectedFolderPath";
    private const string FirstRunExperienceVersionKey = "FirstRunExperienceVersion";
    private const string FileTypeFilterKey = "FileTypeFilter";
    private const string BrowseThumbnailSizeKey = "BrowseThumbnailSize";
    private const string ShowCapturePairsKey = "ShowCapturePairs";
    private const string AppThemeKey = "AppTheme";
    private const string StripLocationDataKey = "StripLocationData";
    private const string OutputSharpeningKey = "OutputSharpening";

    public AppSettingsService(CatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public async Task<AppSettings> LoadAsync()
    {
        var saved = await _catalogService.GetAppSettingsAsync();
        string? Read(string key) => saved.GetValueOrDefault(key);
        bool Flag(string key) => bool.TryParse(Read(key), out var value) && value;

        var fileTypeFilter = ImageFileTypeFilter.All;
        var savedFilter = Read(FileTypeFilterKey);
        if (!string.IsNullOrEmpty(savedFilter) &&
            Enum.TryParse<ImageFileTypeFilter>(savedFilter, ignoreCase: true, out var parsedFilter))
        {
            fileTypeFilter = parsedFilter;
        }

        return new AppSettings
        {
            ShowTips = !bool.TryParse(Read("ShowTips"), out var showTips) || showTips,
            BrowseTipsSeen = Flag("BrowseTipsSeen"),
            DevelopTipsSeen = Flag("DevelopTipsSeen"),
            ExportTipsSeen = Flag("ExportTipsSeen"),
            PasteGroups = LoadPasteGroups(Read),
            PresetGroups = LoadGroupPreferences(Read("PresetGroups")),
            DevelopGroups = LoadGroupPreferences(Read("DevelopGroups")),
            SpotMode = Read("SpotMode") == "clone" ? "clone" : "heal",
            SpotSize = BrushNumber(Read("SpotSize"), 3, .2, 10),
            SpotFeather = BrushNumber(Read("SpotFeather"), 50, 0, 100),
            SpotOpacity = BrushNumber(Read("SpotOpacity"), 100, 5, 100),
            SpotVisualizeThreshold = BrushNumber(Read("SpotVisualizeThreshold"), 50, 0, 100),
            BrushMode = Read("BrushMode") == "erase" ? "erase" : "paint",
            BrushSize = BrushNumber(Read("BrushSize"), 65, 1, 100),
            BrushFeather = BrushNumber(Read("BrushFeather"), 50, 0, 100),
            BrushFlow = BrushNumber(Read("BrushFlow"), 100, 5, 100),
            Watermark = LoadWatermark(Read),
            WatermarkEnabled = Flag("WatermarkEnabled"),
            RootFolderPath = Read(RootFolderPathKey),
            SelectedFolderPath = Read(SelectedFolderPathKey),
            FirstRunExperienceVersion = int.TryParse(Read(FirstRunExperienceVersionKey), out var firstRun)
                ? firstRun : null,
            FileTypeFilter = fileTypeFilter,
            BrowseThumbnailSize = DefinedEnum(Read(BrowseThumbnailSizeKey), BrowseThumbnailSize.Medium),
            ShowCapturePairs = Flag(ShowCapturePairsKey),
            AppTheme = DefinedEnum(Read(AppThemeKey), AppTheme.Dark),
            StripLocationData = Flag(StripLocationDataKey),
            OutputSharpening = ParseOutputSharpening(Read(OutputSharpeningKey))
        };
    }

    private static T DefinedEnum<T>(string? value, T fallback) where T : struct, Enum =>
        !string.IsNullOrEmpty(value) && Enum.TryParse<T>(value, ignoreCase: true, out var parsed) &&
        Enum.IsDefined(parsed) ? parsed : fallback;

    private static double BrushNumber(string? text, double fallback, double min, double max) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? Math.Clamp(value, min, max) : fallback;

    public Task SaveAsync(AppSettings settings, bool saveTips = true)
    {
        var values = Preferences(settings, saveTips);
        values[RootFolderPathKey] = settings.RootFolderPath;
        values[SelectedFolderPathKey] = settings.SelectedFolderPath;
        values[FirstRunExperienceVersionKey] = settings.FirstRunExperienceVersion?.ToString();

        return _catalogService.SetAppSettingsAsync(values);
    }

    public Task SavePreferencesAsync(AppSettings settings, bool saveTips = true) =>
        _catalogService.SetAppSettingsAsync(Preferences(settings, saveTips));

    private static Dictionary<string, string?> Preferences(AppSettings settings, bool saveTips)
    {
        var values = new Dictionary<string, string?>
        {
            ["SpotMode"] = settings.SpotMode,
            ["SpotSize"] = settings.SpotSize.ToString(CultureInfo.InvariantCulture),
            ["SpotFeather"] = settings.SpotFeather.ToString(CultureInfo.InvariantCulture),
            ["SpotOpacity"] = settings.SpotOpacity.ToString(CultureInfo.InvariantCulture),
            ["SpotVisualizeThreshold"] = settings.SpotVisualizeThreshold.ToString(CultureInfo.InvariantCulture),
            ["BrushMode"] = settings.BrushMode,
            ["BrushSize"] = settings.BrushSize.ToString(CultureInfo.InvariantCulture),
            ["BrushFeather"] = settings.BrushFeather.ToString(CultureInfo.InvariantCulture),
            ["BrushFlow"] = settings.BrushFlow.ToString(CultureInfo.InvariantCulture),
            [PasteGroupsKey] = System.Text.Json.JsonSerializer.Serialize(settings.PasteGroups),
            ["PresetGroups"] = System.Text.Json.JsonSerializer.Serialize(settings.PresetGroups),
            ["DevelopGroups"] = System.Text.Json.JsonSerializer.Serialize(settings.DevelopGroups),
            [FileTypeFilterKey] = settings.FileTypeFilter.ToString(),
            [BrowseThumbnailSizeKey] = settings.BrowseThumbnailSize.ToString(),
            [ShowCapturePairsKey] = settings.ShowCapturePairs.ToString(),
            [AppThemeKey] = settings.AppTheme.ToString(),
            [StripLocationDataKey] = settings.StripLocationData.ToString(),
            [OutputSharpeningKey] = settings.OutputSharpening.ToString()
        };

        AddWatermark(settings, values);
        if (!saveTips) return values;

        values["ShowTips"] = settings.ShowTips.ToString();
        values["BrowseTipsSeen"] = settings.BrowseTipsSeen.ToString();
        values["DevelopTipsSeen"] = settings.DevelopTipsSeen.ToString();
        values["ExportTipsSeen"] = settings.ExportTipsSeen.ToString();

        return values;
    }

    public Task SaveFirstRunVersionAsync(int version)
    {
        return _catalogService.SetAppSettingAsync(
            FirstRunExperienceVersionKey,
            version.ToString());
    }

    private static OutputSharpeningMode ParseOutputSharpening(string? value)
    {
        if (Enum.TryParse<OutputSharpeningMode>(
                value,
                ignoreCase: true,
                out var mode) &&
            Enum.IsDefined(mode))
        {
            return mode;
        }

        return bool.TryParse(value, out var legacyEnabled)
            ? legacyEnabled
                ? OutputSharpeningMode.Screen
                : OutputSharpeningMode.Off
            : OutputSharpeningMode.Screen;
    }
}
