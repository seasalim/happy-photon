using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal enum OpsClarity { Gaussian, Guided }
internal readonly record struct OpsArm(string Name, double Texture = 0, double Clarity = 0,
    double Dehaze = 0, bool Local = false)
{
    internal static readonly OpsArm Off = new("off");
    internal static readonly OpsArm Stack = new("OP", 40, 40, 30);
    internal static readonly OpsArm Locals = new("LPP8", Local: true);
    internal static OpsArm[] All => [new("TX+", 60), new("TX-", -60),
        new("CL+", Clarity: 60), new("CL-", Clarity: -60),
        new("DH+", Dehaze: 50), new("DH-", Dehaze: -50), Stack];
}

internal static class OpsWorkloads
{
    internal const int PyramidEdge = 256, LatticeEdge = 48;
    internal const double ClaritySigma = .01, GuidedEpsilon = .0025;
    internal const double TextureThreshold = .01, HaloLimit = .04;
    internal const double TransmissionFloor = .1, DarkStrength = .95;
    internal static readonly double[] HazeAirlight = [.65, .70, .75];
    internal const double HazeTransmission = .975, HazeVariation = .0125;

    internal static BaseImage Skyline(int edge)
    {
        var h = edge * 2 / 3;
        var values = new ushort[edge * h * 3];
        for (var y = 0; y < h; y++) for (var x = 0; x < edge; x++)
        for (var c = 0; c < 3; c++) values[(y * edge + x) * 3 + c] = Q(x < edge / 2 ? .08 : .65);
        return RenderPipelineTestSupport.CreateBase(values, height: h);
    }

    internal static BaseImage Haze(BaseImage clear, double transmission = HazeTransmission, double variation = HazeVariation)
    {
        var image = new ImageMagick.MagickImage(clear.Pixels);
        using var pixels = image.GetPixels();
        var rgb = pixels.GetArea(0, 0, image.Width, image.Height)!;
        var w = (int)image.Width; var h = (int)image.Height;
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var t = transmission + variation * Math.Cos(Math.PI * (x + .5) / w) * Math.Cos(Math.PI * (y + .5) / h);
            for (var c = 0; c < 3; c++)
            {
                var i = (y * w + x) * 3 + c;
                rgb[i] = Q(t * rgb[i] / 65535d + (1 - t) * HazeAirlight[c]);
            }
        }
        pixels.SetArea(0, 0, image.Width, image.Height, rgb);
        return new(image, clear.Info);
    }

    internal static EditSettings Settings(OpsArm arm) => arm.Local ? HealWorkloads.LH8() : new();
    internal static ushort Q(double value) => (ushort)Math.Round(Math.Clamp(value, 0, 1) * 65535,
        MidpointRounding.AwayFromZero);
    internal static double Luma(double r, double g, double b) =>
        Rec2020Luminance.Red * r + Rec2020Luminance.Green * g + Rec2020Luminance.Blue * b;
}

