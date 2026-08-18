#:project ../HappyPhoton.csproj
#:property PublishAot=false
#:property SelfContained=false
#:property RestoreIgnoreFailedSources=true

using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using System.Runtime.CompilerServices;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine(
        "Usage: dotnet run --file scripts/evaluate-wide-working-space.cs -- " +
        "<frozen-baseline-directory> [output-directory]");
    return 1;
}

var baselineDirectory = Path.GetFullPath(args[0]);
var outputDirectory = Path.GetFullPath(args.Length == 2
    ? args[1]
    : Path.Combine("artifacts", "wide-working-space"));
var renderDirectory = Path.Combine(outputDirectory, "renders");
var cropDirectory = Path.Combine(outputDirectory, "crop-sheets");
Directory.CreateDirectory(renderDirectory);
Directory.CreateDirectory(cropDirectory);

var repository = FindRepositoryRoot();
if (repository is null)
{
    Console.Error.WriteLine(
        "Could not locate HappyPhoton.sln from the script location or the " +
        "current directory. Run this script from a checkout of the repository.");
    return 2;
}
var assetDirectory = Path.Combine(repository, "Tests", "assets");
var loader = new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader());
var pipeline = new RenderPipeline();
var cases = new[]
{
    new EvaluationCase("canon-eos-350d", "canon-eos-350d.cr2", new EditSettings()),
    new EvaluationCase("canon-eos-350d__wb-3000", "canon-eos-350d.cr2",
        WhiteBalance(3000, 0)),
    new EvaluationCase("fujifilm-x30", "fujifilm-x30.raf", new EditSettings()),
    new EvaluationCase("display-p3-reference", "display-p3-reference.jpg",
        new EditSettings()),
    new EvaluationCase("display-p3-reference__wb-3000", "display-p3-reference.jpg",
        WhiteBalance(3000, 0)),
    new EvaluationCase("srgb-reference", "srgb-reference.jpg", new EditSettings())
};
var report = new List<string>
{
    "case\tnormalized-rmse\tbaseline\tcurrent\tcrop-sheet"
};

foreach (var item in cases)
{
    var sourcePath = Path.Combine(assetDirectory, item.FileName);
    using var baseImage = loader.LoadFullBase(
        new ImageFile(sourcePath),
        BaseDecodeSettings.Default,
        CancellationToken.None) ?? throw new InvalidOperationException(
            $"Could not load {sourcePath}.");
    using var rendered = pipeline.Render(new RenderRequest(
        baseImage,
        item.Settings,
        RenderIntent.Export,
        500,
        new RenderOptions(false, false)));
    var currentPath = Path.Combine(renderDirectory, $"{item.Name}.png");
    rendered.Image.Write(currentPath, MagickFormat.Png);

    var baselineName = ResolveBaselineName(item.Name);
    var baselinePath = Path.Combine(baselineDirectory, baselineName);
    if (!File.Exists(baselinePath))
    {
        Console.Error.WriteLine($"Missing frozen baseline: {baselinePath}");
        return 2;
    }

    using var baseline = new MagickImage(baselinePath);
    using var current = new MagickImage(currentPath);
    var distortion = baseline.Compare(current, ErrorMetric.RootMeanSquared);
    var cropPath = Path.Combine(cropDirectory, $"{item.Name}.png");
    WriteCropSheet(baseline, current, cropPath);
    report.Add(string.Join('\t',
        item.Name,
        distortion.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        baselinePath,
        currentPath,
        cropPath));
    Console.WriteLine($"{item.Name}: normalized RMSE {distortion:F6}");
}

var reportPath = Path.Combine(outputDirectory, "report.tsv");
File.WriteAllLines(reportPath, report);
Console.WriteLine($"Report: {reportPath}");
Console.WriteLine($"Side-by-side crops: {cropDirectory}");
return 0;

static string? FindRepositoryRoot([CallerFilePath] string scriptPath = "") =>
    SearchForRepositoryRoot(Path.GetDirectoryName(scriptPath))
        ?? SearchForRepositoryRoot(Directory.GetCurrentDirectory());

static string? SearchForRepositoryRoot(string? start)
{
    if (string.IsNullOrEmpty(start))
    {
        return null;
    }
    var directory = new DirectoryInfo(Path.GetFullPath(start));
    while (directory != null &&
        !File.Exists(Path.Combine(directory.FullName, "HappyPhoton.sln")))
    {
        directory = directory.Parent;
    }
    return directory?.FullName;
}

static string ResolveBaselineName(string name)
{
    var separator = name.IndexOf("__", StringComparison.Ordinal);
    var asset = separator < 0 ? name : name[..separator];
    var settings = separator < 0 ? "identity" : name[(separator + 2)..];
    return $"{asset}__{settings}.png";
}

static EditSettings WhiteBalance(double kelvin, double tint) => new()
{
    Wb = new WhiteBalanceSettings
    {
        Mode = WbMode.Custom,
        Kelvin = kelvin,
        Tint = tint
    }
};

static void WriteCropSheet(
    MagickImage baseline,
    MagickImage current,
    string outputPath)
{
    var crop = (uint)Math.Min(180, Math.Min(baseline.Width, baseline.Height));
    var positions = new (int X, int Y)[]
    {
        (0, 0),
        (checked((int)(baseline.Width - crop)), 0),
        (checked((int)((baseline.Width - crop) / 2)),
            checked((int)((baseline.Height - crop) / 2)))
    };
    using var sheet = new MagickImage(
        MagickColors.Black,
        crop * 2,
        crop * (uint)positions.Length);
    for (var index = 0; index < positions.Length; index++)
    {
        using var before = Crop(baseline, positions[index], crop);
        using var after = Crop(current, positions[index], crop);
        sheet.Composite(before, 0, checked((int)(index * crop)), CompositeOperator.Over);
        sheet.Composite(after, checked((int)crop), checked((int)(index * crop)),
            CompositeOperator.Over);
    }
    sheet.Write(outputPath, MagickFormat.Png);
}

static MagickImage Crop(MagickImage source, (int X, int Y) position, uint size)
{
    var result = (MagickImage)source.Clone();
    result.Crop(new MagickGeometry(position.X, position.Y, size, size));
    result.ResetPage();
    return result;
}

sealed record EvaluationCase(string Name, string FileName, EditSettings Settings);
