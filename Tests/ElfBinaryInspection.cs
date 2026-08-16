using System.Buffers.Binary;
using System.Text;

namespace HappyPhoton.Tests;

internal static class ElfBinaryInspection
{
    private sealed record Segment(uint Type, ulong Offset, ulong Address, ulong FileSize, ulong MemorySize);

    public static NativeBinaryInfo Inspect(Stream stream)
    {
        try
        {
            var data = ReadAll(stream);
            if (data.Length < 64 || data[4] != 2)
            {
                throw new InvalidDataException("Only ELF64 binaries are supported.");
            }

            var littleEndian = data[5] switch
            {
                1 => true,
                2 => false,
                _ => throw new InvalidDataException("Invalid ELF byte order.")
            };
            var machine = U16(data, 18, littleEndian);
            var programOffset = U64(data, 32, littleEndian);
            var entrySize = U16(data, 54, littleEndian);
            var count = U16(data, 56, littleEndian);
            if (entrySize < 56 || count > 4096)
            {
                throw new InvalidDataException("Invalid ELF program header table.");
            }

            var segments = new List<Segment>(count);
            for (var i = 0; i < count; i++)
            {
                var offset = CheckedOffset(programOffset + (ulong)i * entrySize, data.Length, 56);
                segments.Add(new Segment(
                    U32(data, offset, littleEndian),
                    U64(data, offset + 8, littleEndian),
                    U64(data, offset + 16, littleEndian),
                    U64(data, offset + 32, littleEndian),
                    U64(data, offset + 40, littleEndian)));
            }

            var dynamicSegment = segments.SingleOrDefault(segment => segment.Type == 2)
                ?? throw new InvalidDataException("ELF dynamic segment is missing.");
            var dynamicOffset = CheckedOffset(dynamicSegment.Offset, data.Length, 16);
            var dynamicEnd = CheckedOffset(
                dynamicSegment.Offset + dynamicSegment.FileSize,
                data.Length,
                0);
            var entries = new List<(long Tag, ulong Value)>();
            for (var offset = dynamicOffset; offset + 16 <= dynamicEnd; offset += 16)
            {
                var tag = unchecked((long)U64(data, offset, littleEndian));
                var value = U64(data, offset + 8, littleEndian);
                if (tag == 0)
                {
                    break;
                }
                entries.Add((tag, value));
            }

            var stringAddress = SingleValue(entries, 5);
            var stringSize = SingleValue(entries, 10);
            var stringOffset = VirtualToFile(stringAddress, segments, data.Length);
            var stringEnd = CheckedOffset((ulong)stringOffset + stringSize, data.Length, 0);
            string ReadString(ulong index) => ReadAsciiZ(
                data,
                CheckedOffset((ulong)stringOffset + index, stringEnd, 1),
                stringEnd);

            var imports = entries.Where(entry => entry.Tag == 1)
                .Select(entry => ReadString(entry.Value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var identity = entries.Where(entry => entry.Tag == 14)
                .Select(entry => ReadString(entry.Value))
                .SingleOrDefault();
            var requirements = ReadVersionRequirements(
                data, entries, segments, littleEndian, ReadString);
            return new NativeBinaryInfo(
                "ELF64", Architecture(machine), identity, imports, requirements);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("ELF offset overflowed.", exception);
        }
    }

    private static IReadOnlyList<string> ReadVersionRequirements(
        byte[] data,
        IReadOnlyList<(long Tag, ulong Value)> entries,
        IReadOnlyList<Segment> segments,
        bool littleEndian,
        Func<ulong, string> readString)
    {
        var address = entries.FirstOrDefault(entry => entry.Tag == 0x6ffffffe).Value;
        var count = entries.FirstOrDefault(entry => entry.Tag == 0x6fffffff).Value;
        if (address == 0 || count == 0)
        {
            return [];
        }

        var offset = VirtualToFile(address, segments, data.Length);
        var versions = new List<string>();
        for (ulong i = 0; i < count; i++)
        {
            Require(data, offset, 16);
            var auxiliaryCount = U16(data, offset + 2, littleEndian);
            var file = readString(U32(data, offset + 4, littleEndian));
            var auxiliary = offset + checked((int)U32(data, offset + 8, littleEndian));
            for (var j = 0; j < auxiliaryCount; j++)
            {
                Require(data, auxiliary, 16);
                var name = readString(U32(data, auxiliary + 8, littleEndian));
                versions.Add($"{file}: {name}");
                var nextAuxiliary = U32(data, auxiliary + 12, littleEndian);
                if (nextAuxiliary == 0)
                {
                    break;
                }
                auxiliary += checked((int)nextAuxiliary);
            }

            var next = U32(data, offset + 12, littleEndian);
            if (next == 0)
            {
                break;
            }
            offset += checked((int)next);
        }

        return versions.Distinct(StringComparer.Ordinal).Order().ToArray();
    }

    private static ulong SingleValue(IReadOnlyList<(long Tag, ulong Value)> entries, long tag)
    {
        var values = entries.Where(entry => entry.Tag == tag).Select(entry => entry.Value).ToArray();
        return values.Length == 1
            ? values[0]
            : throw new InvalidDataException($"ELF dynamic tag {tag} is missing or duplicated.");
    }

    private static int VirtualToFile(ulong address, IReadOnlyList<Segment> segments, int length)
    {
        var segment = segments.FirstOrDefault(candidate =>
            candidate.Type == 1 && address >= candidate.Address &&
            address < candidate.Address + candidate.MemorySize)
            ?? throw new InvalidDataException($"ELF address 0x{address:X} is outside load segments.");
        return CheckedOffset(segment.Offset + address - segment.Address, length, 1);
    }

    private static byte[] ReadAll(Stream stream)
    {
        if (stream.Length > int.MaxValue)
        {
            throw new InvalidDataException("ELF binary is too large.");
        }
        var data = new byte[(int)stream.Length];
        stream.Position = 0;
        stream.ReadExactly(data);
        return data;
    }

    private static string ReadAsciiZ(byte[] data, int offset, int end)
    {
        var terminator = Array.IndexOf(data, (byte)0, offset, end - offset);
        if (terminator < 0)
        {
            throw new InvalidDataException("ELF string is not terminated.");
        }
        return Encoding.ASCII.GetString(data, offset, terminator - offset);
    }

    private static ushort U16(byte[] data, int offset, bool little)
    {
        Require(data, offset, 2);
        return little
            ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset))
            : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
    }

    private static uint U32(byte[] data, int offset, bool little)
    {
        Require(data, offset, 4);
        return little
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset))
            : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
    }

    private static ulong U64(byte[] data, int offset, bool little)
    {
        Require(data, offset, 8);
        return little
            ? BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset))
            : BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset));
    }

    private static int CheckedOffset(ulong offset, int limit, int size)
    {
        if (offset > (ulong)limit || size < 0 || offset + (ulong)size > (ulong)limit)
        {
            throw new InvalidDataException("ELF offset is outside the file.");
        }
        return checked((int)offset);
    }

    private static void Require(byte[] data, int offset, int size)
    {
        if (offset < 0 || size < 0 || offset > data.Length - size)
        {
            throw new InvalidDataException("ELF structure is truncated.");
        }
    }

    private static string Architecture(ushort machine) => machine switch
    {
        62 => "x86-64",
        183 => "arm64",
        3 => "x86",
        _ => $"machine-{machine}"
    };
}
