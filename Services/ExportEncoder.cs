using System.Buffers.Binary;
using HappyPhoton.Models;
using ImageMagick;
using ImageMagick.Formats;

namespace HappyPhoton.Services;

internal static class ExportEncoder
{
    private const uint PngAdaptiveFilterQuality = 85;

    public static void Write(
        MagickImage image,
        ExportSettings settings,
        OutputColorSpace outputColorSpace,
        string path,
        bool overwriteExisting = false)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var quality = Math.Clamp(settings.Quality, 1, 100);
        image.Format = GetFormat(settings.Format);
        image.Quality = (uint)quality;
        image.SetProfile(OutputColorProfiles.Get(outputColorSpace));

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(path) ?? string.Empty,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            switch (settings.Format)
            {
                case ExportFormat.Png:
                    WritePng(image, temporaryPath);
                    break;
                case ExportFormat.Webp:
                    image.Settings.SetDefine(MagickFormat.WebP, "lossless", false);
                    image.Write(temporaryPath);
                    break;
                case ExportFormat.Tiff:
                    WriteTiff(image, temporaryPath);
                    break;
                default:
                    image.Settings.Interlace = Interlace.NoInterlace;
                    image.Settings.SetDefine(
                        MagickFormat.Jpeg,
                        "sampling-factor",
                        quality >= 90 ? "4:4:4" : "4:2:0");
                    image.Write(temporaryPath);
                    break;
            }

            File.Move(temporaryPath, path, overwriteExisting);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void WritePng(MagickImage image, string path)
    {
        // Depth is deliberately left alone: setting it to 8 quantizes toward zero,
        // while the writer's BitDepth define rounds to the nearest level like the
        // display path (OUTPUT.md §1).
        image.Quality = PngAdaptiveFilterQuality;
        image.RemoveArtifact("png:exclude-chunk");
        image.Settings.RemoveDefine(MagickFormat.Png, "exclude-chunk");
        image.Write(path, CreatePngWriteDefines());
    }

    internal static PngWriteDefines CreatePngWriteDefines() =>
        new()
        {
            BitDepth = 8,
            CompressionLevel = 3,
            CompressionStrategy = PngCompressionStrategy.Adaptive,
            PreserveiCCP = true,
            ExcludeChunks = PngChunkFlags.sRGB
        };

    private static void WriteTiff(MagickImage image, string path)
    {
        // ImageMagick's TIFF coder drops the EXIF profile on write (confirmed by
        // read-back), so preserve the normalized profile and splice it into IFD0.
        var exif = image.GetExifProfile()?.ToByteArray();
        image.HasAlpha = false;
        // These encoder settings preserve the Q16 pixels. In particular, do not
        // assign image.Depth: that quantizes the shared pre-encode buffer.
        image.Settings.Depth = 16;
        image.Settings.Compression = CompressionMethod.Zip;
        if (TryGetTiffHeader(exif, out var exifHeader, out var littleEndian))
        {
            image.Settings.Endian = littleEndian ? Endian.LSB : Endian.MSB;
        }
        image.Write(path, new TiffWriteDefines());
        if (exifHeader >= 0)
        {
            WriteTiffExif(path, exif![exifHeader..], littleEndian);
        }
    }

    private static bool TryGetTiffHeader(
        byte[]? exif,
        out int offset,
        out bool littleEndian)
    {
        offset = -1;
        littleEndian = false;
        if (exif is not { Length: >= 8 })
        {
            return false;
        }

        offset = exif.AsSpan().StartsWith("Exif\0\0"u8) ? 6 : 0;
        if (exif.Length < offset + 8)
        {
            offset = -1;
            return false;
        }

        littleEndian = exif[offset] == (byte)'I' &&
            exif[offset + 1] == (byte)'I';
        var bigEndian = exif[offset] == (byte)'M' &&
            exif[offset + 1] == (byte)'M';
        var hasTiffMagic = littleEndian
            ? exif[offset + 2] == 42 && exif[offset + 3] == 0
            : exif[offset + 2] == 0 && exif[offset + 3] == 42;
        return (littleEndian || bigEndian) && hasTiffMagic;
    }

    private static void WriteTiffExif(
        string path,
        byte[] exifTiff,
        bool littleEndian)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        Span<byte> header = stackalloc byte[8];
        stream.ReadExactly(header);
        var outputLittleEndian = header[0] == (byte)'I';
        if (outputLittleEndian != littleEndian)
        {
            throw new InvalidDataException("TIFF and EXIF byte order differ.");
        }

        var originalIfdOffset = ReadUInt32(header[4..], littleEndian);
        var originalEntries = ReadIfd(stream, originalIfdOffset, littleEndian);
        var exifBody = (byte[])exifTiff.Clone();
        var profileIfdOffset = ReadUInt32(exifBody.AsSpan(4), littleEndian);
        var exifIfdOffset = FindIfdPointer(
            exifBody,
            profileIfdOffset,
            34665,
            littleEndian);
        var gpsIfdOffset = FindIfdPointer(
            exifBody,
            profileIfdOffset,
            34853,
            littleEndian);

        stream.Position = stream.Length;
        if ((stream.Position & 1) != 0)
        {
            stream.WriteByte(0);
        }
        var exifBaseOffset = checked((uint)stream.Position);
        RebaseExifOffsets(
            exifBody,
            profileIfdOffset,
            exifBaseOffset,
            littleEndian,
            []);
        stream.Write(exifBody);
        if ((stream.Position & 1) != 0)
        {
            stream.WriteByte(0);
        }

        var entries = new SortedDictionary<ushort, byte[]>();
        foreach (var entry in originalEntries.Entries)
        {
            entries[ReadUInt16(entry, littleEndian)] = entry;
        }
        foreach (var entry in ReadIfdEntries(
                     exifBody,
                     profileIfdOffset,
                     littleEndian))
        {
            var tag = ReadUInt16(entry, littleEndian);
            if (!IsStructuralTiffTag(tag) && tag is not 34665 and not 34853)
            {
                entries[tag] = entry;
            }
        }
        entries.Remove(34665);
        entries.Remove(34853);
        if (exifIfdOffset != 0)
        {
            entries[34665] = CreateLongEntry(
                34665,
                checked(exifBaseOffset + exifIfdOffset),
                littleEndian);
        }
        if (gpsIfdOffset != 0)
        {
            entries[34853] = CreateLongEntry(
                34853,
                checked(exifBaseOffset + gpsIfdOffset),
                littleEndian);
        }

        var replacementIfdOffset = checked((uint)stream.Position);
        WriteUInt16(stream, checked((ushort)entries.Count), littleEndian);
        foreach (var entry in entries.Values)
        {
            stream.Write(entry);
        }
        WriteUInt32(stream, originalEntries.NextOffset, littleEndian);
        stream.Position = 4;
        WriteUInt32(stream, replacementIfdOffset, littleEndian);
    }

    private static TiffIfd ReadIfd(
        Stream stream,
        uint offset,
        bool littleEndian)
    {
        stream.Position = offset;
        Span<byte> countBytes = stackalloc byte[2];
        stream.ReadExactly(countBytes);
        var count = ReadUInt16(countBytes, littleEndian);
        var entries = new List<byte[]>(count);
        for (var index = 0; index < count; index++)
        {
            var entry = new byte[12];
            stream.ReadExactly(entry);
            entries.Add(entry);
        }
        Span<byte> next = stackalloc byte[4];
        stream.ReadExactly(next);
        return new(entries, ReadUInt32(next, littleEndian));
    }

    private static IReadOnlyList<byte[]> ReadIfdEntries(
        byte[] bytes,
        uint offset,
        bool littleEndian)
    {
        var count = ReadUInt16(bytes.AsSpan(checked((int)offset)), littleEndian);
        var entries = new List<byte[]>(count);
        var position = checked((int)offset + 2);
        for (var index = 0; index < count; index++, position += 12)
        {
            entries.Add(bytes.AsSpan(position, 12).ToArray());
        }
        return entries;
    }

    private static uint FindIfdPointer(
        byte[] bytes,
        uint ifdOffset,
        ushort targetTag,
        bool littleEndian)
    {
        foreach (var entry in ReadIfdEntries(bytes, ifdOffset, littleEndian))
        {
            if (ReadUInt16(entry, littleEndian) == targetTag)
            {
                return ReadUInt32(entry.AsSpan(8), littleEndian);
            }
        }
        return 0;
    }

    private static void RebaseExifOffsets(
        byte[] bytes,
        uint ifdOffset,
        uint baseOffset,
        bool littleEndian,
        HashSet<uint> visited)
    {
        if (ifdOffset == 0 || !visited.Add(ifdOffset))
        {
            return;
        }

        var position = checked((int)ifdOffset);
        var count = ReadUInt16(bytes.AsSpan(position), littleEndian);
        position += 2;
        for (var index = 0; index < count; index++, position += 12)
        {
            var entry = bytes.AsSpan(position, 12);
            var tag = ReadUInt16(entry, littleEndian);
            var type = ReadUInt16(entry[2..], littleEndian);
            var valueCount = ReadUInt32(entry[4..], littleEndian);
            var size = checked(GetTiffTypeSize(type) * valueCount);
            var value = ReadUInt32(entry[8..], littleEndian);
            if (size > 4)
            {
                WriteUInt32(entry[8..], checked(value + baseOffset), littleEndian);
            }
            if (tag is 34665 or 34853 or 40965)
            {
                WriteUInt32(entry[8..], checked(value + baseOffset), littleEndian);
                RebaseExifOffsets(
                    bytes,
                    value,
                    baseOffset,
                    littleEndian,
                    visited);
            }
        }

        var nextPosition = position;
        var next = ReadUInt32(bytes.AsSpan(nextPosition), littleEndian);
        if (next != 0)
        {
            WriteUInt32(
                bytes.AsSpan(nextPosition),
                checked(next + baseOffset),
                littleEndian);
            RebaseExifOffsets(
                bytes,
                next,
                baseOffset,
                littleEndian,
                visited);
        }
    }

    private static uint GetTiffTypeSize(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 or 13 => 4,
        5 or 10 or 12 or 16 or 17 or 18 => 8,
        _ => throw new InvalidDataException($"Unsupported TIFF field type {type}.")
    };

    private static bool IsStructuralTiffTag(ushort tag) => tag is
        254 or 255 or 256 or 257 or 258 or 259 or 262 or 266 or 273 or 277 or
        278 or 279 or 282 or 283 or 284 or 296 or 317 or 320 or 322 or 323 or
        324 or 325 or 338 or 339 or 34675;

    private static byte[] CreateLongEntry(
        ushort tag,
        uint value,
        bool littleEndian)
    {
        var entry = new byte[12];
        WriteUInt16(entry, tag, littleEndian);
        WriteUInt16(entry.AsSpan(2), 4, littleEndian);
        WriteUInt32(entry.AsSpan(4), 1, littleEndian);
        WriteUInt32(entry.AsSpan(8), value, littleEndian);
        return entry;
    }

    private static ushort ReadUInt16(
        ReadOnlySpan<byte> bytes,
        bool littleEndian) => littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static uint ReadUInt32(
        ReadOnlySpan<byte> bytes,
        bool littleEndian) => littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt32BigEndian(bytes);

    private static void WriteUInt16(
        Span<byte> bytes,
        ushort value,
        bool littleEndian)
    {
        if (littleEndian) BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        else BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
    }

    private static void WriteUInt32(
        Span<byte> bytes,
        uint value,
        bool littleEndian)
    {
        if (littleEndian) BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        else BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
    }

    private static void WriteUInt16(
        Stream stream,
        ushort value,
        bool littleEndian)
    {
        Span<byte> bytes = stackalloc byte[2];
        WriteUInt16(bytes, value, littleEndian);
        stream.Write(bytes);
    }

    private static void WriteUInt32(
        Stream stream,
        uint value,
        bool littleEndian)
    {
        Span<byte> bytes = stackalloc byte[4];
        WriteUInt32(bytes, value, littleEndian);
        stream.Write(bytes);
    }

    private sealed record TiffIfd(
        IReadOnlyList<byte[]> Entries,
        uint NextOffset);

    private static MagickFormat GetFormat(ExportFormat format) => format switch
    {
        ExportFormat.Png => MagickFormat.Png,
        ExportFormat.Webp => MagickFormat.WebP,
        ExportFormat.Tiff => MagickFormat.Tiff,
        _ => MagickFormat.Jpeg
    };
}
