using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HappyPhoton.Models;

public enum OutputSharpeningMode
{
    Off,
    Screen,
    Print
}

/// <summary>
/// Settings for batch export operations.
/// </summary>
public partial class ExportSettings : ObservableObject
{
    [ObservableProperty]
    private string _outputFolder = string.Empty;

    [ObservableProperty]
    private int _quality = 85;

    [ObservableProperty]
    private ExportFormat _format = ExportFormat.Jpeg;

    [ObservableProperty]
    private OutputColorSpace _outputColorSpace = OutputColorSpace.Srgb;

    [ObservableProperty]
    private bool _exportHiRes = true;

    [ObservableProperty]
    private bool _exportWeb;

    [ObservableProperty]
    private bool _exportSmall;

    [ObservableProperty]
    private int _webMaxSize = 2048;

    [ObservableProperty]
    private int _smallMaxSize = 1024;

    [ObservableProperty]
    private string _namingPattern = "{name}";

    [ObservableProperty]
    private bool _stripLocationData;

    [ObservableProperty]
    private OutputSharpeningMode _outputSharpening = OutputSharpeningMode.Screen;

    [ObservableProperty]
    private bool _showProof;

    /// <summary>
    /// Generate output filename based on naming pattern.
    /// </summary>
    public string GetOutputFileName(string originalFileName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(originalFileName);
        var date = DateTime.Now.ToString("yyyyMMdd");

        var result = NamingPattern
            .Replace("{name}", nameWithoutExt)
            .Replace("{date}", date);

        return result + FileExtension;
    }

    public string FileExtension => Format switch
    {
        ExportFormat.Png => ".png",
        ExportFormat.Webp => ".webp",
        ExportFormat.Tiff => ".tif",
        _ => ".jpg"
    };

    public IReadOnlyList<ExportVariant> GetActiveVariants()
    {
        var variants = new List<ExportVariant>();
        if (ExportHiRes) variants.Add(new ExportVariant("hi-res", null));
        if (ExportWeb) variants.Add(new ExportVariant("web", Math.Clamp(WebMaxSize, 16, 65536)));
        if (ExportSmall) variants.Add(new ExportVariant("small", Math.Clamp(SmallMaxSize, 16, 65536)));

        return variants
            .OrderBy(v => v.MaxDimension.HasValue ? 1 : 0)
            .ThenByDescending(v => v.MaxDimension ?? 0)
            .ToList();
    }

    public string GetOutputPath(string originalFileName, ExportVariant variant, bool useSubfolders)
    {
        var folder = useSubfolders
            ? Path.Combine(OutputFolder, variant.Name)
            : OutputFolder;
        return Path.Combine(folder, GetOutputFileName(originalFileName));
    }

    public ExportJob CreateJob(
        IEnumerable<ImageFile> captures,
        IReadOnlyList<ExportVariant>? recipes = null,
        bool? useSubfolders = null)
    {
        var resolvedRecipes = recipes ?? GetActiveVariants();
        return ExportJob.Create(
            captures,
            this,
            resolvedRecipes,
            useSubfolders ?? resolvedRecipes.Count > 1);
    }
}

public sealed record ExportOutputSettings(
    string OutputFolder,
    int Quality,
    ExportFormat Format,
    OutputColorSpace OutputColorSpace,
    string NamingPattern,
    bool StripLocationData,
    OutputSharpeningMode OutputSharpening)
{
    internal ExportSettings CreateEncoderSettings() => new()
    {
        OutputFolder = OutputFolder,
        Quality = Quality,
        Format = Format,
        OutputColorSpace = OutputColorSpace,
        NamingPattern = NamingPattern,
        StripLocationData = StripLocationData,
        OutputSharpening = OutputSharpening
    };
}

public sealed record ExportTarget(
    ImageFile Capture,
    ExportVariant Recipe,
    ExportOutputSettings Output,
    string ResolvedPath,
    bool OverwriteAuthorized = false);

public sealed record ExportPathCollision(
    string ResolvedPath,
    IReadOnlyList<ExportTarget> Targets);

public sealed class ExportJob
{
    private readonly IReadOnlyDictionary<ImageFile, EditSettings> _editSettings;

    public IReadOnlyList<ImageFile> Captures { get; }
    public IReadOnlyList<ExportVariant> Recipes { get; }
    public ExportOutputSettings Output { get; }
    public IReadOnlyList<ExportTarget> Targets { get; }
    public IReadOnlyList<ExportPathCollision> PathCollisions { get; }
    public bool HasPathCollisions => PathCollisions.Count > 0;

    private ExportJob(
        IReadOnlyList<ImageFile> captures,
        IReadOnlyList<ExportVariant> recipes,
        ExportOutputSettings output,
        IReadOnlyDictionary<ImageFile, EditSettings> editSettings,
        IReadOnlyList<ExportTarget> targets,
        IReadOnlyList<ExportPathCollision> pathCollisions)
    {
        Captures = captures;
        Recipes = recipes;
        Output = output;
        _editSettings = editSettings;
        Targets = targets;
        PathCollisions = pathCollisions;
    }

    internal EditSettings GetEditSettings(ImageFile capture) =>
        _editSettings[capture];

    internal ExportJob AuthorizeOverwrites(IEnumerable<string> paths)
    {
        var authorized = new HashSet<string>(
            paths.Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);
        return ProjectTargets(Targets.Select(target => authorized.Contains(
            target.ResolvedPath)
                ? target with { OverwriteAuthorized = true }
                : target with { OverwriteAuthorized = false }));
    }

    internal ExportJob ProjectTargets(IEnumerable<ExportTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var targetSnapshot = Array.AsReadOnly(targets.ToArray());
        var captures = Array.AsReadOnly(Captures
            .Where(capture => targetSnapshot.Any(target =>
                ReferenceEquals(target.Capture, capture)))
            .ToArray());
        var recipes = Array.AsReadOnly(Recipes
            .Where(recipe => targetSnapshot.Any(target =>
                target.Recipe == recipe))
            .ToArray());
        var edits = new ReadOnlyDictionary<ImageFile, EditSettings>(
            _editSettings
                .Where(pair => captures.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value));
        return new ExportJob(
            captures,
            recipes,
            Output,
            edits,
            targetSnapshot,
            FindPathCollisions(targetSnapshot));
    }

    public static ExportJob Create(
        IEnumerable<ImageFile> captures,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> recipes,
        bool useSubfolders)
    {
        ArgumentNullException.ThrowIfNull(captures);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(recipes);

        var captureSnapshot = Array.AsReadOnly(captures.ToArray());
        var recipeSnapshot = Array.AsReadOnly(recipes
            .Select(recipe => new ExportVariant(recipe.Name, recipe.MaxDimension))
            .OrderBy(recipe => recipe.MaxDimension.HasValue ? 1 : 0)
            .ThenByDescending(recipe => recipe.MaxDimension ?? 0)
            .ToArray());
        var output = new ExportOutputSettings(
            settings.OutputFolder,
            settings.Quality,
            settings.Format,
            settings.OutputColorSpace,
            settings.NamingPattern,
            settings.StripLocationData,
            settings.OutputSharpening);
        var dateToken = DateTime.Now.ToString("yyyyMMdd");
        var edits = new Dictionary<ImageFile, EditSettings>();
        var versionedPaths = captureSnapshot
            .GroupBy(capture => capture.FilePath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(capture => capture.Version).Distinct().Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = new List<ExportTarget>(
            captureSnapshot.Count * recipeSnapshot.Count);
        foreach (var capture in captureSnapshot)
        {
            edits.Add(capture, capture.EditSettings.Clone());
            foreach (var recipe in recipeSnapshot)
            {
                targets.Add(new ExportTarget(
                    capture,
                    recipe,
                    output,
                    ResolvePath(capture.FileName, recipe, output, useSubfolders,
                        dateToken, versionedPaths.Contains(capture.FilePath)
                            ? $"-V{capture.Version}"
                            : string.Empty)));
            }
        }

        var targetSnapshot = targets.AsReadOnly();
        return new ExportJob(
            captureSnapshot,
            recipeSnapshot,
            output,
            new ReadOnlyDictionary<ImageFile, EditSettings>(edits),
            targetSnapshot,
            FindPathCollisions(targetSnapshot));
    }

    private static IReadOnlyList<ExportPathCollision> FindPathCollisions(
        IReadOnlyList<ExportTarget> targets) => targets
            .GroupBy(target => target.ResolvedPath,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new ExportPathCollision(
                group.First().ResolvedPath,
                new ReadOnlyCollection<ExportTarget>(group.ToList())))
            .ToList()
            .AsReadOnly();

    private static string ResolvePath(
        string originalFileName,
        ExportVariant recipe,
        ExportOutputSettings output,
        bool useSubfolders,
        string dateToken,
        string versionSuffix)
    {
        var name = Path.GetFileNameWithoutExtension(originalFileName);
        var fileName = output.NamingPattern
            .Replace("{name}", name)
            .Replace("{date}", dateToken) + versionSuffix +
            GetExtension(output.Format);
        var folder = useSubfolders
            ? Path.Combine(output.OutputFolder, recipe.Name)
            : output.OutputFolder;
        return Path.GetFullPath(Path.Combine(folder, fileName));
    }

    private static string GetExtension(ExportFormat format) => format switch
    {
        ExportFormat.Png => ".png",
        ExportFormat.Webp => ".webp",
        ExportFormat.Tiff => ".tif",
        _ => ".jpg"
    };
}
