using System.Buffers.Binary;
using System.Text;

namespace HappyPhoton.Tests;

internal sealed record SyntheticDcpOptions
{
    internal bool IncludeColorMatrix1 { get; init; } = true;
    internal double[] ColorMatrix1 { get; init; } = Identity;
    internal double[]? ColorMatrix2 { get; init; }
    internal double[]? ForwardMatrix1 { get; init; }
    internal double[]? ForwardMatrix2 { get; init; }
    internal int? Illuminant1 { get; init; } = 21;
    internal int? Illuminant2 { get; init; }
    internal string? Name { get; init; }
    internal string? UniqueCameraModel { get; init; }
    internal string? CalibrationSignature { get; init; }
    internal uint? EmbedPolicy { get; init; }
    internal uint[]? HueSatDimensions { get; init; }
    internal float[]? HueSatTable1 { get; init; }
    internal float[]? HueSatTable2 { get; init; }
    internal uint? HueSatEncoding { get; init; }
    internal double[]? AnalogBalance { get; init; }
    internal double[]? CameraCalibration1 { get; init; }
    internal double[]? CameraCalibration2 { get; init; }
    internal double[]? ReductionMatrix1 { get; init; }
    internal double[]? ReductionMatrix2 { get; init; }
    internal double[]? AsShotNeutral { get; init; }
    internal string? CameraCalibrationSignature { get; init; }
    internal bool CyclicIfd { get; init; }
    internal ushort? ExtraLongTag { get; init; }

    internal static double[] Identity { get; } =
        [1, 0, 0, 0, 1, 0, 0, 0, 1];
}

internal static class SyntheticDcpFactory
{
    // Adobe DNG 1.7.1, tables 11-20. Keeping these independently named in
    // the fixture writer makes accidental production-tag drift visible.
    private const ushort ExtraCameraProfiles = 50933;
    private const ushort ColorMatrix1 = 50721;
    private const ushort ColorMatrix2 = 50722;
    private const ushort CameraCalibration1 = 50723;
    private const ushort CameraCalibration2 = 50724;
    private const ushort ReductionMatrix1 = 50725;
    private const ushort ReductionMatrix2 = 50726;
    private const ushort AnalogBalance = 50727;
    private const ushort AsShotNeutral = 50728;
    private const ushort CalibrationIlluminant1 = 50778;
    private const ushort CalibrationIlluminant2 = 50779;
    private const ushort UniqueCameraModel = 50708;
    private const ushort ProfileCalibrationSignature = 50932;
    private const ushort CameraCalibrationSignature = 50931;
    private const ushort ProfileName = 50936;
    private const ushort ProfileHueSatMapDims = 50937;
    private const ushort ProfileHueSatMapData1 = 50938;
    private const ushort ProfileHueSatMapData2 = 50939;
    private const ushort ProfileEmbedPolicy = 50941;
    private const ushort ForwardMatrix1 = 50964;
    private const ushort ForwardMatrix2 = 50965;
    private const ushort ProfileHueSatMapEncoding = 51107;

    internal static byte[] Create(SyntheticDcpOptions? options = null)
    {
        options ??= new SyntheticDcpOptions();
        return Write(BuildEntries(options), options.CyclicIfd);
    }

    internal static byte[] CreateDngWithExtraProfile(
        SyntheticDcpOptions primary,
        SyntheticDcpOptions extra)
    {
        var primaryEntries = BuildEntries(primary);
        var extraEntries = BuildEntries(extra);
        primaryEntries.Add(Longs(ExtraCameraProfiles, [0]));
        primaryEntries.Sort((left, right) => left.Tag.CompareTo(right.Tag));
        extraEntries.Sort((left, right) => left.Tag.CompareTo(right.Tag));

        const int rootOffset = 8;
        var extraOffset = checked(rootOffset + IfdByteCount(primaryEntries));
        var extraTag = primaryEntries.Single(entry =>
            entry.Tag == ExtraCameraProfiles);
        primaryEntries[primaryEntries.IndexOf(extraTag)] =
            Longs(ExtraCameraProfiles, [checked((uint)extraOffset)]);
        var dataOffset = checked(extraOffset + IfdByteCount(extraEntries));
        var total = checked(dataOffset + ExternalDataBytes(primaryEntries) +
            ExternalDataBytes(extraEntries));
        var bytes = CreateHeader(total, rootOffset);
        WriteIfd(bytes, primaryEntries, rootOffset, ref dataOffset, 0);
        WriteIfd(bytes, extraEntries, extraOffset, ref dataOffset, 0);
        return bytes;
    }

    private static List<Entry> BuildEntries(SyntheticDcpOptions options)
    {
        var entries = new List<Entry>();
        if (options.IncludeColorMatrix1)
        {
            entries.Add(SRational(ColorMatrix1, options.ColorMatrix1));
        }
        AddMatrix(entries, ColorMatrix2, options.ColorMatrix2);
        AddMatrix(entries, ForwardMatrix1, options.ForwardMatrix1);
        AddMatrix(entries, ForwardMatrix2, options.ForwardMatrix2);
        AddMatrix(entries, CameraCalibration1, options.CameraCalibration1);
        AddMatrix(entries, CameraCalibration2, options.CameraCalibration2);
        AddMatrix(entries, ReductionMatrix1, options.ReductionMatrix1);
        AddMatrix(entries, ReductionMatrix2, options.ReductionMatrix2);
        AddUnsignedRational(entries, AnalogBalance, options.AnalogBalance);
        AddUnsignedRational(entries, AsShotNeutral, options.AsShotNeutral);
        AddShort(entries, CalibrationIlluminant1, options.Illuminant1);
        AddShort(entries, CalibrationIlluminant2, options.Illuminant2);
        AddAscii(entries, ProfileName, options.Name);
        AddAscii(entries, UniqueCameraModel, options.UniqueCameraModel);
        AddAscii(entries, ProfileCalibrationSignature, options.CalibrationSignature);
        AddAscii(
            entries,
            CameraCalibrationSignature,
            options.CameraCalibrationSignature);
        AddLong(entries, ProfileEmbedPolicy, options.EmbedPolicy);
        if (options.HueSatDimensions != null)
        {
            entries.Add(Longs(ProfileHueSatMapDims, options.HueSatDimensions));
        }
        if (options.HueSatTable1 != null)
        {
            entries.Add(Floats(ProfileHueSatMapData1, options.HueSatTable1));
        }
        if (options.HueSatTable2 != null)
        {
            entries.Add(Floats(ProfileHueSatMapData2, options.HueSatTable2));
        }
        AddLong(entries, ProfileHueSatMapEncoding, options.HueSatEncoding);
        if (options.ExtraLongTag.HasValue)
        {
            AddLong(entries, options.ExtraLongTag.Value, 1);
        }
        return entries;
    }

    internal static string WriteTemporary(
        string directory,
        SyntheticDcpOptions? options = null,
        string name = "synthetic.dcp")
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, Create(options));
        return path;
    }

    private static byte[] Write(List<Entry> entries, bool cyclicIfd)
    {
        entries.Sort((left, right) => left.Tag.CompareTo(right.Tag));
        const int ifdOffset = 8;
        var dataOffset = checked(ifdOffset + IfdByteCount(entries));
        var total = checked(dataOffset + ExternalDataBytes(entries));
        var bytes = CreateHeader(total, ifdOffset);
        WriteIfd(
            bytes,
            entries,
            ifdOffset,
            ref dataOffset,
            cyclicIfd ? (uint)ifdOffset : 0u);
        return bytes;
    }

    private static byte[] CreateHeader(int total, int ifdOffset)
    {
        var bytes = new byte[total];
        bytes[0] = (byte)'I';
        bytes[1] = (byte)'I';
        WriteUInt16(bytes, 2, 0x4352);
        WriteUInt32(bytes, 4, checked((uint)ifdOffset));
        return bytes;
    }

    private static void WriteIfd(
        byte[] bytes,
        IReadOnlyList<Entry> entries,
        int ifdOffset,
        ref int valueOffset,
        uint nextOffset)
    {
        WriteUInt16(bytes, ifdOffset, checked((ushort)entries.Count));

        var entryOffset = ifdOffset + 2;
        foreach (var entry in entries)
        {
            WriteUInt16(bytes, entryOffset, entry.Tag);
            WriteUInt16(bytes, entryOffset + 2, entry.Type);
            WriteUInt32(bytes, entryOffset + 4, entry.Count);
            if (entry.Data.Length <= 4)
            {
                entry.Data.CopyTo(bytes, entryOffset + 8);
            }
            else
            {
                WriteUInt32(bytes, entryOffset + 8, checked((uint)valueOffset));
                entry.Data.CopyTo(bytes, valueOffset);
                valueOffset += entry.Data.Length;
            }
            entryOffset += 12;
        }
        WriteUInt32(bytes, entryOffset, nextOffset);
    }

    private static int IfdByteCount(IReadOnlyCollection<Entry> entries) =>
        checked(2 + entries.Count * 12 + 4);

    private static int ExternalDataBytes(IEnumerable<Entry> entries) =>
        entries.Where(entry => entry.Data.Length > 4)
            .Sum(entry => entry.Data.Length);

    private static Entry SRational(ushort tag, IReadOnlyList<double> values)
    {
        var data = new byte[checked(values.Count * 8)];
        for (var index = 0; index < values.Count; index++)
        {
            const int denominator = 1_000_000;
            var numerator = checked((int)Math.Round(
                values[index] * denominator,
                MidpointRounding.AwayFromZero));
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(index * 8, 4),
                numerator);
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(index * 8 + 4, 4),
                denominator);
        }
        return new Entry(tag, 10, checked((uint)values.Count), data);
    }

    private static Entry Longs(ushort tag, IReadOnlyList<uint> values)
    {
        var data = new byte[checked(values.Count * 4)];
        for (var index = 0; index < values.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                data.AsSpan(index * 4, 4),
                values[index]);
        }
        return new Entry(tag, 4, checked((uint)values.Count), data);
    }

    private static Entry UnsignedRational(
        ushort tag,
        IReadOnlyList<double> values)
    {
        var data = new byte[checked(values.Count * 8)];
        for (var index = 0; index < values.Count; index++)
        {
            const uint denominator = 1_000_000;
            var numerator = checked((uint)Math.Round(
                values[index] * denominator,
                MidpointRounding.AwayFromZero));
            BinaryPrimitives.WriteUInt32LittleEndian(
                data.AsSpan(index * 8, 4),
                numerator);
            BinaryPrimitives.WriteUInt32LittleEndian(
                data.AsSpan(index * 8 + 4, 4),
                denominator);
        }
        return new Entry(tag, 5, checked((uint)values.Count), data);
    }

    private static Entry Floats(ushort tag, IReadOnlyList<float> values)
    {
        var data = new byte[checked(values.Count * 4)];
        for (var index = 0; index < values.Count; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(index * 4, 4),
                BitConverter.SingleToInt32Bits(values[index]));
        }
        return new Entry(tag, 11, checked((uint)values.Count), data);
    }

    private static void AddMatrix(
        ICollection<Entry> entries,
        ushort tag,
        double[]? values)
    {
        if (values != null) entries.Add(SRational(tag, values));
    }

    private static void AddUnsignedRational(
        ICollection<Entry> entries,
        ushort tag,
        double[]? values)
    {
        if (values != null) entries.Add(UnsignedRational(tag, values));
    }

    private static void AddShort(
        ICollection<Entry> entries,
        ushort tag,
        int? value)
    {
        if (!value.HasValue) return;
        var data = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(data, checked((ushort)value.Value));
        entries.Add(new Entry(tag, 3, 1, data));
    }

    private static void AddLong(
        ICollection<Entry> entries,
        ushort tag,
        uint? value)
    {
        if (value.HasValue) entries.Add(Longs(tag, [value.Value]));
    }

    private static void AddAscii(
        ICollection<Entry> entries,
        ushort tag,
        string? value)
    {
        if (value == null) return;
        var data = Encoding.UTF8.GetBytes(value + '\0');
        entries.Add(new Entry(tag, 2, checked((uint)data.Length), data));
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), value);

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);

    private sealed record Entry(
        ushort Tag,
        ushort Type,
        uint Count,
        byte[] Data);
}
