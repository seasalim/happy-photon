using System.Buffers.Binary;
using System.Text;

namespace HappyPhoton.Tests;

internal sealed record SyntheticRawDngOptions
{
    internal int Scale { get; init; } = 1;
    internal bool IncludeOpcodes { get; init; } = true;
    internal bool UseInsetDefaultCrop { get; init; } = true;
    internal double WarpKr1 { get; init; } = -0.08;
    internal double VignetteK0 { get; init; } = 0.2;
    internal ushort Orientation { get; init; } = 1;
    internal (int X, int Y)? SaturatedRedSite { get; init; }

    internal int Width => 640 * Scale;
    internal int Height => 480 * Scale;
    internal uint[] ActiveArea => ScaleValues(16, 16, 464, 624);
    internal uint[] DefaultCropOrigin => ScaleValues(32, 24);
    internal uint[] DefaultCropSize => ScaleValues(544, 400);

    private uint[] ScaleValues(params uint[] values) =>
        values.Select(value => checked(value * (uint)Scale)).ToArray();
}

internal static class SyntheticRawDngFactory
{
    private const ushort Byte = 1;
    private const ushort Ascii = 2;
    private const ushort Short = 3;
    private const ushort Long = 4;
    private const ushort Rational = 5;
    private const ushort Undefined = 7;
    private const ushort SRational = 10;

    internal static string Write(string directory, SyntheticRawDngOptions options)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.dng");
        var pixels = BuildPixels(options);
        var entries = BuildEntries(options, pixels.Length);
        entries.Sort((left, right) => left.Tag.CompareTo(right.Tag));

        var ifdBytes = checked(2 + entries.Count * 12 + 4);
        var dataOffset = Align(8 + ifdBytes);
        foreach (var entry in entries.Where(entry => entry.Data.Length > 4))
        {
            entry.Offset = dataOffset;
            dataOffset = Align(dataOffset + entry.Data.Length);
        }
        var stripOffset = dataOffset;
        entries.Single(entry => entry.Tag == 273).Data = UInts((uint)stripOffset);
        var bytes = new byte[checked(stripOffset + pixels.Length)];
        bytes[0] = (byte)'I';
        bytes[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)entries.Count);
        var ifdOffset = 10;
        foreach (var entry in entries)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(ifdOffset), entry.Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(ifdOffset + 2), entry.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ifdOffset + 4), entry.Count);
            if (entry.Data.Length <= 4)
            {
                entry.Data.CopyTo(bytes, ifdOffset + 8);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    bytes.AsSpan(ifdOffset + 8), (uint)entry.Offset);
                entry.Data.CopyTo(bytes, entry.Offset);
            }
            ifdOffset += 12;
        }
        pixels.CopyTo(bytes, stripOffset);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static List<Entry> BuildEntries(
        SyntheticRawDngOptions options,
        int pixelBytes)
    {
        var cropOrigin = options.UseInsetDefaultCrop
            ? options.DefaultCropOrigin
            : new uint[] { 0, 0 };
        var cropSize = options.UseInsetDefaultCrop
            ? options.DefaultCropSize
            : new uint[]
            {
                options.ActiveArea[3] - options.ActiveArea[1],
                options.ActiveArea[2] - options.ActiveArea[0]
            };
        var result = new List<Entry>
        {
            E(254, Long, UInts(0)),
            E(256, Long, UInts((uint)options.Width)),
            E(257, Long, UInts((uint)options.Height)),
            E(258, Short, Shorts(16)),
            E(259, Short, Shorts(1)),
            E(262, Short, Shorts(32803)),
            E(271, Ascii, Text("Happy Photon")),
            E(272, Ascii, Text("Synthetic Bayer DNG")),
            E(273, Long, UInts(0)),
            E(274, Short, Shorts(options.Orientation)),
            E(277, Short, Shorts(1)),
            E(278, Long, UInts((uint)options.Height)),
            E(279, Long, UInts((uint)pixelBytes)),
            E(284, Short, Shorts(1)),
            E(33421, Short, Shorts(2, 2)),
            E(33422, Byte, [0, 1, 1, 2]),
            E(50706, Byte, [1, 4, 0, 0]),
            E(50707, Byte, [1, 1, 0, 0]),
            E(50708, Ascii, Text("Happy Photon Synthetic")),
            E(50710, Byte, [0, 1, 2]),
            E(50711, Short, Shorts(1)),
            E(50713, Short, Shorts(2, 2)),
            E(50714, Short, Shorts(0, 0, 0, 0)),
            E(50717, Long, UInts(4095)),
            E(50718, Rational, Rationals(1, 1)),
            E(50719, Long, UInts(cropOrigin)),
            E(50720, Long, UInts(cropSize)),
            E(50721, SRational, SRationals(1, 0, 0, 0, 1, 0, 0, 0, 1)),
            E(50728, Rational, Rationals(1, 1, 1)),
            E(50730, SRational, SRationals(0)),
            E(50778, Short, Shorts(21)),
            E(50829, Long, UInts(options.ActiveArea)),
            E(42036, Ascii, Text("Synthetic 35mm"))
        };
        if (options.IncludeOpcodes)
        {
            result.Add(E(51022, Undefined, OpcodeList(
                Opcode(1, WarpPayload(options.WarpKr1)),
                Opcode(3, VignettePayload(options.VignetteK0)))));
        }
        return result;
    }

    private static byte[] BuildPixels(SyntheticRawDngOptions options)
    {
        var values = new ushort[checked(options.Width * options.Height)];
        for (var y = 0; y < options.Height; y++)
        for (var x = 0; x < options.Width; x++)
        {
            var targetX = (x + 0.5) / options.Width;
            var targetY = (y + 0.5) / options.Height;
            var inverse = InvertWarp(
                targetX, targetY, options.WarpKr1, options.Width, options.Height);
            var radius = NormalizedRadiusSquared(
                inverse.X, inverse.Y, options.Width, options.Height);
            var gain = 1 + options.VignetteK0 * radius;
            var value = (512 + inverse.X * 2600) / gain;
            values[y * options.Width + x] =
                (ushort)Math.Clamp(Math.Round(value), 0, 4095);
        }
        if (options.SaturatedRedSite is { } saturated)
        {
            if ((saturated.X & 1) != 0 || (saturated.Y & 1) != 0)
                throw new ArgumentException("The saturated site must be on the RGGB red phase.");
            values[saturated.Y * options.Width + saturated.X] = 4095;
        }
        var bytes = new byte[values.Length * sizeof(ushort)];
        for (var index = 0; index < values.Length; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * 2), values[index]);
        return bytes;
    }

    private static (double X, double Y) InvertWarp(
        double x,
        double y,
        double kr1,
        int width,
        int height)
    {
        var estimateX = x;
        var estimateY = y;
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var (mappedX, mappedY) = Warp(
                estimateX, estimateY, kr1, width, height);
            estimateX += x - mappedX;
            estimateY += y - mappedY;
        }
        return (estimateX, estimateY);
    }

    private static (double X, double Y) Warp(
        double x,
        double y,
        double kr1,
        int width,
        int height)
    {
        var radius = NormalizedRadiusSquared(x, y, width, height);
        var factor = 1 + kr1 * radius;
        return (0.5 + (x - 0.5) * factor, 0.5 + (y - 0.5) * factor);
    }

    private static double NormalizedRadiusSquared(
        double x,
        double y,
        int width,
        int height)
    {
        var dx = (x - 0.5) * (width - 1);
        var dy = (y - 0.5) * (height - 1);
        var maximumSquared = 0.25 *
            ((width - 1d) * (width - 1) + (height - 1d) * (height - 1));
        return (dx * dx + dy * dy) / maximumSquared;
    }

    private static byte[] OpcodeList(params byte[][] opcodes)
    {
        var result = new byte[4 + opcodes.Sum(opcode => opcode.Length)];
        BinaryPrimitives.WriteUInt32BigEndian(result, (uint)opcodes.Length);
        var offset = 4;
        foreach (var opcode in opcodes)
        {
            opcode.CopyTo(result, offset);
            offset += opcode.Length;
        }
        return result;
    }

    private static byte[] Opcode(uint id, byte[] payload)
    {
        var result = new byte[16 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(result, id);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), 0x01030000);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(12), (uint)payload.Length);
        payload.CopyTo(result, 16);
        return result;
    }

    private static byte[] WarpPayload(double kr1)
    {
        var result = new byte[68];
        BinaryPrimitives.WriteUInt32BigEndian(result, 1);
        WriteDoubles(result, 4, 1, kr1, 0, 0, 0, 0, 0.5, 0.5);
        return result;
    }

    private static byte[] VignettePayload(double k0)
    {
        var result = new byte[56];
        WriteDoubles(result, 0, k0, 0, 0, 0, 0, 0.5, 0.5);
        return result;
    }

    private static void WriteDoubles(byte[] target, int offset, params double[] values)
    {
        foreach (var value in values)
        {
            BinaryPrimitives.WriteInt64BigEndian(
                target.AsSpan(offset), BitConverter.DoubleToInt64Bits(value));
            offset += 8;
        }
    }

    private static Entry E(ushort tag, ushort type, byte[] data) =>
        new(tag, type, checked((uint)(data.Length / TypeSize(type))), data);

    private static int TypeSize(ushort type) => type switch
    {
        Byte or Ascii or Undefined => 1,
        Short => 2,
        Long => 4,
        Rational or SRational => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');

    private static byte[] Shorts(params ushort[] values)
    {
        var result = new byte[values.Length * 2];
        for (var index = 0; index < values.Length; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(index * 2), values[index]);
        return result;
    }

    private static byte[] UInts(params uint[] values)
    {
        var result = new byte[values.Length * 4];
        for (var index = 0; index < values.Length; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * 4), values[index]);
        return result;
    }

    private static byte[] Rationals(params uint[] numerators) =>
        Fractions(numerators.Select(value => (Numerator: (int)value, Denominator: 1)));

    private static byte[] SRationals(params int[] numerators) =>
        Fractions(numerators.Select(value => (Numerator: value, Denominator: 1)));

    private static byte[] Fractions(IEnumerable<(int Numerator, int Denominator)> values)
    {
        var fractions = values.ToArray();
        var result = new byte[fractions.Length * 8];
        for (var index = 0; index < fractions.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(index * 8), fractions[index].Numerator);
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(index * 8 + 4), fractions[index].Denominator);
        }
        return result;
    }

    private static int Align(int value) => (value + 3) & ~3;

    private sealed class Entry(
        ushort tag,
        ushort type,
        uint count,
        byte[] data)
    {
        internal ushort Tag { get; } = tag;
        internal ushort Type { get; } = type;
        internal uint Count { get; } = count;
        internal byte[] Data { get; set; } = data;
        internal int Offset { get; set; }
    }
}
