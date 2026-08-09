using Avalonia;

namespace HappyPhoton.Services;

internal static class JpegDimensions
{
    private const long MaximumHeaderBytes = 1024 * 1024;

    public static bool TryRead(string path, out PixelSize dimensions)
    {
        dimensions = default;
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return TryRead(stream, out dimensions);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryRead(Stream stream, out PixelSize dimensions)
    {
        dimensions = default;
        if (ReadByte(stream) != 0xff || ReadByte(stream) != 0xd8)
        {
            return false;
        }

        while (stream.Position < Math.Min(stream.Length, MaximumHeaderBytes))
        {
            var prefix = ReadByte(stream);
            if (prefix < 0) return false;
            if (prefix != 0xff) continue;

            int marker;
            do
            {
                marker = ReadByte(stream);
            }
            while (marker == 0xff);

            if (marker is < 0 or 0xda or 0xd9) return false;
            if (marker is 0x01 or >= 0xd0 and <= 0xd7) continue;

            var segmentLength = ReadUInt16(stream);
            if (segmentLength < 2) return false;
            if (IsStartOfFrame(marker))
            {
                if (segmentLength < 7 || ReadByte(stream) < 0) return false;
                var height = ReadUInt16(stream);
                var width = ReadUInt16(stream);
                if (width <= 0 || height <= 0) return false;
                dimensions = new PixelSize(width, height);
                return true;
            }

            var next = stream.Position + segmentLength - 2;
            if (next > stream.Length || next > MaximumHeaderBytes) return false;
            stream.Position = next;
        }

        return false;
    }

    private static bool IsStartOfFrame(int marker) => marker is
        0xc0 or 0xc1 or 0xc2 or 0xc3 or
        0xc5 or 0xc6 or 0xc7 or
        0xc9 or 0xca or 0xcb or
        0xcd or 0xce or 0xcf;

    private static int ReadByte(Stream stream) => stream.ReadByte();

    private static int ReadUInt16(Stream stream)
    {
        var high = ReadByte(stream);
        var low = ReadByte(stream);
        return high < 0 || low < 0 ? -1 : (high << 8) | low;
    }
}
