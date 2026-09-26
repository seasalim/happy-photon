using System.Text.Json;
using Xunit;

namespace HappyPhoton.Tests;

// Source-derived diagnostic, not a replacement registration oracle. See
// docs/pipeline/HEAL_REGISTRATION.md for the pinned LibRaw source and limitations.
public sealed class HealDecodeFramingTests
{
    [Fact]
    public void BayerHalfSizeGreenCentroidIsTheBlockCentreInEitherRowOrder()
    {
        // Bayer keeps the two greens in separate channels before mix_green.
        var pattern = new[] { 0, 1, 3, 2 };
        foreach (var reverse in new[] { false, true })
        {
            var samples = Pack(pattern, 2, reverse);
            Assert.Equal(0, (samples[0, 1]!.Value.X + samples[0, 3]!.Value.X) / 2);
            Assert.Equal(0, (samples[0, 1]!.Value.Y + samples[0, 3]!.Value.Y) / 2);
        }
    }

    [Fact]
    public void X30HalfSizeGreenCentroidDependsOnRowWriteOrder()
    {
        var forward = Pack(X30Pattern(), 6, reverseRows: false);
        var reverse = Pack(X30Pattern(), 6, reverseRows: true);
        var forwardGreen = Enumerable.Range(0, 9).Select(i => forward[i, 1]!.Value).ToArray();
        var reverseGreen = Enumerable.Range(0, 9).Select(i => reverse[i, 1]!.Value).ToArray();

        Assert.Equal(1d / 6, forwardGreen.Average(p => p.X), 14);
        Assert.Equal(1d / 6, reverseGreen.Average(p => p.X), 14);
        Assert.Equal(7d / 18, forwardGreen.Average(p => p.Y), 14);
        Assert.Equal(-7d / 18, reverseGreen.Average(p => p.Y), 14);
        Assert.Equal(7, forwardGreen.Zip(reverseGreen).Count(p => p.First.Y != p.Second.Y));
        Assert.NotEqual(.25, forwardGreen.Average(p => p.X));
    }

    [Fact]
    public void X30PackingAlsoLeavesAChromaHoleForPreInterpolation()
    {
        var samples = Pack(X30Pattern(), 6, reverseRows: false);
        var missing = Enumerable.Range(0, 9)
            .Where(i => samples[i, 0] == null && samples[i, 2] == null).ToArray();
        Assert.Equal(new[] { 4 }, missing);
        Assert.All(Enumerable.Range(0, 9), i => Assert.NotNull(samples[i, 1]));
    }

    private static int[] X30Pattern()
    {
        var path = Path.Combine(GoldenTestPaths.RepositoryRoot,
            "native", "libraw", "oracle", "facts", "fujifilm-x30.raf.json");
        using var facts = JsonDocument.Parse(File.ReadAllText(path));
        var sensor = facts.RootElement.GetProperty("sensor");
        Assert.Equal(9, sensor.GetProperty("filters").GetInt32());
        var pattern = sensor.GetProperty("xtrans").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        Assert.Equal(36, pattern.Length);
        return pattern;
    }

    private static (double X, double Y)?[,] Pack(int[] pattern, int size, bool reverseRows)
    {
        var half = size / 2;
        var samples = new (double X, double Y)?[half * half, 4];
        // Track sample positions, not brightness. The assignment destination follows
        // raw2image.cpp's copy_bayer: (row >> shrink, col >> shrink, fcol(row,col)).
        // Swapping row completion order models two possible OpenMP schedules; columns
        // remain increasing within each row. No decoder or original image is touched.
        for (var step = 0; step < size; step++)
        {
            var row = reverseRows ? step ^ 1 : step;
            for (var col = 0; col < size; col++)
                samples[(row / 2) * half + col / 2, pattern[row * size + col]] =
                    (col % 2 - .5, row % 2 - .5);
        }
        return samples;
    }
}
