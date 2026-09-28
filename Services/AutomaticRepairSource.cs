using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

public readonly record struct RepairSourceCandidate(double U, double V, double Score);

/// <summary>Bounded, deterministic UI-action search on an immutable interactive base.</summary>
public static class AutomaticRepairSource
{
    public static IReadOnlyList<RepairSourceCandidate> Rank(BaseImage image, Repair repair)
    {
        var width = (int)image.Pixels.Width;
        var height = (int)image.Pixels.Height;
        var radius = RepairGeometry.EffectiveRadius(repair.Radius, width, height);
        var separation = 2 * radius + Math.Sqrt((double)width * width + (double)height * height) / Repair.CoordinateScale;
        using var pixels = image.Pixels.GetPixelsUnsafe();
        var destination = Samples(repair.U, repair.V);
        var candidates = new List<RepairSourceCandidate>();
        for (var ring = 0; ring < 6; ring++)
        {
            var distance = radius * (1.5 + ring * .5);
            for (var angle = 0; angle < 24; angle++)
            {
                var theta = angle * Math.Tau / 24;
                var u = repair.U + Math.Cos(theta) * distance / width;
                var v = repair.V + Math.Sin(theta) * distance / height;
                var trial = repair with { Su = u, Sv = v };
                (u, v) = RepairGeometry.ClampSource(trial, width, height);
                var dx = (u - repair.U) * width;
                var dy = (v - repair.V) * height;
                if (dx * dx + dy * dy <= separation * separation) continue;
                var source = Samples(u, v);
                double mismatch = 0, texture = 0;
                for (var i = 0; i < source.Length; i++)
                {
                    mismatch += Math.Abs(source[i] - destination[i]);
                    var next = (i + 3) % source.Length;
                    texture += Math.Abs(Math.Abs(source[i] - source[next]) -
                        Math.Abs(destination[i] - destination[next]));
                }
                candidates.Add(new(u, v, mismatch + texture));
            }
        }
        return candidates.DistinctBy(candidate => (candidate.U, candidate.V)).OrderBy(candidate => candidate.Score).ToArray();

        double[] Samples(double u, double v)
        {
            var samples = new double[48];
            for (var i = 0; i < 16; i++)
            {
                var theta = i * Math.Tau / 16;
                var x = Math.Clamp((int)Math.Round(u * width - .5 + Math.Cos(theta) * radius * 1.5), 0, width - 1);
                var y = Math.Clamp((int)Math.Round(v * height - .5 + Math.Sin(theta) * radius * 1.5), 0, height - 1);
                var pixel = pixels.GetPixel(x, y);
                for (var channel = 0; channel < 3; channel++) samples[i * 3 + channel] = pixel[(uint)channel];
            }
            return samples;
        }
    }
}
