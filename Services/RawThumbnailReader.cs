using System.Text;
using HappyPhoton.LibRaw.Interop;

namespace HappyPhoton.Services;

internal static class RawThumbnailReader
{
    internal static byte[]? Read(LibRawContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            using var thumbnail = context.ExtractThumbnail();
            if (thumbnail == null) return null;
            var description = thumbnail.Description;
            var data = thumbnail.CopyData();
            if (data.Length > 2 && data[0] == 0xff && data[1] == 0xd8)
            {
                return data;
            }

            if (description.Width == 0 || description.Height == 0 ||
                description.BitsPerSample != 8 || description.Channels != 3 ||
                data.Length == 0)
            {
                return null;
            }

            var header = Encoding.ASCII.GetBytes(
                $"P6\n{description.Width} {description.Height}\n255\n");
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
