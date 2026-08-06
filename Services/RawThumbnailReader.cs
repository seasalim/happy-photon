using System.Text;
using Sdcb.LibRaw;

namespace HappyPhoton.Services;

internal static class RawThumbnailReader
{
    internal static byte[]? Read(RawContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            context.UnpackThumbnail();
            using var thumbnail = context.MakeDcrawMemoryThumbnail();
            var data = thumbnail.AsSpan<byte>().ToArray();
            if (data.Length > 2 && data[0] == 0xff && data[1] == 0xd8)
            {
                return data;
            }

            if (thumbnail.Width == 0 || thumbnail.Height == 0 || data.Length == 0)
            {
                return null;
            }

            var header = Encoding.ASCII.GetBytes(
                $"P6\n{thumbnail.Width} {thumbnail.Height}\n255\n");
            var ppm = new byte[header.Length + data.Length];
            header.CopyTo(ppm, 0);
            data.CopyTo(ppm, header.Length);
            return ppm;
        }
        catch
        {
            return null;
        }
    }
}
