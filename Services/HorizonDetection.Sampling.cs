using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using ImageMagick;

namespace HappyPhoton.Services;

public static partial class HorizonDetection
{
    private const int EncodingSteps = ushort.MaxValue;

    private static readonly double EncodingJoinEnd = Math.Ceiling(.0031308 * EncodingSteps) / EncodingSteps;

    // Q16-spaced samples keep interpolation error below 1e-7 encoded luminance.
    // Evaluate the cell spanning the sRGB join analytically: its slopes differ.
    private static readonly double[] LuminanceEncoding = Enumerable.Range(0, EncodingSteps + 1)
        .Select(i => ToneLut.SrgbEncode(i / (double)EncodingSteps)).ToArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double EncodeLuminance(double value)
    {
        if (value <= EncodingJoinEnd || value >= 1) return ToneLut.SrgbEncode(value);

        var position = value * EncodingSteps;
        var index = (int)position;
        var fraction = position - index;

        return LuminanceEncoding[index] * (1 - fraction) + LuminanceEncoding[index + 1] * fraction;
    }

    private readonly record struct BoxWeights(int Start, double[] Weights);

    private static BoxWeights[] BoxAxis(int count, double scale, int sourceCount)
    {
        var boxes = new BoxWeights[count];

        for (var i = 0; i < count; i++)
        {
            var first = i * scale;
            var last = (i + 1) * scale;
            var start = (int)first;
            // A rounded ratio can put the final box a fraction beyond the source.
            var weights = new double[Math.Min(sourceCount, (int)Math.Ceiling(last)) - start];

            for (var j = 0; j < weights.Length; j++)
            {
                weights[j] = Math.Min(start + j + 1, last) - Math.Max(start + j, first);
            }

            boxes[i] = new(start, weights);
        }

        return boxes;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe double[] ReadLuminance(MagickImage image, int width, int height)
    {
        var plane = new double[width * height];
        using var pixels = image.GetPixelsUnsafe();
        var layout = RenderKernelSupport.GetLayout(pixels);
        var sx = image.Width / (double)width;
        var sy = image.Height / (double)height;
        var columns = BoxAxis(width, sx, (int)image.Width);
        var rows = BoxAxis(height, sy, (int)image.Height);
        var starts = columns.Select(c => c.Start).ToArray();
        var weights = new double[columns.Max(c => c.Weights.Length)][];

        for (var tap = 0; tap < weights.Length; tap++)
        {
            weights[tap] = columns.Select(c => tap < c.Weights.Length ? c.Weights[tap] : 0).ToArray();
        }

        var sourceWidth = (int)image.Width;
        var luminance = new double[sourceWidth];
        var cachedRow = -1;
        var denominator = sx * sy * ushort.MaxValue;

        for (var y = 0; y < height; y++)
        {
            var box = rows[y];
            var strip = (ushort*)pixels.GetAreaPointer(0, box.Start, image.Width, (uint)box.Weights.Length);
            if (strip == null) throw new InvalidOperationException("Unable to read base pixels.");

            // Retain row/column accumulation order and the combined weight. Factoring the
            // vertical weight out of a horizontal sum changes rounding at weak edges.
            for (var row = 0; row < box.Weights.Length; row++)
            {
                var source = strip + row * sourceWidth * layout.Channels;
                var wy = box.Weights[row];

                if (cachedRow != box.Start + row)
                {
                    for (var col = 0; col < sourceWidth; col++)
                    {
                        var p = source + col * layout.Channels;
                        luminance[col] = Rec2020Luminance.Red * p[layout.Red] +
                            Rec2020Luminance.Green * p[layout.Green] + Rec2020Luminance.Blue * p[layout.Blue];
                    }

                    cachedRow = box.Start + row;
                }

                AccumulateBoxRow(luminance, plane, y * width, columns, starts, weights, wy);
            }

            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                plane[i] = EncodeLuminance(plane[i] / denominator);
            }
        }

        return plane;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void AccumulateBoxRow(double[] luminance, double[] plane, int offset,
        BoxWeights[] columns, int[] starts, double[][] weights, double wy)
    {
        var x = 0;

        if (Avx2.IsSupported)
        {
            fixed (double* source = luminance, target = plane)
            fixed (int* first = starts)
            {
                for (; x <= columns.Length - 4; x += 4)
                {
                    var sum = Avx.LoadVector256(target + offset + x);
                    var indices = Sse2.LoadVector128(first + x);

                    for (var tap = 0; tap < weights.Length; tap++)
                    {
                        // Zero-padded taps read a valid source location; adding their zero
                        // contribution leaves each positive luminance sum unchanged.
                        var positions = Sse41.Min(Sse2.Add(indices, Vector128.Create(tap)),
                            Vector128.Create(luminance.Length - 1));
                        var weight = Vector256.LoadUnsafe(ref weights[tap][x]);
                        weight = Avx.Multiply(Vector256.Create(wy), weight);
                        sum = Avx.Add(sum, Avx.Multiply(weight, Avx2.GatherVector256(source, positions, 8)));
                    }

                    Avx.Store(target + offset + x, sum);
                }
            }
        }

        for (; x < columns.Length; x++)
        {
            var column = columns[x];
            var sum = plane[offset + x];

            for (var col = 0; col < column.Weights.Length; col++)
            {
                var weight = wy * column.Weights[col];
                sum += weight * luminance[column.Start + col];
            }

            plane[offset + x] = sum;
        }
    }

    private static readonly double[] GaussianKernel = CreateGaussianKernel();

    private static readonly double[] DerivativeKernel = GaussianKernel.Select((weight, index) =>
        (index - DerivativeRadius) * weight / (DerivativeSigma * DerivativeSigma)).ToArray();

    private static double[] CreateGaussianKernel()
    {
        var kernel = new double[2 * DerivativeRadius + 1];

        for (var k = -DerivativeRadius; k <= DerivativeRadius; k++)
        {
            kernel[k + DerivativeRadius] = Math.Exp(-.5 * k * k / (DerivativeSigma * DerivativeSigma));
        }

        var normalization = kernel.Sum();

        for (var k = 0; k < kernel.Length; k++)
        {
            kernel[k] /= normalization;
        }

        return kernel;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static (double[] X, double[] Y) Gradients(double[] plane, int width, int height)
    {
        // Precompute the matched Gaussian/derivative coefficients and pair symmetric
        // taps. This changes rounding, not the derivative's scale. SIMD lanes follow
        // the paired scalar arithmetic without fused multiply-add.
        var kernel = GaussianKernel;
        var smoothX = new double[plane.Length];
        var derivativeX = new double[plane.Length];
        var gy = new double[plane.Length];
        var lanes = Vector<double>.Count;

        for (var y = DerivativeRadius; y < height - DerivativeRadius; y++)
        {
            var x = DerivativeRadius;

            for (; x <= width - DerivativeRadius - lanes; x += lanes)
            {
                var i = y * width + x;
                var smooth = new Vector<double>(plane, i) * new Vector<double>(kernel[DerivativeRadius]);
                var derivative = Vector<double>.Zero;

                for (var k = 1; k <= DerivativeRadius; k++)
                {
                    var left = new Vector<double>(plane, i - k);
                    var right = new Vector<double>(plane, i + k);
                    smooth += (left + right) * new Vector<double>(kernel[DerivativeRadius + k]);
                    derivative += (right - left) * new Vector<double>(DerivativeKernel[DerivativeRadius + k]);
                }

                smooth.CopyTo(smoothX, i);
                derivative.CopyTo(derivativeX, i);
            }

            for (; x < width - DerivativeRadius; x++)
            {
                var i = y * width + x;
                var smooth = plane[i] * kernel[DerivativeRadius];
                var derivative = 0d;

                for (var k = 1; k <= DerivativeRadius; k++)
                {
                    smooth += (plane[i - k] + plane[i + k]) * kernel[DerivativeRadius + k];
                    derivative += (plane[i + k] - plane[i - k]) * DerivativeKernel[DerivativeRadius + k];
                }

                smoothX[i] = smooth;
                derivativeX[i] = derivative;
            }
        }

        for (var y = Border; y < height - Border; y++)
        {
            var x = Border;

            for (; x <= width - Border - lanes; x += lanes)
            {
                var i = y * width + x;
                var dx = new Vector<double>(derivativeX, i) * new Vector<double>(kernel[DerivativeRadius]);
                var dy = Vector<double>.Zero;

                for (var k = 1; k <= DerivativeRadius; k++)
                {
                    var offset = k * width;
                    dx += (new Vector<double>(derivativeX, i - offset) + new Vector<double>(derivativeX, i + offset)) *
                        new Vector<double>(kernel[DerivativeRadius + k]);
                    dy += (new Vector<double>(smoothX, i + offset) - new Vector<double>(smoothX, i - offset)) *
                        new Vector<double>(DerivativeKernel[DerivativeRadius + k]);
                }

                dx.CopyTo(plane, i);
                dy.CopyTo(gy, i);
            }

            for (; x < width - Border; x++)
            {
                var i = y * width + x;
                var dx = derivativeX[i] * kernel[DerivativeRadius];
                var dy = 0d;

                for (var k = 1; k <= DerivativeRadius; k++)
                {
                    var offset = k * width;
                    dx += (derivativeX[i - offset] + derivativeX[i + offset]) * kernel[DerivativeRadius + k];
                    dy += (smoothX[i + offset] - smoothX[i - offset]) * DerivativeKernel[DerivativeRadius + k];
                }

                plane[i] = dx;
                gy[i] = dy;
            }
        }

        return (plane, gy);
    }
}
