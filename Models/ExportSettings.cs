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
        if (variants.Count == 0) variants.Add(new ExportVariant("hi-res", null));

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
}
