#:project ../HappyPhoton.csproj

using ImageMagick;

if (args.Length > 1)
{
    Console.Error.WriteLine("Usage: dotnet run --file scripts/generate-site-images.cs -- [repository-root]");
    return 1;
}

var projectRoot = Path.GetFullPath(args.Length == 1 ? args[0] : Directory.GetCurrentDirectory());
var screenshotRoot = Path.Combine(projectRoot, "docs", "screenshots");
var outputRoot = Path.Combine(projectRoot, "site", "assets", "images");

if (!File.Exists(Path.Combine(projectRoot, "HappyPhoton.csproj")))
{
    Console.Error.WriteLine($"Repository root not found: {projectRoot}");
    return 1;
}

Directory.CreateDirectory(outputRoot);

var images = new[]
{
    new SiteImage("Screenshot_Browse.png", "library"),
    new SiteImage("Screenshot_Develop.png", "develop"),
    new SiteImage("Screenshot_Develop_MidGray_Assess.png", "assess")
};
var widths = new uint[] { 720, 1200, 1800 };

foreach (var image in images)
{
    var sourcePath = Path.Combine(screenshotRoot, image.SourceName);
    if (!File.Exists(sourcePath))
    {
        Console.Error.WriteLine($"Site screenshot not found: {sourcePath}");
        return 1;
    }

    using var source = new MagickImage(sourcePath);
    source.AutoOrient();
    source.Strip();

    foreach (var width in widths)
    {
        using var output = (MagickImage)source.Clone();
        output.FilterType = FilterType.Lanczos;
        output.Resize(new MagickGeometry(width, 0));
        output.Format = MagickFormat.WebP;
        output.Quality = 82;
        output.Settings.SetDefine(MagickFormat.WebP, "method", "6");
        output.Settings.SetDefine(MagickFormat.WebP, "thread-level", "0");
        output.Strip();

        var outputPath = Path.Combine(outputRoot, $"{image.OutputStem}-{width}.webp");
        output.Write(outputPath);
        Console.WriteLine($"Wrote {Path.GetRelativePath(projectRoot, outputPath)} ({output.Width}x{output.Height})");
    }
}

return 0;

internal sealed record SiteImage(string SourceName, string OutputStem);
