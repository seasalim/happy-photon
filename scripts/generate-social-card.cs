#:project ../HappyPhoton.csproj

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ImageMagick;
using ImageMagick.Drawing;

if (args.Length > 1)
{
    Console.Error.WriteLine("Usage: dotnet run --file scripts/generate-social-card.cs -- [repository-root]");
    return 1;
}

var root = Path.GetFullPath(args.Length == 1 ? args[0] : Directory.GetCurrentDirectory());
var iconPath = Path.Combine(root, "Assets", "happy-photon-icon.svg");
var screenshotPath = Path.Combine(root, "docs", "screenshots", "Screenshot_Browse.png");
var headingFont = Path.Combine(root, "Assets", "Fonts", "Sora-Bold.ttf");
var bodyFont = Path.Combine(root, "Assets", "Fonts", "HankenGrotesk-Regular.ttf");
var tokensPath = Path.Combine(root, "site", "assets", "css", "tokens.css");
var outputPath = Path.Combine(root, "site", "assets", "images", "social-card.png");

foreach (var input in new[] { iconPath, screenshotPath, headingFont, bodyFont, tokensPath })
{
    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"Social card input not found: {input}");
        return 1;
    }
}

using var card = new MagickImage(new MagickColor("#0f0f14"), 1200, 630);
AddGlow(card, "#3aa6b940", 770, 230, 290, 160);
AddGlow(card, "#7213ff35", 1050, 500, 300, 150);

using var screenshot = new MagickImage(screenshotPath);
screenshot.Resize(800, 0);
screenshot.VirtualPixelMethod = VirtualPixelMethod.Transparent;
screenshot.BackgroundColor = MagickColors.Transparent;
var width = (double)screenshot.Width;
var height = (double)screenshot.Height;
screenshot.Distort(new DistortSettings(DistortMethod.Perspective)
{
    Viewport = new MagickGeometry(1200, 630)
}, [0, 0, 610, 160, width, 0, 1220, 90, width, height, 1290, 485, 0, height, 610, 515]);
screenshot.ResetPage();

using (var shadow = (MagickImage)screenshot.Clone())
{
    shadow.Colorize(MagickColors.Black, new Percentage(100));
    shadow.Blur(0, 18);
    card.Composite(shadow, 0, 16, CompositeOperator.Over);
}

card.Composite(screenshot, CompositeOperator.Over);

using (var icon = new MagickImage(iconPath, new MagickReadSettings
{
    Width = 112,
    Height = 112,
    BackgroundColor = MagickColors.Transparent
}))
{
    card.Composite(icon, 64, 113, CompositeOperator.Over);
}

card.Settings.Font = headingFont;
card.Settings.FontPointsize = 43;
var happyWidth = card.FontTypeMetrics("Happy ")!.TextWidth;
new Drawables()
    .Font(headingFont).FontPointSize(43).StrokeColor(MagickColors.Transparent)
    .FillColor(new MagickColor("#ece9f1")).Text(64, 307, "Happy ")
    .FillColor(new MagickColor("#3aa6b9")).Text(64 + happyWidth, 307, "Photon")
    .Font(bodyFont).FontPointSize(29).FillColor(new MagickColor("#c0c0c0"))
    .Text(64, 365, "Photo editing, simplified.")
    .Draw(card);

using (var beam = SpectrumBeam(tokensPath))
{
    card.Composite(beam, 64, 426, CompositeOperator.Over);
}

card.Alpha(AlphaOption.Remove);
card.Format = MagickFormat.Png24;
card.Strip();
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
card.Write(outputPath);
Console.WriteLine($"Wrote {Path.GetRelativePath(root, outputPath)} ({card.Width}x{card.Height})");

return 0;

static void AddGlow(MagickImage card, string color, double x, double y, double rx, double ry)
{
    using var glow = new MagickImage(MagickColors.Transparent, card.Width, card.Height);
    new Drawables().FillColor(new MagickColor(color)).StrokeColor(MagickColors.Transparent)
        .Ellipse(x, y, rx, ry, 0, 360).Draw(glow);
    glow.Blur(0, 85);
    card.Composite(glow, CompositeOperator.Over);
}

static MagickImage SpectrumBeam(string tokensPath)
{
    var spectrum = Regex.Match(File.ReadAllText(tokensPath), @"--spectrum:\s*linear-gradient\(90deg,\s*([^;]+)\);");
    if (!spectrum.Success) throw new InvalidDataException("The site's horizontal spectrum token is missing.");

    var stops = spectrum.Groups[1].Value.Split(',');
    var gradient = new StringBuilder();

    for (var index = 0; index < stops.Length; index++)
    {
        var parts = stops[index].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var offset = parts.Length == 2 ? parts[1] : index == 0 ? "0%" : "100%";
        gradient.Append(CultureInfo.InvariantCulture,
            $"<stop offset='{offset}' stop-color='{parts[0]}'/>");
    }

    var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='430' height='3'>" +
        $"<defs><linearGradient id='beam'>{gradient}</linearGradient></defs>" +
        "<rect width='430' height='3' fill='url(#beam)'/></svg>";

    return new MagickImage(Encoding.UTF8.GetBytes(svg));
}
