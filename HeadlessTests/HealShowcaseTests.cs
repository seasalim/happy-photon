using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HealShowcaseTests
{
    [AvaloniaFact]
    public void HealS64Raw()
    {
        var file = new ImageFile(GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2"));
        var availability = new SourceAvailabilityService();
        Assert.Equal(SourceAvailability.AvailableLocally, availability.GetAvailability(file.FilePath));
        var loader = new GatedBaseImageLoader(new RawBaseLoader(), availability);
        using var pair = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair); Assert.Equal(1600u, pair.Interactive.Pixels.Width);
        var spots = HealWorkloads.S64();
        var settings = new EditSettings { Repairs = HealWorkloads.Repairs(spots) };
        using var before = new RenderPipeline().Render(new(pair.Interactive, new(), RenderIntent.Preview, null, new(false, false)));
        using var after = new RenderPipeline().Render(new(pair.Interactive, settings, RenderIntent.Preview, null, new(false, false)));
        var sheet = new UniformGrid { Columns = 3, Rows = 2 };
        var owned = new List<Bitmap>();
        try
        {
            foreach (var index in new[] { 0, 1, 2, 6, 7, 15 })
            {
                var spot = spots[index];
                var side = (uint)Math.Max(36, Math.Ceiling(spot.Radius * 1600 * 2.6));
                var crop = new MagickGeometry((int)(spot.U * before.Image.Width - side / 2),
                    (int)(spot.V * before.Image.Height - side / 2), side, side);
                var pictures = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
                foreach (var image in new[] { before.Image, after.Image })
                {
                    using var patch = new MagickImage(image);
                    patch.Crop(crop);
                    patch.ResetPage();
                    patch.GammaCorrect(3);
                    var bitmap = BitmapConversionService.ConvertToBitmap(patch)!; owned.Add(bitmap);
                    pictures.Children.Add(new Image { Source = bitmap, Width = 148, Height = 148, Stretch = Stretch.Uniform });
                }
                sheet.Children.Add(new StackPanel { Margin = new Thickness(8), Spacing = 8, Children =
                {
                    new TextBlock { Text = $"{(spot.IsClone ? "Clone" : "Heal")} {index + 1} - Spot-free / Repaired" }, pictures
                } });
            }
            var window = new Window { Content = new StackPanel { Margin = new Thickness(12), Spacing = 12, Children =
            {
                new TextBlock { Text = "S64 - Canon RAW at 1600 - all crops: gamma 3 display lift", FontSize = 18 }, sheet
            } } };
            ShowcaseTestHelper.Capture("heal-s64-raw", window, new PixelSize(1000, 440), HappyPhotonThemes.MidGray);
        }
        finally { foreach (var bitmap in owned) bitmap.Dispose(); }
    }

    [AvaloniaFact]
    public void HealHighlight()
    {
        const int width = 160, height = 100;
        var values = new ushort[width * height * 3];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) for (var c = 0; c < 3; c++)
            values[(y * width + x) * 3 + c] = (ushort)((x < 80 ? 18000 : 46000) + x * 50 + y * 20 + c * 100 +
                33000 * Math.Exp(-((x - 44.3) * (x - 44.3) + (y - 49.5) * (y - 49.5)) / 40));
        using var basis = new BaseImage(RawBaseLoader.ImportRgb16(MemoryMarshal.AsBytes(values.AsSpan()), width, height),
            new(BaseSourceKind.Standard, false, BaseDecodeSettings.Default, null, null, 6504, 0, false, null, 1, width, height));
        using var before = new RenderPipeline().Render(new(basis, new() { Exposure = -2 }, RenderIntent.Preview, null, new(false, false)));
        using var after = new RenderPipeline().Render(new(basis, new() { Exposure = -2,
            Repairs = [new() { U = .51, V = .5, Su = .28, Sv = .5, Radius = .1, Feather = .2 }] },
            RenderIntent.Preview, null, new(false, false)));
        using var left = BitmapConversionService.ConvertToBitmap(before.Image)!;
        using var right = BitmapConversionService.ConvertToBitmap(after.Image)!;
        var window = new Window { Content = new StackPanel { Margin = new Thickness(16), Spacing = 16, Children =
        {
            new TextBlock { Text = "Highlight edge, then -2 EV - Spot-free / Repaired", FontSize = 18 },
            new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 16, Children =
            {
                new Image { Source = left, Width = 384, Height = 240 },
                new Image { Source = right, Width = 384, Height = 240 }
            } }
        } } };
        ShowcaseTestHelper.Capture("heal-highlight", window, new PixelSize(840, 320), HappyPhotonThemes.MidGray);
    }
}
