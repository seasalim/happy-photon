using System.Buffers.Binary;

namespace HappyPhoton.Services;

internal sealed class DngLensPrescriptionReader
{
    private const ushort SubIfds = 330;
    private const ushort ImageWidth = 256;
    private const ushort ImageLength = 257;
    private const ushort DefaultCropOrigin = 50719;
    private const ushort DefaultCropSize = 50720;
    private const ushort ActiveArea = 50829;
    private const ushort OpcodeList1 = 51008;
    private const ushort OpcodeList2 = 51009;
    private const ushort OpcodeList3 = 51022;
    private const ushort LensModel = 42036;
    private const int MaxOpcodes = 128;
    private const int MaxOpcodeBytes = 4 * 1024 * 1024;

    // Adobe DNG 1.7.1, chapter 7. Opcode payloads are always big-endian.
    internal LensPrescriptionReadResult Read(string path)
    {
        try
        {
            using var reader = DcpTiffReader.Open(path);
            var ifds = ReadIfds(reader);
            var candidate = ifds.FirstOrDefault(ifd =>
                ifd.Find(OpcodeList1) != null ||
                ifd.Find(OpcodeList2) != null ||
                ifd.Find(OpcodeList3) != null);
            if (candidate == null)
            {
                return LensPrescriptionReadResult.None;
            }

            var frame = ReadFrame(reader, candidate, out var imageWidth, out var imageHeight);
            var warps = new List<LensWarp>();
            var vignettes = new List<LensVignette>();
            LensFrameWindow? trim = null;
            if (!ParseList(reader, candidate.Find(OpcodeList1), list: 1,
                    warps, vignettes, ref trim, imageWidth, imageHeight, out var error) ||
                !ParseList(reader, candidate.Find(OpcodeList2), list: 2,
                    warps, vignettes, ref trim, imageWidth, imageHeight, out error) ||
                !ParseList(reader, candidate.Find(OpcodeList3), list: 3,
                    warps, vignettes, ref trim, imageWidth, imageHeight, out error))
            {
                return Unsupported(error!);
            }

            if (warps.Count == 0 && vignettes.Count == 0)
            {
                return LensPrescriptionReadResult.None;
            }

            var outputWindow = trim ?? frame.DefaultCrop;
            if (!frame.Source.IsValid || !outputWindow.IsValid ||
                outputWindow.Left < frame.Source.Left || outputWindow.Top < frame.Source.Top ||
                outputWindow.Right > frame.Source.Right || outputWindow.Bottom > frame.Source.Bottom)
            {
                return Unsupported("The DNG correction bounds cannot be mapped to the visible frame.");
            }
            var lensName = ifds.Select(ifd => ifd.Find(LensModel))
                .FirstOrDefault(entry => entry != null) is { } lens
                ? reader.ReadString(lens)
                : null;
            return LensPrescriptionReadResult.Available(new LensPrescription(
                LensPrescriptionSource.DngOpcode,
                string.IsNullOrWhiteSpace(lensName) ? null : lensName,
                warps,
                vignettes,
                frame.Source,
                outputWindow));
        }
        catch (Exception exception) when (exception is IOException or
            OverflowException or DcpProfileException or ArgumentException)
        {
            return LensPrescriptionReadResult.Reject(
                LensPrescriptionStatus.Invalid,
                $"Invalid DNG lens prescription: {exception.Message}");
        }
    }

    private static IReadOnlyList<TiffIfd> ReadIfds(DcpTiffReader reader)
    {
        var result = reader.ReadIfdChain().ToList();
        var visited = new HashSet<uint>();
        for (var index = 0; index < result.Count && result.Count < 32; index++)
        {
            if (result[index].Find(SubIfds) is not { } subIfds) continue;
            foreach (var offset in reader.ReadUnsignedValues(subIfds))
            {
                if (offset <= uint.MaxValue && visited.Add((uint)offset))
                {
                    result.Add(reader.ReadIfdAtOffset((uint)offset));
                }
            }
        }
        return result;
    }

    private static DngFrameWindows ReadFrame(
        DcpTiffReader reader,
        TiffIfd ifd,
        out int imageWidth,
        out int imageHeight)
    {
        var width = ReadSingle(reader, ifd.Find(ImageWidth));
        var height = ReadSingle(reader, ifd.Find(ImageLength));
        imageWidth = width is > 1 and <= int.MaxValue ? (int)width : 0;
        imageHeight = height is > 1 and <= int.MaxValue ? (int)height : 0;
        if (imageWidth == 0 || imageHeight == 0)
            return new DngFrameWindows(LensFrameWindow.Full, LensFrameWindow.Full);

        var active = ifd.Find(ActiveArea) is { } activeEntry
            ? reader.ReadUnsignedValues(activeEntry)
            : [0d, 0d, height, width];
        if (active.Length != 4) return default;
        var top = active[0];
        var left = active[1];
        var bottom = active[2];
        var right = active[3];
        var source = new LensFrameWindow(
            left / width,
            top / height,
            right / width,
            bottom / height);
        var defaultCrop = source;
        var originEntry = ifd.Find(DefaultCropOrigin);
        var sizeEntry = ifd.Find(DefaultCropSize);
        if ((originEntry == null) != (sizeEntry == null)) return default;
        if (originEntry != null && sizeEntry != null)
        {
            var origin = reader.ReadNumericValues(originEntry);
            var size = reader.ReadNumericValues(sizeEntry);
            if (origin.Length != 2 || size.Length != 2) return default;
            left += origin[0];
            top += origin[1];
            right = left + size[0];
            bottom = top + size[1];
            defaultCrop = new LensFrameWindow(
                left / width,
                top / height,
                right / width,
                bottom / height);
        }
        return new DngFrameWindows(source, defaultCrop);
    }

    private static double ReadSingle(DcpTiffReader reader, TiffEntry? entry)
    {
        if (entry == null) return 0;
        var values = reader.ReadNumericValues(entry);
        return values.Length == 1 ? values[0] : 0;
    }

    private static bool ParseList(
        DcpTiffReader reader,
        TiffEntry? entry,
        int list,
        List<LensWarp> warps,
        List<LensVignette> vignettes,
        ref LensFrameWindow? trim,
        int imageWidth,
        int imageHeight,
        out string? error)
    {
        error = null;
        if (entry == null) return true;
        var data = reader.ReadValue(entry, MaxOpcodeBytes);
        var offset = 0;
        var count = checked((int)ReadUInt32(data, ref offset));
        if (count > MaxOpcodes)
        {
            error = "The DNG opcode list exceeds the supported count.";
            return false;
        }
        var sawTrimBounds = false;
        for (var index = 0; index < count; index++)
        {
            var id = ReadUInt32(data, ref offset);
            var version = ReadUInt32(data, ref offset);
            var flags = ReadUInt32(data, ref offset);
            var size = checked((int)ReadUInt32(data, ref offset));
            if (size < 0 || size > data.Length - offset)
            {
                throw new ArgumentException("An opcode payload exceeds its list.");
            }
            var payload = data.AsSpan(offset, size);
            offset += size;
            var optional = (flags & 1) != 0;
            if (version > 0x01070100)
            {
                if (optional) continue;
                error = $"Mandatory opcode {id} requires a newer DNG version.";
                return false;
            }
            if (list == 1)
            {
                if (optional) continue;
                error = "Mandatory OpcodeList1 operations cannot be applied after demosaic.";
                return false;
            }
            if (id == 1 && list == 2)
            {
                error = "OpcodeList2 geometry cannot be applied after demosaic.";
                return false;
            }
            if (id == 1 && list == 3)
            {
                if (sawTrimBounds)
                {
                    error = "TrimBounds must follow geometry and vignetting opcodes.";
                    return false;
                }
                warps.Add(ParseWarp(payload));
            }
            else if (id == 3)
            {
                if (sawTrimBounds)
                {
                    error = "TrimBounds must follow geometry and vignetting opcodes.";
                    return false;
                }
                vignettes.Add(ParseVignette(payload));
            }
            else if (id == 6 && list == 3)
            {
                trim = ParseTrim(payload, imageWidth, imageHeight);
                sawTrimBounds = true;
            }
            else if (!optional)
            {
                error = $"Mandatory opcode {id} is not supported.";
                return false;
            }
        }
        if (offset != data.Length)
        {
            throw new ArgumentException("The opcode list has trailing bytes.");
        }
        return true;
    }

    private static LensWarp ParseWarp(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        var planes = checked((int)ReadUInt32(data, ref offset));
        if (planes is not (1 or 3) || data.Length != 20 + planes * 48)
        {
            throw new ArgumentException("WarpRectilinear must contain one or three coefficient sets.");
        }
        var coefficients = new LensWarpCoefficients[planes];
        for (var plane = 0; plane < planes; plane++)
        {
            coefficients[plane] = new LensWarpCoefficients(
                ReadDouble(data, ref offset), ReadDouble(data, ref offset),
                ReadDouble(data, ref offset), ReadDouble(data, ref offset),
                ReadDouble(data, ref offset), ReadDouble(data, ref offset));
            if (!coefficients[plane].IsFinite)
                throw new ArgumentException("WarpRectilinear contains a non-finite coefficient.");
        }
        var centerX = ReadDouble(data, ref offset);
        var centerY = ReadDouble(data, ref offset);
        if (!double.IsFinite(centerX) || !double.IsFinite(centerY) ||
            centerX is < 0 or > 1 || centerY is < 0 or > 1)
            throw new ArgumentException("WarpRectilinear has an invalid optical center.");
        return new LensWarp(coefficients, centerX, centerY);
    }

    private static LensVignette ParseVignette(ReadOnlySpan<byte> data)
    {
        if (data.Length != 56)
            throw new ArgumentException("FixVignetteRadial has an invalid payload size.");
        var offset = 0;
        var result = new LensVignette(
            ReadDouble(data, ref offset), ReadDouble(data, ref offset),
            ReadDouble(data, ref offset), ReadDouble(data, ref offset),
            ReadDouble(data, ref offset), ReadDouble(data, ref offset),
            ReadDouble(data, ref offset));
        if (!result.IsFinite || result.CenterX is < 0 or > 1 ||
            result.CenterY is < 0 or > 1)
            throw new ArgumentException("FixVignetteRadial contains invalid values.");
        return result;
    }

    private static LensFrameWindow ParseTrim(
        ReadOnlySpan<byte> data,
        int imageWidth,
        int imageHeight)
    {
        if (data.Length != 16)
            throw new ArgumentException("TrimBounds has an invalid payload size.");
        var offset = 0;
        var top = ReadUInt32(data, ref offset);
        var left = ReadUInt32(data, ref offset);
        var bottom = ReadUInt32(data, ref offset);
        var right = ReadUInt32(data, ref offset);
        if (bottom <= top || right <= left)
            throw new ArgumentException("TrimBounds is empty.");
        if (imageWidth <= 0 || imageHeight <= 0)
            throw new ArgumentException("TrimBounds cannot be mapped without image dimensions.");
        return new LensFrameWindow(
            left / (double)imageWidth,
            top / (double)imageHeight,
            right / (double)imageWidth,
            bottom / (double)imageHeight);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        if (data.Length - offset < 4) throw new ArgumentException("An opcode list is truncated.");
        var value = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
        offset += 4;
        return value;
    }

    private static double ReadDouble(ReadOnlySpan<byte> data, ref int offset)
    {
        if (data.Length - offset < 8) throw new ArgumentException("An opcode payload is truncated.");
        var bits = BinaryPrimitives.ReadInt64BigEndian(data.Slice(offset, 8));
        offset += 8;
        return BitConverter.Int64BitsToDouble(bits);
    }

    private static LensPrescriptionReadResult Unsupported(string message) =>
        LensPrescriptionReadResult.Reject(LensPrescriptionStatus.Unsupported, message);

    private readonly record struct DngFrameWindows(
        LensFrameWindow Source,
        LensFrameWindow DefaultCrop);
}
