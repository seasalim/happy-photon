using System.Buffers.Binary;
using System.Text;

namespace HappyPhoton.Services;

internal sealed class DcpTiffReader : IDisposable
{
    private const int MaxIfdEntries = 4096;
    private const int MaxIfdDepth = 8;
    private const int MaxValueBytes = 4 * 1024 * 1024;
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly long _headerOffset;
    private readonly bool _littleEndian;
    private readonly long _firstIfdOffset;

    private DcpTiffReader(Stream stream, bool ownsStream, long headerOffset)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _headerOffset = headerOffset;
        Span<byte> header = stackalloc byte[8];
        ReadExactly(headerOffset, header);
        _littleEndian = header[0] == (byte)'I' && header[1] == (byte)'I';
        var bigEndian = header[0] == (byte)'M' && header[1] == (byte)'M';
        if (!_littleEndian && !bigEndian)
        {
            throw Invalid("The DCP byte-order marker is invalid.");
        }

        var magic = ReadUInt16(header[2..4]);
        if (magic is not (42 or 0x4352))
        {
            throw new DcpProfileException(
                DcpProfileErrorCode.UnsupportedVariant,
                $"Unsupported TIFF/DCP magic value {magic}.");
        }
        _firstIfdOffset = CheckedAbsolute(ReadUInt32(header[4..8]), 0);
    }

    internal long Length => _stream.Length;

    internal static DcpTiffReader Open(byte[] snapshot) => new(
        new MemoryStream(
            snapshot ?? throw new ArgumentNullException(nameof(snapshot)),
            writable: false),
        ownsStream: true,
        headerOffset: 0);

    internal static DcpTiffReader Open(string path, long headerOffset = 0) =>
        new(
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                4096,
                FileOptions.RandomAccess),
            ownsStream: true,
            headerOffset);

    internal TiffIfd ReadFirstIfd() => ReadIfd(_firstIfdOffset);

    internal TiffIfd ReadIfdAtOffset(uint relativeOffset) =>
        ReadIfd(CheckedAbsolute(relativeOffset, 0));

    internal IReadOnlyList<TiffIfd> ReadIfdChain()
    {
        var result = new List<TiffIfd>();
        var visited = new HashSet<long>();
        var offset = _firstIfdOffset;
        while (offset != 0)
        {
            if (result.Count >= MaxIfdDepth || !visited.Add(offset))
            {
                throw Invalid("The DCP IFD chain is cyclic or too deep.");
            }
            var ifd = ReadIfd(offset);
            result.Add(ifd);
            offset = ifd.NextOffset;
        }
        return result;
    }

    internal byte[] ReadValue(TiffEntry entry, int expectedMaximum = MaxValueBytes)
    {
        var size = GetTypeSize(entry.Type);
        var length = CheckedLength(entry.Count, size, expectedMaximum);
        if (length <= 4)
        {
            return entry.InlineValue[..length];
        }

        var absolute = CheckedAbsolute(entry.ValueOffset, length);
        var result = GC.AllocateUninitializedArray<byte>(length);
        ReadExactly(absolute, result);
        return result;
    }

    internal string ReadString(TiffEntry entry, int maximumBytes = 4096)
    {
        RequireType(entry, 1, 2, 7);
        var bytes = ReadValue(entry, maximumBytes);
        var end = Array.IndexOf(bytes, (byte)0);
        if (end < 0) end = bytes.Length;
        return Encoding.UTF8.GetString(bytes, 0, end).Trim();
    }

    internal ushort ReadShort(TiffEntry entry)
    {
        RequireType(entry, 3);
        RequireCount(entry, 1);
        return ReadUInt16(ReadValue(entry));
    }

    internal ushort[] ReadShorts(TiffEntry entry, int expectedCount)
    {
        RequireType(entry, 3);
        RequireCount(entry, expectedCount);
        var bytes = ReadValue(entry);
        var values = new ushort[expectedCount];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = ReadUInt16(bytes.AsSpan(index * 2, 2));
        }
        return values;
    }

    internal uint ReadLong(TiffEntry entry)
    {
        RequireType(entry, 4);
        RequireCount(entry, 1);
        return ReadUInt32(ReadValue(entry));
    }

    internal uint[] ReadLongs(TiffEntry entry, int expectedCount)
    {
        RequireType(entry, 4);
        RequireCount(entry, expectedCount);
        var bytes = ReadValue(entry);
        var values = new uint[expectedCount];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = ReadUInt32(bytes.AsSpan(index * 4, 4));
        }
        return values;
    }

    internal double[] ReadRationals(
        TiffEntry entry,
        int expectedCount,
        bool allowUnsigned = false)
    {
        if (entry.Type != 10 && !(allowUnsigned && entry.Type == 5))
        {
            throw Invalid($"Tag {entry.Tag} has unsupported rational type {entry.Type}.");
        }
        RequireCount(entry, expectedCount);
        var bytes = ReadValue(entry);
        var values = new double[expectedCount];
        for (var index = 0; index < values.Length; index++)
        {
            var pair = bytes.AsSpan(index * 8, 8);
            double numerator = entry.Type == 10
                ? ReadInt32(pair[..4])
                : ReadUInt32(pair[..4]);
            double denominator = entry.Type == 10
                ? ReadInt32(pair[4..])
                : ReadUInt32(pair[4..]);
            if (denominator == 0)
            {
                throw Invalid($"Tag {entry.Tag} contains a zero denominator.");
            }
            values[index] = numerator / denominator;
        }
        return values;
    }

    internal float[] ReadFloats(TiffEntry entry, int expectedCount)
    {
        RequireType(entry, 11);
        RequireCount(entry, expectedCount);
        var bytes = ReadValue(entry);
        var values = new float[expectedCount];
        for (var index = 0; index < values.Length; index++)
        {
            var bits = ReadInt32(bytes.AsSpan(index * 4, 4));
            values[index] = BitConverter.Int32BitsToSingle(bits);
            if (!float.IsFinite(values[index]))
            {
                throw Invalid($"Tag {entry.Tag} contains a non-finite value.");
            }
        }
        return values;
    }

    internal double[] ReadUnsignedValues(TiffEntry entry)
    {
        if (entry.Type is not (3 or 4 or 13))
            throw Invalid($"Tag {entry.Tag} has unsupported integer type {entry.Type}.");
        var bytes = ReadValue(entry);
        var size = entry.Type == 3 ? 2 : 4;
        var values = new double[checked((int)entry.Count)];
        for (var index = 0; index < values.Length; index++)
        {
            var value = bytes.AsSpan(index * size, size);
            values[index] = entry.Type == 3 ? ReadUInt16(value) : ReadUInt32(value);
        }
        return values;
    }

    internal double[] ReadNumericValues(TiffEntry entry)
    {
        if (entry.Type is 3 or 4) return ReadUnsignedValues(entry);
        if (entry.Type is not (5 or 10))
            throw Invalid($"Tag {entry.Tag} has unsupported numeric type {entry.Type}.");
        return ReadRationals(entry, checked((int)entry.Count), allowUnsigned: true);
    }

    private TiffIfd ReadIfd(long absoluteOffset)
    {
        Span<byte> countBytes = stackalloc byte[2];
        ReadExactly(absoluteOffset, countBytes);
        var count = ReadUInt16(countBytes);
        if (count > MaxIfdEntries)
        {
            throw Invalid($"The DCP IFD contains too many entries ({count}).");
        }

        var tableBytes = checked(count * 12);
        var table = GC.AllocateUninitializedArray<byte>(tableBytes + 4);
        ReadExactly(checked(absoluteOffset + 2), table);
        var entries = new Dictionary<ushort, TiffEntry>();
        for (var index = 0; index < count; index++)
        {
            var item = table.AsSpan(index * 12, 12);
            var tag = ReadUInt16(item[..2]);
            var entry = new TiffEntry(
                tag,
                ReadUInt16(item[2..4]),
                ReadUInt32(item[4..8]),
                ReadUInt32(item[8..12]),
                item[8..12].ToArray());
            _ = GetTypeSize(entry.Type);
            _ = CheckedLength(entry.Count, GetTypeSize(entry.Type), MaxValueBytes);
            if (!entries.TryAdd(tag, entry))
            {
                throw Invalid($"The DCP repeats tag {tag}.");
            }
        }

        var nextRelative = ReadUInt32(table.AsSpan(tableBytes, 4));
        var next = nextRelative == 0
            ? 0
            : CheckedAbsolute(nextRelative, 0);
        return new TiffIfd(entries, next);
    }

    private void ReadExactly(long offset, Span<byte> destination)
    {
        if (offset < 0 || destination.Length > _stream.Length - offset)
        {
            throw Invalid("A DCP offset or count is outside the container.");
        }
        _stream.Position = offset;
        _stream.ReadExactly(destination);
    }

    private long CheckedAbsolute(uint relativeOffset, int length)
    {
        var absolute = checked(_headerOffset + relativeOffset);
        if (absolute < _headerOffset || length > _stream.Length - absolute)
        {
            throw Invalid("A DCP offset or count is outside the container.");
        }
        return absolute;
    }

    private static int CheckedLength(uint count, int size, int maximum)
    {
        var length = checked((long)count * size);
        if (length > maximum || length > int.MaxValue)
        {
            throw new DcpProfileException(
                DcpProfileErrorCode.TooLarge,
                "A DCP tag exceeds the supported size limit.");
        }
        return (int)length;
    }

    private static int GetTypeSize(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 or 13 => 4,
        5 or 10 or 12 => 8,
        _ => throw new DcpProfileException(
            DcpProfileErrorCode.UnsupportedVariant,
            $"Unsupported TIFF field type {type}.")
    };

    private static void RequireType(TiffEntry entry, params ushort[] types)
    {
        if (!types.Contains(entry.Type))
        {
            throw Invalid($"Tag {entry.Tag} has TIFF type {entry.Type}.");
        }
    }

    private static void RequireCount(TiffEntry entry, int expected)
    {
        if (entry.Count != expected)
        {
            throw Invalid(
                $"Tag {entry.Tag} has count {entry.Count}; expected {expected}.");
        }
    }

    private ushort ReadUInt16(ReadOnlySpan<byte> value) => _littleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(value)
        : BinaryPrimitives.ReadUInt16BigEndian(value);

    private uint ReadUInt32(ReadOnlySpan<byte> value) => _littleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(value)
        : BinaryPrimitives.ReadUInt32BigEndian(value);

    private int ReadInt32(ReadOnlySpan<byte> value) => _littleEndian
        ? BinaryPrimitives.ReadInt32LittleEndian(value)
        : BinaryPrimitives.ReadInt32BigEndian(value);

    private static DcpProfileException Invalid(string message) => new(
        DcpProfileErrorCode.InvalidContainer,
        message);

    public void Dispose()
    {
        if (_ownsStream) _stream.Dispose();
    }
}

internal sealed record TiffIfd(
    IReadOnlyDictionary<ushort, TiffEntry> Entries,
    long NextOffset)
{
    internal TiffEntry? Find(ushort tag) =>
        Entries.TryGetValue(tag, out var entry) ? entry : null;
}

internal sealed record TiffEntry(
    ushort Tag,
    ushort Type,
    uint Count,
    uint ValueOffset,
    byte[] InlineValue);
