using System.Buffers.Binary;

namespace HappyPhoton.Tests;

internal static class DngCompressionInspection
{
    public static IReadOnlyList<ushort> ReadCompressionTags(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 8)
        {
            throw new InvalidDataException("TIFF header is truncated.");
        }

        var little = data.AsSpan(0, 2).SequenceEqual("II"u8);
        if (!little && !data.AsSpan(0, 2).SequenceEqual("MM"u8))
        {
            throw new InvalidDataException("Invalid TIFF byte order.");
        }
        if (U16(data, 2, little) != 42)
        {
            throw new InvalidDataException("Only classic TIFF/DNG is supported.");
        }

        var pending = new Queue<uint>();
        pending.Enqueue(U32(data, 4, little));
        var visited = new HashSet<uint>();
        var compressions = new List<ushort>();
        while (pending.TryDequeue(out var ifdOffset))
        {
            if (ifdOffset == 0 || !visited.Add(ifdOffset))
            {
                continue;
            }

            var offset = CheckedOffset(ifdOffset, data.Length, 2);
            var count = U16(data, offset, little);
            if (count > 4096)
            {
                throw new InvalidDataException("TIFF IFD contains too many entries.");
            }
            var entriesOffset = offset + 2;
            Require(data, entriesOffset, checked(count * 12 + 4));
            for (var i = 0; i < count; i++)
            {
                var entry = entriesOffset + i * 12;
                var tag = U16(data, entry, little);
                var type = U16(data, entry + 2, little);
                var valueCount = U32(data, entry + 4, little);
                if (tag == 259)
                {
                    foreach (var value in ReadValues(data, entry, type, valueCount, little))
                    {
                        compressions.Add(checked((ushort)value));
                    }
                }
                else if (tag == 330)
                {
                    foreach (var value in ReadValues(data, entry, type, valueCount, little))
                    {
                        pending.Enqueue(value);
                    }
                }
            }

            pending.Enqueue(U32(data, entriesOffset + count * 12, little));
        }

        if (compressions.Count == 0)
        {
            throw new InvalidDataException("DNG contains no TIFF Compression tags.");
        }
        return compressions.Distinct().Order().ToArray();
    }

    private static IEnumerable<uint> ReadValues(
        byte[] data,
        int entry,
        ushort type,
        uint count,
        bool little)
    {
        var elementSize = type switch
        {
            3 => 2,
            4 or 13 => 4,
            _ => throw new InvalidDataException($"Unsupported TIFF field type {type}.")
        };
        if (count > 4096)
        {
            throw new InvalidDataException("TIFF value array is too large.");
        }
        var byteCount = checked((int)count * elementSize);
        var offset = byteCount <= 4
            ? entry + 8
            : CheckedOffset(U32(data, entry + 8, little), data.Length, byteCount);
        Require(data, offset, byteCount);
        for (var i = 0; i < count; i++)
        {
            yield return type == 3
                ? U16(data, offset + checked((int)i * 2), little)
                : U32(data, offset + checked((int)i * 4), little);
        }
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

    private static int CheckedOffset(uint offset, int length, int size)
    {
        if (offset > int.MaxValue || offset > length - size)
        {
            throw new InvalidDataException("TIFF offset is outside the file.");
        }
        return (int)offset;
    }

    private static void Require(byte[] data, int offset, int size)
    {
        if (offset < 0 || size < 0 || offset > data.Length - size)
        {
            throw new InvalidDataException("TIFF structure is truncated.");
        }
    }
}
