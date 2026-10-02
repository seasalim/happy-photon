using System.Text.Json;
using HappyPhoton.Services;
using ImageMagick;
using ImageMagick.Drawing;
using Xunit;

namespace HappyPhoton.Tests;

internal static class StraightenGateHorizontalEligibility
{
    internal static void ReportLeica(BaseImage basis, ITestOutputHelper output)
    {
        const string reason = "No two qualifying horizontals: gate bars span less than 300 px; " +
            "the exposed right fence rail spans about 265 px. The sofa occludes the remainder.";
        var imagePath = StraightenGateFixtures.ImagePath("m2462362.DNG");
        using var view = new MagickImage(basis.Pixels);
        view.AutoLevel();
        view.GammaCorrect(2.2);
        view.Depth = 8;
        new Drawables().FillColor(MagickColors.Transparent).StrokeWidth(2)
            .StrokeColor(MagickColors.Red).Rectangle(0, 165, 290, 218)
            .Rectangle(0, 686, 290, 728)
            .StrokeColor(MagickColors.Lime).Rectangle(1335, 672, 1599, 714)
            .FontPointSize(20).StrokeColor(MagickColors.Black).StrokeWidth(.5)
            .FillColor(MagickColors.White)
            .Text(20, 35, "G3 UNMEASURABLE: no two fixed horizontal edges >=400 px")
            .Text(20, 65, "Gate bars <300 px; exposed right fence rail about 265 px")
            .Text(20, 95, "No angle, delta or L assigned; prior vertical labels rejected")
            .Draw(view);
        view.Write(imagePath);
        output.WriteLine("STRAIGHTEN " + JsonSerializer.Serialize(new
        {
            gate = "G3", pid = Environment.ProcessId,
            values = new
            {
                fixture = "m2462362.DNG", width = basis.Pixels.Width, height = basis.Pixels.Height,
                correctionAngles = new double?[] { null, null }, difference = (double?)null,
                L = (double?)null, valid = false, measurable = false, reason, imagePath
            }
        }));
    }
}
