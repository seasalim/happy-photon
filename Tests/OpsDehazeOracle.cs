using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal sealed record OpsDehazeOracle(OpsGrid[] Color, double[] Airlight, double[] Transmission)
{
    internal static OpsDehazeOracle Build(ushort[] rgb, int width, int height, double[,] wb)
    {
        var color = new OpsGrid[3];
        for (var c = 0; c < 3; c++)
        {
            var channel = new double[width * height];
            for (var i = 0; i < channel.Length; i++)
                for (var j = 0; j < 3; j++) channel[i] += wb[c, j] * rgb[i * 3 + j] / 65535d;
            color[c] = OpsPresenceOracle.Reduce(channel, width, height, 48);
        }
        var weights = Enumerable.Range(0, color[0].Values.Length).Select(i =>
            Math.Pow(Math.Max(0, color.Min(channel => channel.Values[i])), 8) + 1e-12).ToArray();
        var air = color.Select(channel => Math.Max(1d / 65535, channel.Values.Zip(weights, (v, w) => v * w).Sum() / weights.Sum())).ToArray();
        var transmission = Enumerable.Range(0, weights.Length).Select(i => Math.Clamp(1 - .95 *
            Enumerable.Range(0, 3).Min(c => color[c].Values[i] / air[c]), .1, 1)).ToArray();
        return new(color, air, transmission);
    }

    internal double[] Correct(double[] value, double u, double v, double amount, bool refine)
    {
        if (amount == 0) return (double[])value.Clone();
        var w = Color[0].Width; var h = Color[0].Height;
        var t = OpsPresenceOracle.Sample(Transmission, w, h, u, v);
        if (refine)
        {
            var x = Math.Clamp(u * w - .5, 0, w - 1); var y = Math.Clamp(v * h - .5, 0, h - 1);
            var terms = new List<(double Weight, double T)>();
            for (var dy = 0; dy <= 1; dy++) for (var dx = 0; dx <= 1; dx++)
            {
                var i = Math.Min((int)y + dy, h - 1) * w + Math.Min((int)x + dx, w - 1);
                var difference = OpsWorkloads.Luma(value[0], value[1], value[2]) -
                    OpsWorkloads.Luma(Color[0].Values[i], Color[1].Values[i], Color[2].Values[i]);
                var spatial = Math.Abs(1 - dx - (x - (int)x)) * Math.Abs(1 - dy - (y - (int)y));
                var weight = spatial * Math.Exp(-difference * difference / .005) + 1e-30;
                terms.Add((weight, Transmission[i]));
            }
            t = terms.Sum(p => p.Weight * p.T) / terms.Sum(p => p.Weight);
        }
        var divisor = Math.Max(.1, 1 - Math.Abs(amount) / 100 * (1 - t));
        return Enumerable.Range(0, 3).Select(c => amount > 0 ? Airlight[c] + (value[c] - Airlight[c]) / divisor :
            Airlight[c] + (value[c] - Airlight[c]) * divisor).ToArray();
    }

    internal static ushort[] Tone(double[] balanced, BaseImageInfo info, EditSettings settings)
    {
        var output = new ushort[3];
        if (!info.IsRawSource)
        {
            var p = new ToneParams(settings.Exposure + info.SourceExposureBiasEv, 1, settings.Brightness,
                settings.Contrast, settings.Shadows, settings.Highlights, settings.BaseLook ?? false,
                settings.Curve, settings.CurveRed, settings.CurveGreen, settings.CurveBlue);
            CurveData?[] curves = [settings.CurveRed, settings.CurveGreen, settings.CurveBlue];
            for (var c = 0; c < 3; c++) output[c] = OpsWorkloads.Q(ToneLut.Evaluate(p, Math.Max(0, balanced[c]), curves[c]));
            return output;
        }
        var parameters = new AgxToneParameters(settings.Exposure, info.SourceExposureBiasEv, settings.Contrast,
            settings.Highlights, settings.Shadows, settings.Curve, settings.CurveRed, settings.CurveGreen, settings.CurveBlue);
        var inset = new double[3]; var tone = new double[3];
        CurveData?[] channelCurves = [settings.CurveRed, settings.CurveGreen, settings.CurveBlue];
        for (var c = 0; c < 3; c++)
        {
            for (var j = 0; j < 3; j++) inset[c] += AgxToneEngine.InsetMatrix[c, j] * balanced[j];
            tone[c] = AgxToneEngine.EvaluateToneExtendedUnchecked(inset[c], parameters,
                Math.Pow(2, parameters.ExposureEv + parameters.SourceExposureEv), 0,
                AgxToneEngine.Slope(parameters.Contrast), AgxToneEngine.ToePower(parameters.Shadows),
                AgxToneEngine.ShoulderPower(parameters.Highlights), channelCurves[c]);
        }
        for (var c = 0; c < 3; c++)
        {
            double linear = 0;
            for (var j = 0; j < 3; j++) linear += AgxToneEngine.OutsetMatrix[c, j] * tone[j];
            output[c] = OpsWorkloads.Q(ToneLut.SrgbEncode(Math.Clamp(linear, 0, 1)));
        }
        return output;
    }
}
