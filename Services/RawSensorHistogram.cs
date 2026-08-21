using HappyPhoton.Models;
using HappyPhoton.LibRaw.Interop;

namespace HappyPhoton.Services;

public static class RawSensorHistogram
{
    internal const int MaxLookupBytes = 4 * 1024 * 1024;
    private const int SensorValueCount = ushort.MaxValue + 1;

    public static HistogramData? Sample(
        RawSensorFrame frame,
        CancellationToken cancellationToken = default) =>
        Sample((IRawSensorFrame)frame, cancellationToken);

    internal static HistogramData? Sample(
        LibRawContext context,
        CancellationToken cancellationToken)
    {
        using var frame = RawSensorFrame.TryCreate(context, cancellationToken);
        return frame == null ? null : Sample(frame, cancellationToken);
    }

    internal static HistogramData? Sample(
        IRawSensorFrame frame,
        CancellationToken cancellationToken = default,
        int? workerLimit = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!TryGetCfaPeriod(frame, out var cfaRows, out var cfaColumns))
        {
            return null;
        }

        var phaseRows = Lcm(cfaRows, checked((int)Math.Max(1, frame.RepeatingRows)));
        var phaseColumns = Lcm(cfaColumns, checked((int)Math.Max(1, frame.RepeatingColumns)));
        var phaseCount = checked(phaseRows * phaseColumns);
        var channels = new byte[phaseCount];
        var blacks = new uint[phaseCount];
        BuildPhaseTables(frame, phaseRows, phaseColumns, channels, blacks);

        var fusedBytes = (long)phaseCount * SensorValueCount * sizeof(ushort);
        var white = frame.Maximum;
        ushort[]? fused = fusedBytes <= MaxLookupBytes
            ? BuildFusedLookup(white, channels, blacks)
            : null;
        var encoded = fused == null ? BuildEncodeLookup() : null;

        var histogram = new HistogramData { Domain = HistogramDomain.RawSensor };
        long redClipped = 0;
        long greenClipped = 0;
        long blueClipped = 0;
        var red = histogram.Red;
        var green = histogram.Green;
        var blue = histogram.Blue;
        var pitch = checked((int)(frame.RawPitch / sizeof(ushort)));
        var top = checked((int)frame.TopMargin);
        var left = checked((int)frame.LeftMargin);
        var width = checked((int)frame.VisibleWidth);
        var height = checked((int)frame.VisibleHeight);

        // Chunked rows with per-worker bins merged under a lock: integer
        // merges are order-independent, so bins are bit-identical for any
        // worker count.
        var workers = workerLimit ?? WorkerCount(checked(width * height));
        var sync = new object();
        Parallel.For(
            0,
            workers,
            new ParallelOptions { CancellationToken = cancellationToken },
            worker =>
            {
                var (startRow, endRow) = ChunkRange(height, worker, workers);
                var workerRed = new int[red.Length];
                var workerGreen = new int[green.Length];
                var workerBlue = new int[blue.Length];
                long workerRedClipped = 0;
                long workerGreenClipped = 0;
                long workerBlueClipped = 0;
                var samples = frame.Samples;
                for (var row = startRow; row < endRow; row++)
                {
                    if ((row & 255) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    var rowStart = checked((top + row) * pitch + left);
                    var phaseRow = row % phaseRows;
                    var phaseColumn = 0;
                    for (var column = 0; column < width; column++)
                    {
                        var value = samples[rowStart + column];
                        var phase = phaseRow * phaseColumns + phaseColumn;
                        if (++phaseColumn == phaseColumns) phaseColumn = 0;
                        int channel;
                        int bin;
                        if (fused != null)
                        {
                            var packed = fused[phase * SensorValueCount + value];
                            channel = packed >> 8;
                            bin = packed & 255;
                        }
                        else
                        {
                            channel = channels[phase];
                            bin = Encode(value, blacks[phase], white, encoded!);
                        }

                        switch (channel)
                        {
                            case 0:
                                workerRed[bin]++;
                                if (value >= white) workerRedClipped++;
                                break;
                            case 1:
                                workerGreen[bin]++;
                                if (value >= white) workerGreenClipped++;
                                break;
                            case 2:
                                workerBlue[bin]++;
                                if (value >= white) workerBlueClipped++;
                                break;
                            default:
                                throw new InvalidDataException(
                                    "The CFA channel is invalid.");
                        }
                    }
                }

                lock (sync)
                {
                    for (var bin = 0; bin < workerRed.Length; bin++)
                    {
                        red[bin] += workerRed[bin];
                        green[bin] += workerGreen[bin];
                        blue[bin] += workerBlue[bin];
                    }
                    redClipped += workerRedClipped;
                    greenClipped += workerGreenClipped;
                    blueClipped += workerBlueClipped;
                }
            });

        cancellationToken.ThrowIfCancellationRequested();
        histogram.Clipping = new RawClipping(
            redClipped, greenClipped, blueClipped,
            (long)width * height, white);
        histogram.Normalize();
        return histogram;
    }

    private static int WorkerCount(int pixelCount) =>
        Math.Min(Environment.ProcessorCount, Math.Max(1, pixelCount / 8192));

    private static (int Start, int End) ChunkRange(
        int rows,
        int worker,
        int workers) =>
        ((int)((long)rows * worker / workers),
            (int)((long)rows * (worker + 1) / workers));

    internal static bool UsesFusedLookup(
        int cfaRows,
        int cfaColumns,
        int repeatingRows,
        int repeatingColumns)
    {
        var rows = Lcm(cfaRows, Math.Max(1, repeatingRows));
        var columns = Lcm(cfaColumns, Math.Max(1, repeatingColumns));
        return (long)rows * columns * SensorValueCount * sizeof(ushort) <=
            MaxLookupBytes;
    }

    private static void BuildPhaseTables(
        IRawSensorFrame frame,
        int rows,
        int columns,
        byte[] channels,
        uint[] blacks)
    {
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var phase = row * columns + column;
                var nativeChannel = GetChannel(frame, row, column);
                if (nativeChannel is < 0 or > 3)
                    throw new InvalidDataException("The CFA channel is invalid.");
                channels[phase] = (byte)(nativeChannel == 3 ? 1 : nativeChannel);

                var block = 0u;
                if (frame.RepeatingRows > 0 && frame.RepeatingColumns > 0)
                {
                    var blockIndex = checked(6 +
                        (row % (int)frame.RepeatingRows) * (int)frame.RepeatingColumns +
                        column % (int)frame.RepeatingColumns);
                    block = frame.CBlack[blockIndex];
                }
                blacks[phase] = SaturatingAdd(
                    frame.Black,
                    frame.CBlack[nativeChannel],
                    block);
            }
        }
    }

    private static ushort[] BuildFusedLookup(
        uint white,
        IReadOnlyList<byte> channels,
        IReadOnlyList<uint> blacks)
    {
        var table = new ushort[checked(channels.Count * SensorValueCount)];
        for (var phase = 0; phase < channels.Count; phase++)
        {
            var offset = phase * SensorValueCount;
            for (var value = 0; value < SensorValueCount; value++)
            {
                table[offset + value] = (ushort)(
                    (channels[phase] << 8) |
                    EncodeExact((uint)value, blacks[phase], white));
            }
        }
        return table;
    }

    private static byte[] BuildEncodeLookup()
    {
        var table = new byte[SensorValueCount];
        for (var value = 0; value < table.Length; value++)
        {
            table[value] = EncodeUnit(value / (double)ushort.MaxValue);
        }
        return table;
    }

    private static int Encode(
        uint value,
        uint black,
        uint white,
        IReadOnlyList<byte> encoded)
    {
        if (value <= black) return 0;
        var denominator = Math.Max(1L, (long)white - black);
        if (value >= black + denominator) return 255;
        var index = (int)Math.Round(
            (value - black) * (double)ushort.MaxValue / denominator,
            MidpointRounding.AwayFromZero);
        return encoded[index];
    }

    private static byte EncodeExact(uint value, uint black, uint white)
    {
        var denominator = Math.Max(1L, (long)white - black);
        var normalized = Math.Clamp(((long)value - black) / (double)denominator, 0, 1);
        return EncodeUnit(normalized);
    }

    private static byte EncodeUnit(double value)
    {
        var encoded = value <= 0.0031308
            ? 12.92 * value
            : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055;
        return (byte)Math.Clamp(
            Math.Round(encoded * 255, MidpointRounding.AwayFromZero),
            0,
            255);
    }

    private static int GetChannel(IRawSensorFrame frame, int row, int column)
    {
        if (frame.Filters == 9)
        {
            return frame.XTrans[(row % 6) * 6 + column % 6];
        }

        var shift = ((((row << 1) & 14) | (column & 1)) << 1);
        return (int)((frame.Filters >> shift) & 3);
    }

    private static bool TryGetCfaPeriod(
        IRawSensorFrame frame,
        out int rows,
        out int columns)
    {
        if (frame.Colors is not (3 or 4))
        {
            rows = columns = 0;
            return false;
        }
        if (frame.Filters == 9)
        {
            rows = columns = 6;
            return true;
        }
        if (frame.Filters > 1000)
        {
            rows = 8;
            columns = 2;
            return true;
        }
        rows = columns = 0;
        return false;
    }

    private static int Lcm(int left, int right) =>
        checked(left / GreatestCommonDivisor(left, right) * right);

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }
        return left;
    }

    private static uint SaturatingAdd(uint first, uint second, uint third)
    {
        var sum = (ulong)first + second + third;
        return sum > uint.MaxValue ? uint.MaxValue : (uint)sum;
    }
}
