#:project ../HappyPhoton.csproj

using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

// Renders the website's before/after photographs from the CC0 RAW fixtures in
// Tests/assets through Happy Photon's own export pipeline. "before" is the
// default render with no edits; "after" applies the listed global edits.
// Framing crops happen after export so both halves of a pair line up.
var projectRoot = Path.GetFullPath(args.Length == 1 ? args[0] : Directory.GetCurrentDirectory());
var assets = Path.Combine(projectRoot, "Tests", "assets");
var work = Path.Combine(projectRoot, "artifacts", "site-photos");
var output = Path.Combine(projectRoot, "site", "assets", "images", "photos");
Directory.CreateDirectory(output);

var shots = new (string Source, string Name, EditSettings Edit, (double L, double T, double R, double B) Frame)[]
{
    ("fujifilm-x30.raf", "valley", new EditSettings
    {
        Exposure = -0.15, Contrast = 45, Highlights = -35, Shadows = 25, Vibrance = 60, Saturation = 22, Curve = SCurve(0.07),
        Wb = new WhiteBalanceSettings { Mode = WbMode.Custom, Kelvin = 6400, Tint = 4 }
    }, (0, 0.1, 1, 0.94)),
    ("canon-eos-350d.cr2", "canal", new EditSettings
    {
        Exposure = 0.7, Contrast = 40, Highlights = -25, Shadows = 40, Vibrance = 55, Saturation = 18, Curve = SCurve(0.08),
        Wb = new WhiteBalanceSettings { Mode = WbMode.Custom, Kelvin = 6800, Tint = 10 }
    }, (0, 0, 1, 1)),
    ("canon-eos-6d-iso-6400.cr2", "milky-way", new EditSettings
    {
        Exposure = 1.3, Contrast = 45, Highlights = -10, Shadows = 10, Vibrance = 45, Saturation = 15, Curve = SCurve(0.1),
        Wb = new WhiteBalanceSettings { Mode = WbMode.Custom, Kelvin = 3900, Tint = 6 }
    }, (0.24, 0.1, 0.76, 0.62)),
    ("nikon-d300-colorchecker.nef", "colorchecker", new EditSettings
    {
        Exposure = 0.35, Contrast = 20, Vibrance = 20, Curve = SCurve(0.04)
    }, (0.1, 0.16, 0.9, 0.9)),

};

var service = new ImageExportService(new RenderPipeline(), new RawBaseLoader(), new ExportMetadataService());
foreach (var (source, name, edit, frame) in shots)
{
    foreach (var (suffix, settings) in new[] { ("before", new EditSettings()), ("after", edit) })
    {
        var pairWork = Path.Combine(work, $"{name}-{suffix}");
        if (Directory.Exists(pairWork)) Directory.Delete(pairWork, true);
        Directory.CreateDirectory(pairWork);
        var copy = Path.Combine(pairWork, $"{name}-{suffix}{Path.GetExtension(source)}");
        File.Copy(Path.Combine(assets, source), copy);
        var file = new ImageFile(copy) { EditSettings = settings };
        var exportSettings = new ExportSettings
        {
            OutputFolder = Path.Combine(pairWork, "out"),
            Format = ExportFormat.Png,
            OutputColorSpace = OutputColorSpace.Srgb,
            OutputSharpening = OutputSharpeningMode.Screen
        };
        var count = await service.ExportBatchAsync([file], exportSettings, [new ExportVariant("web", 2400)], useSubfolders: false);
        var png = Directory.GetFiles(Path.Combine(pairWork, "out"), "*.png").Single();
        using var image = new MagickImage(png);
        var (w, h) = ((int)image.Width, (int)image.Height);
        image.Crop(new MagickGeometry((int)(frame.L * w), (int)(frame.T * h), (uint)((frame.R - frame.L) * w), (uint)((frame.B - frame.T) * h)));
        image.ResetPage();
        foreach (var width in new uint[] { 720, 1200, 2000 })
        {
            using var sized = (MagickImage)image.Clone();
            sized.Resize(width, 0);
            sized.Quality = 82;
            sized.Write(Path.Combine(output, $"{name}-{suffix}-{width}.webp"), MagickFormat.WebP);
        }
        if (count != 1) throw new InvalidOperationException($"Export failed for {name}-{suffix}.");
    }
}
return 0;

static CurveData SCurve(double strength)
{
    var curve = new CurveData();
    curve.Points.Clear();
    curve.Points.Add(new CurvePoint(0, 0));
    curve.Points.Add(new CurvePoint(0.25, 0.25 - strength));
    curve.Points.Add(new CurvePoint(0.75, 0.75 + strength));
    curve.Points.Add(new CurvePoint(1, 1));
    curve.BuildLookupTable();
    return curve;
}
