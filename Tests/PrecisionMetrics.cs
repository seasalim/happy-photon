using ImageMagick;

namespace HappyPhoton.Tests;

internal static class PrecisionMetrics
{
    private const double MinimumUseful = 8 / (double)byte.MaxValue;
    private const double MaximumUseful = 247 / (double)byte.MaxValue;
    private const double Q16PerDisplayCode = ushort.MaxValue / (double)byte.MaxValue;

    private static readonly int[,] Bayer8 =
    {
        { 0, 32, 8, 40, 2, 34, 10, 42 },
        { 48, 16, 56, 24, 50, 18, 58, 26 },
        { 12, 44, 4, 36, 14, 46, 6, 38 },
        { 60, 28, 52, 20, 62, 30, 54, 22 },
        { 3, 35, 11, 43, 1, 33, 9, 41 },
        { 51, 19, 59, 27, 49, 17, 57, 25 },
        { 15, 47, 7, 39, 13, 45, 5, 37 },
        { 63, 31, 55, 23, 61, 29, 53, 21 }
    };

    public static PrecisionCaseMetrics Analyze(
        string fixtureName,
        string vectorName,
        int width,
        int height,
        PrecisionCapture capture,
        string ditherPngPath)
    {
        var preliminary = capture.Checkpoints
            .Select(checkpoint => AnalyzeCheckpoint(
                checkpoint,
                width,
                height,
                q16Passed: true))
            .ToArray();
        var q16Passed = !preliminary.Single(item =>
            item.Checkpoint.Number == 6).PreOutputBanding;
        var checkpoints = capture.Checkpoints
            .Select(checkpoint => AnalyzeCheckpoint(
                checkpoint,
                width,
                height,
                q16Passed))
            .ToArray();

        var reference = capture.Checkpoints.Single(item => item.Number == 4).Actual;
        using var dithered = (MagickImage)capture.Rendered.Clone();
        ApplyOrderedDither(dithered);
        var ditherOutput = PrecisionOracle.Quantize(dithered, ditherPngPath);
        var ditherCheckpoint = new PrecisionCheckpoint(
            9,
            "ordered-dither-probe",
            ditherOutput.Preview.Red,
            reference,
            reference,
            IsByteOutput: true);
        var ditherMetrics = AnalyzeCheckpoint(
            ditherCheckpoint,
            width,
            height,
            q16Passed);
        var nativeBlock = checkpoints.Single(item =>
            item.Checkpoint.Number == 7).BlockMeanP99;
        var reduction = nativeBlock > 0
            ? (nativeBlock - ditherMetrics.BlockMeanP99) / nativeBlock
            : 0;
        var pointP99 = ditherMetrics.Rows.Max(row => row.P99AbsoluteError);
        var previewExportMatch = ditherOutput.Parity.Match;
        var viable = reduction >= 0.5 &&
            pointP99 <= 1.0 &&
            previewExportMatch;
        var dither = new PrecisionDitherMetrics(
            ditherMetrics,
            nativeBlock,
            reduction,
            pointP99,
            ditherOutput.Parity,
            viable);
        return new PrecisionCaseMetrics(
            fixtureName,
            vectorName,
            checkpoints,
            dither);
    }

    public static string SelectOutcome(IReadOnlyList<PrecisionCaseMetrics> cases)
    {
        var baseFailure = cases.Any(item => item.Get(2).PreOutputBanding);
        var q16InputFailure = cases.Any(item => item.Get(4).PreOutputBanding);
        var laterFailure = cases.Any(item =>
            item.Get(5).PreOutputBanding || item.Get(6).PreOutputBanding);
        var outputFailures = cases
            .Where(item => item.Get(7).FinalOutputBanding ||
                item.Get(8).FinalOutputBanding)
            .ToArray();

        if ((baseFailure || q16InputFailure) && laterFailure)
        {
            return "P0-X";
        }
        if (baseFailure || q16InputFailure)
        {
            return "P0-D";
        }
        if (laterFailure)
        {
            return "P0-C";
        }
        if (outputFailures.Length > 0)
        {
            return outputFailures.All(item => item.Dither.Viable)
                ? "P0-B"
                : "P0-X";
        }
        return "P0-A";
    }

    private static PrecisionCheckpointMetrics AnalyzeCheckpoint(
        PrecisionCheckpoint checkpoint,
        int width,
        int height,
        bool q16Passed)
    {
        if (checkpoint.Actual.Length != checked(width * height) ||
            checkpoint.Reference.Length != checkpoint.Actual.Length ||
            checkpoint.UsefulReference.Length != checkpoint.Actual.Length)
        {
            throw new InvalidOperationException(
                $"Checkpoint {checkpoint.Number} has invalid sample dimensions.");
        }

        var rows = new PrecisionMetricRow[height];
        for (var row = 0; row < height; row++)
        {
            rows[row] = AnalyzeRow(
                checkpoint.Actual,
                checkpoint.Reference,
                checkpoint.UsefulReference,
                width,
                row);
        }
        var preOutputBanding = rows.All(row => row.PreOutputBanding);
        var blockMeanP99 = checkpoint.IsByteOutput
            ? CalculateBlockMeanP99(
                checkpoint.Actual,
                checkpoint.Reference,
                checkpoint.UsefulReference,
                width,
                height)
            : 0;
        var finalBanding = checkpoint.IsByteOutput &&
            q16Passed &&
            rows.All(row => row.LongestIdenticalRun >= 8) &&
            blockMeanP99 >= 0.25;
        return new PrecisionCheckpointMetrics(
            checkpoint,
            rows,
            blockMeanP99,
            preOutputBanding,
            finalBanding);
    }

    private static PrecisionMetricRow AnalyzeRow(
        double[] actual,
        double[] reference,
        double[] usefulReference,
        int width,
        int row)
    {
        var offset = row * width;
        var useful = new bool[width];
        var actualUnique = new HashSet<double>();
        var referenceUnique = new HashSet<double>();
        var errors = new List<double>(width);
        var actualCodes = new HashSet<int>();
        var usefulCount = 0;
        var signedSum = 0d;
        var signedMinimum = double.PositiveInfinity;
        var signedMaximum = double.NegativeInfinity;
        for (var x = 0; x < width; x++)
        {
            var index = offset + x;
            useful[x] = IsUseful(usefulReference, index, x, width);
            if (!useful[x])
            {
                continue;
            }

            var actualCode = actual[index] * byte.MaxValue;
            var referenceCode = reference[index] * byte.MaxValue;
            var signed = actualCode - referenceCode;
            usefulCount++;
            actualUnique.Add(actual[index]);
            referenceUnique.Add(reference[index]);
            actualCodes.Add(ToIntegerCode(actualCode));
            errors.Add(Math.Abs(signed));
            signedSum += signed;
            signedMinimum = Math.Min(signedMinimum, signed);
            signedMaximum = Math.Max(signedMaximum, signed);
        }

        if (usefulCount == 0)
        {
            throw new InvalidOperationException(
                $"The useful region is empty for replicated row {row}.");
        }

        var longestRun = 1;
        var currentRun = 0;
        var runExitFailure = false;
        var maximumStep = 0d;
        var maximumStepExcess = 0d;
        for (var x = 0; x < width; x++)
        {
            if (!useful[x])
            {
                currentRun = 0;
                continue;
            }

            var index = offset + x;
            if (x > 0 && useful[x - 1])
            {
                var actualStep = actual[index] * byte.MaxValue -
                    actual[index - 1] * byte.MaxValue;
                var referenceStep = reference[index] * byte.MaxValue -
                    reference[index - 1] * byte.MaxValue;
                maximumStep = Math.Max(maximumStep, actualStep);
                maximumStepExcess = Math.Max(
                    maximumStepExcess,
                    actualStep - referenceStep);
                if (actual[index] == actual[index - 1])
                {
                    currentRun++;
                }
                else
                {
                    if (currentRun >= 8 &&
                        actualStep - referenceStep >= 0.5)
                    {
                        runExitFailure = true;
                    }
                    currentRun = 1;
                }
            }
            else
            {
                currentRun = 1;
            }
            longestRun = Math.Max(longestRun, currentRun);
        }

        var minimumReference = Enumerable.Range(0, width)
            .Where(x => useful[x])
            .Min(x => reference[offset + x] * byte.MaxValue);
        var maximumReference = Enumerable.Range(0, width)
            .Where(x => useful[x])
            .Max(x => reference[offset + x] * byte.MaxValue);
        var missing = LongestMissingRun(
            actualCodes,
            (int)Math.Ceiling(minimumReference),
            (int)Math.Floor(maximumReference));
        var p99 = Percentile99(errors);
        var coverage = actualUnique.Count / (double)referenceUnique.Count;
        var banding = runExitFailure ||
            missing >= 2 ||
            coverage < 0.95 && p99 >= 0.5;
        return new PrecisionMetricRow(
            row,
            usefulCount,
            actualUnique.Count,
            referenceUnique.Count,
            coverage,
            longestRun,
            maximumStep,
            maximumStepExcess,
            p99,
            missing,
            signedSum / usefulCount,
            signedMinimum,
            signedMaximum,
            banding);
    }

    private static double CalculateBlockMeanP99(
        double[] actual,
        double[] reference,
        double[] usefulReference,
        int width,
        int height)
    {
        var errors = new List<double>(width * height / 64);
        for (var y = 0; y < height; y += 8)
        {
            for (var x = 0; x < width; x += 8)
            {
                var actualSum = 0d;
                var referenceSum = 0d;
                var eligible = true;
                for (var dy = 0; dy < 8 && eligible; dy++)
                {
                    for (var dx = 0; dx < 8; dx++)
                    {
                        var index = (y + dy) * width + x + dx;
                        if (!IsUseful(usefulReference, index, x + dx, width))
                        {
                            eligible = false;
                            break;
                        }
                        actualSum += actual[index] * byte.MaxValue;
                        referenceSum += reference[index] * byte.MaxValue;
                    }
                }
                if (eligible)
                {
                    errors.Add(Math.Abs(actualSum - referenceSum) / 64);
                }
            }
        }

        if (errors.Count == 0)
        {
            throw new InvalidOperationException(
                "No aligned 8x8 tile lies entirely inside the useful region.");
        }
        return Percentile99(errors);
    }

    private static bool IsUseful(
        double[] reference,
        int index,
        int x,
        int width)
    {
        var value = reference[index];
        if (value < MinimumUseful || value > MaximumUseful)
        {
            return false;
        }
        return x + 1 < width
            ? reference[index + 1] > value
            : x > 0 && value > reference[index - 1];
    }

    private static int LongestMissingRun(
        HashSet<int> actualCodes,
        int minimum,
        int maximum)
    {
        var longest = 0;
        var current = 0;
        for (var code = minimum; code <= maximum; code++)
        {
            if (!actualCodes.Contains(code))
            {
                current++;
                longest = Math.Max(longest, current);
            }
            else
            {
                current = 0;
            }
        }
        return longest;
    }

    private static int ToIntegerCode(double value) =>
        Math.Clamp(
            (int)Math.Round(value, MidpointRounding.AwayFromZero),
            byte.MinValue,
            byte.MaxValue);

    private static double Percentile99(List<double> values)
    {
        values.Sort();
        var index = Math.Max(0, (int)Math.Ceiling(values.Count * 0.99) - 1);
        return values[index];
    }

    private static void ApplyOrderedDither(MagickImage image)
    {
        using var pixels = image.GetPixels();
        var samples = pixels.GetArea(0, 0, image.Width, image.Height) ??
            throw new InvalidOperationException("Unable to read dither-probe pixels.");
        var channels = pixels.Channels;
        var red = ChannelIndex(pixels, PixelChannel.Red);
        var green = ChannelIndex(pixels, PixelChannel.Green);
        var blue = ChannelIndex(pixels, PixelChannel.Blue);
        var width = checked((int)image.Width);
        var height = checked((int)image.Height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var rank = Bayer8[y & 7, x & 7];
                var dither = ((rank + 0.5) / 64 - 0.5) *
                    Q16PerDisplayCode;
                var offset = (y * width + x) * channels;
                samples[offset + red] = Dither(samples[offset + red], dither);
                samples[offset + green] = Dither(samples[offset + green], dither);
                samples[offset + blue] = Dither(samples[offset + blue], dither);
            }
        }
        pixels.SetArea(0, 0, image.Width, image.Height, samples);
    }

    private static int ChannelIndex(
        IPixelCollection<ushort> pixels,
        PixelChannel channel) =>
        checked((int)(pixels.GetChannelIndex(channel) ??
            throw new InvalidOperationException($"Missing {channel} channel.")));

    private static ushort Dither(ushort value, double offset) =>
        (ushort)Math.Round(
            Math.Clamp(value + offset, ushort.MinValue, ushort.MaxValue),
            MidpointRounding.AwayFromZero);

}
