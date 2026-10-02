using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace HappyPhoton.Services;

public static partial class HorizonDetection
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe int Suppress(double[] gx, double[] gy, double[] magnitude,
        int width, int height, double high, double low, byte[] state, int[] pending)
    {
        var count = 0;

        fixed (double* samples = magnitude, px = gx, py = gy)
        {
            for (var y = Border + 1; y < height - Border - 1; y++)
            {
                var x = Border + 1;

                if (Avx2.IsSupported)
                {
                    var yVector = Vector256.Create((double)y);
                    var lowVector = Vector256.Create(low);

                    for (; x <= width - Border - 1 - 4; x += 4)
                    {
                        var i = y * width + x;
                        var m = Avx.LoadVector256(samples + i);
                        var eligible = Avx.CompareGreaterThanOrEqual(m, lowVector);
                        if (Avx.MoveMask(eligible) == 0) continue;

                        // Mask weak lanes before address calculation, including zero magnitude.
                        // Every sampled coordinate remains within one pixel of this interior row.
                        var divisor = Avx.BlendVariable(Vector256.Create(1d), m, eligible);
                        var dx = Avx.And(Avx.Divide(Avx.LoadVector256(px + i), divisor), eligible);
                        var dy = Avx.And(Avx.Divide(Avx.LoadVector256(py + i), divisor), eligible);
                        var xs = Vector256.Create((double)x, x + 1d, x + 2d, x + 3d);
                        var before = SampleFour(samples + i, width, xs, yVector, Avx.Subtract(xs, dx), Avx.Subtract(yVector, dy));
                        eligible = Avx.And(eligible, Avx.CompareGreaterThanOrEqual(m, before));
                        if (Avx.MoveMask(eligible) == 0) continue;

                        var after = SampleFour(samples + i, width, xs, yVector, Avx.Add(xs, dx), Avx.Add(yVector, dy));
                        var mask = Avx.MoveMask(Avx.And(eligible, Avx.CompareGreaterThan(m, after)));

                        for (var lane = 0; lane < 4; lane++)
                        {
                            if ((mask & (1 << lane)) == 0) continue;

                            var index = i + lane;
                            state[index] = magnitude[index] >= high ? (byte)2 : (byte)1;
                            if (state[index] == 2) pending[count++] = index;
                        }
                    }
                }

                // Portable path and SIMD tail share the original interpolation arithmetic.
                for (; x < width - Border - 1; x++)
                {
                    var i = y * width + x;
                    var m = magnitude[i];
                    if (m < low) continue;

                    var dx = gx[i] / m;
                    var dy = gy[i] / m;
                    var before = Sample(samples, width, x - dx, y - dy);
                    if (m < before) continue;

                    var after = Sample(samples, width, x + dx, y + dy);
                    if (m <= after) continue;

                    state[i] = m >= high ? (byte)2 : (byte)1;
                    if (state[i] == 2) pending[count++] = i;
                }
            }
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Vector256<double> SampleFour(double* centre, int width,
        Vector256<double> baseX, Vector256<double> baseY, Vector256<double> x, Vector256<double> y)
    {
        var ix = Avx.ConvertToVector256Double(Avx.ConvertToVector128Int32WithTruncation(x));
        var iy = Avx.ConvertToVector256Double(Avx.ConvertToVector128Int32WithTruncation(y));
        var fx = Avx.Subtract(x, ix);
        var fy = Avx.Subtract(y, iy);
        var left = Avx.CompareLessThan(ix, baseX);
        var right = Avx.CompareGreaterThan(ix, baseX);
        var above = Avx.CompareLessThan(iy, baseY);
        var below = Avx.CompareGreaterThan(iy, baseY);
        var upperLeft = Choose(Avx.LoadVector256(centre - width - 1), Avx.LoadVector256(centre - width),
            Avx.LoadVector256(centre - width + 1), left, right);
        var middleLeft = Choose(Avx.LoadVector256(centre - 1), Avx.LoadVector256(centre),
            Avx.LoadVector256(centre + 1), left, right);
        var lowerLeft = Choose(Avx.LoadVector256(centre + width - 1), Avx.LoadVector256(centre + width),
            Avx.LoadVector256(centre + width + 1), left, right);
        var upperRight = Avx.BlendVariable(Avx.LoadVector256(centre - width + 1),
            Avx.LoadVector256(centre - width), left);
        var middleRight = Avx.BlendVariable(Avx.LoadVector256(centre + 1), Avx.LoadVector256(centre), left);
        var lowerRight = Avx.BlendVariable(Avx.LoadVector256(centre + width + 1),
            Avx.LoadVector256(centre + width), left);
        var one = Vector256.Create(1d);
        var rx = Avx.Subtract(one, fx);
        var ry = Avx.Subtract(one, fy);
        var top = Avx.Add(Avx.Multiply(Choose(upperLeft, middleLeft, lowerLeft, above, below), rx),
            Avx.Multiply(Choose(upperRight, middleRight, lowerRight, above, below), fx));
        // At an exact +1 coordinate the following row/column has zero weight; reuse
        // the boundary neighbour instead of loading outside the 3x3 neighborhood.
        var bottom = Avx.Add(Avx.Multiply(Avx.BlendVariable(lowerLeft, middleLeft, above), rx),
            Avx.Multiply(Avx.BlendVariable(lowerRight, middleRight, above), fx));

        return Avx.Add(Avx.Multiply(top, ry), Avx.Multiply(bottom, fy));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> Choose(Vector256<double> low, Vector256<double> middle,
        Vector256<double> high, Vector256<double> lower, Vector256<double> higher) =>
        Avx.BlendVariable(Avx.BlendVariable(middle, low, lower), high, higher);
}
