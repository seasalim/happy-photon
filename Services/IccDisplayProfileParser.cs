using System.Buffers.Binary;
using System.Text;

namespace HappyPhoton.Services;

internal enum IccDisplayProfileKind
{
    MatrixTrc,
    Srgb,
    LutBased,
    Mhc2,
}

internal sealed record IccDisplayProfile(
    string Name,
    IccDisplayProfileKind Kind,
    Matrix3 Colorants,
    ToneCurve RedCurve,
    ToneCurve GreenCurve,
    ToneCurve BlueCurve);

internal static class IccDisplayProfileParser
{
    internal static IccDisplayProfile Parse(ReadOnlySpan<byte> data, string fallbackName)
    {
        if (data.Length < 132 || Signature(data, 36) != "acsp" ||
            Signature(data, 16) != "RGB " || Signature(data, 20) != "XYZ ")
        {
            throw new InvalidDataException("Not an RGB display ICC profile.");
        }

        var tags = ReadTags(data);
        var name = ReadDescription(data, tags) ?? fallbackName;
        if (tags.ContainsKey("MHC2"))
        {
            return Unsupported(name, IccDisplayProfileKind.Mhc2);
        }
        if (tags.Keys.Any(IsLutTag))
        {
            return Unsupported(name, IccDisplayProfileKind.LutBased);
        }

        var colorants = new Matrix3(
            ReadXyz(data, Required(tags, "rXYZ")),
            ReadXyz(data, Required(tags, "gXYZ")),
            ReadXyz(data, Required(tags, "bXYZ")));
        _ = ReadXyz(data, Required(tags, "wtpt"));
        var red = ReadCurve(data, Required(tags, "rTRC"));
        var green = ReadCurve(data, Required(tags, "gTRC"));
        var blue = ReadCurve(data, Required(tags, "bTRC"));
        if (!colorants.IsFinite || Math.Abs(colorants.Determinant) < 1e-8)
        {
            throw new InvalidDataException("The ICC colorant matrix is singular.");
        }

        var kind = IsSrgbShaped(colorants, red, green, blue)
            ? IccDisplayProfileKind.Srgb
            : IccDisplayProfileKind.MatrixTrc;
        return new(name, kind, colorants, red, green, blue);
    }

    private static Dictionary<string, Tag> ReadTags(ReadOnlySpan<byte> data)
    {
        var count = checked((int)ReadUInt32(data, 128));
        if (count > 4096 || 132L + count * 12L > data.Length)
        {
            throw new InvalidDataException("The ICC tag table is invalid.");
        }

        var tags = new Dictionary<string, Tag>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var entry = 132 + index * 12;
            var signature = Signature(data, entry);
            var offset = checked((int)ReadUInt32(data, entry + 4));
            var length = checked((int)ReadUInt32(data, entry + 8));
            if (offset < 0 || length < 8 || (long)offset + length > data.Length)
            {
                throw new InvalidDataException($"ICC tag {signature} is out of bounds.");
            }
            tags.TryAdd(signature, new(offset, length));
        }
        return tags;
    }

    private static bool IsLutTag(string signature) =>
        signature.Length == 4 && signature[3] is >= '0' and <= '3' &&
        signature[..3] is "A2B" or "B2A" or "D2B" or "B2D";

    private static IccDisplayProfile Unsupported(
        string name,
        IccDisplayProfileKind kind) =>
        new(name, kind, Matrix3.Identity, ToneCurve.Srgb, ToneCurve.Srgb, ToneCurve.Srgb);

    private static Tag Required(Dictionary<string, Tag> tags, string signature) =>
        tags.TryGetValue(signature, out var tag)
            ? tag
            : throw new InvalidDataException($"ICC tag {signature} is missing.");

    private static (double X, double Y, double Z) ReadXyz(
        ReadOnlySpan<byte> data,
        Tag tag)
    {
        if (tag.Length < 20 || Signature(data, tag.Offset) != "XYZ ")
            throw new InvalidDataException("An ICC XYZ tag is invalid.");
        return (
            ReadFixed(data, tag.Offset + 8),
            ReadFixed(data, tag.Offset + 12),
            ReadFixed(data, tag.Offset + 16));
    }

    private static ToneCurve ReadCurve(ReadOnlySpan<byte> data, Tag tag)
    {
        var type = Signature(data, tag.Offset);
        if (type == "curv")
        {
            var count = checked((int)ReadUInt32(data, tag.Offset + 8));
            if (count == 0) return ToneCurve.Identity;
            if (count == 1)
            {
                EnsureTagLength(tag, 14);
                return ToneCurve.Gamma(ReadUInt16(data, tag.Offset + 12) / 256.0);
            }
            EnsureTagLength(tag, checked(12 + count * 2));
            var samples = new double[count];
            for (var index = 0; index < count; index++)
                samples[index] = ReadUInt16(data, tag.Offset + 12 + index * 2) / 65535.0;
            return ToneCurve.Sampled(samples);
        }

        if (type != "para")
            throw new InvalidDataException($"ICC curve type {type} is unsupported.");
        EnsureTagLength(tag, 12);
        var function = ReadUInt16(data, tag.Offset + 8);
        var parameterCount = function switch
        {
            0 => 1,
            1 => 3,
            2 => 4,
            3 => 5,
            4 => 7,
            _ => throw new InvalidDataException("The ICC parametric curve is unsupported.")
        };
        EnsureTagLength(tag, 12 + parameterCount * 4);
        var parameters = new double[parameterCount];
        for (var index = 0; index < parameters.Length; index++)
            parameters[index] = ReadFixed(data, tag.Offset + 12 + index * 4);
        return ToneCurve.Parametric(function, parameters);
    }

    private static bool IsSrgbShaped(
        Matrix3 colorants,
        ToneCurve red,
        ToneCurve green,
        ToneCurve blue)
    {
        const double matrixTolerance = 0.015;
        if (!colorants.NearlyEquals(Matrix3.SrgbToXyzD50, matrixTolerance)) return false;
        for (var index = 0; index <= 32; index++)
        {
            var value = index / 32.0;
            var expected = ToneCurve.Srgb.Evaluate(value);
            if (Math.Abs(red.Evaluate(value) - expected) > 0.02 ||
                Math.Abs(green.Evaluate(value) - expected) > 0.02 ||
                Math.Abs(blue.Evaluate(value) - expected) > 0.02)
                return false;
        }
        return true;
    }

    private static string? ReadDescription(
        ReadOnlySpan<byte> data,
        Dictionary<string, Tag> tags)
    {
        if (!tags.TryGetValue("desc", out var tag)) return null;
        var type = Signature(data, tag.Offset);
        if (type == "mluc" && tag.Length >= 28 && ReadUInt32(data, tag.Offset + 8) > 0)
        {
            var length = checked((int)ReadUInt32(data, tag.Offset + 20));
            var offset = checked((int)ReadUInt32(data, tag.Offset + 24));
            if (length > 0 && offset >= 0 && offset + length <= tag.Length)
                return Encoding.BigEndianUnicode.GetString(data.Slice(tag.Offset + offset, length));
        }
        if (type == "desc" && tag.Length >= 13)
        {
            var length = checked((int)ReadUInt32(data, tag.Offset + 8));
            if (length > 1 && 12 + length <= tag.Length)
                return Encoding.ASCII.GetString(data.Slice(tag.Offset + 12, length - 1));
        }
        return null;
    }

    private static void EnsureTagLength(Tag tag, int required)
    {
        if (tag.Length < required) throw new InvalidDataException("An ICC curve tag is truncated.");
    }

    private static string Signature(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset + 4 > data.Length)
            throw new InvalidDataException("The ICC profile is truncated.");
        return Encoding.ASCII.GetString(data.Slice(offset, 4));
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

    private static double ReadFixed(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4)) / 65536.0;

    private readonly record struct Tag(int Offset, int Length);
}

internal sealed class ToneCurve
{
    private readonly Func<double, double> _evaluate;

    private ToneCurve(Func<double, double> evaluate) => _evaluate = evaluate;

    internal static ToneCurve Identity { get; } = new(value => value);
    internal static ToneCurve Srgb { get; } = new(value => value <= 0.04045
        ? value / 12.92
        : Math.Pow((value + 0.055) / 1.055, 2.4));

    internal double Evaluate(double value) =>
        Math.Clamp(_evaluate(Math.Clamp(value, 0, 1)), 0, 1);

    internal static ToneCurve Gamma(double gamma)
    {
        if (!double.IsFinite(gamma) || gamma <= 0) throw new InvalidDataException("Invalid ICC gamma.");
        return new(value => Math.Pow(value, gamma));
    }

    internal static ToneCurve Sampled(double[] samples)
    {
        if (samples.Length < 2 || samples.Zip(samples.Skip(1)).Any(pair => pair.First > pair.Second))
            throw new InvalidDataException("The ICC curve table is not monotonic.");
        return new(value =>
        {
            var position = value * (samples.Length - 1);
            var lower = Math.Min((int)position, samples.Length - 2);
            var fraction = position - lower;
            return samples[lower] + (samples[lower + 1] - samples[lower]) * fraction;
        });
    }

    internal static ToneCurve Parametric(ushort function, double[] p) => new(value =>
        function switch
        {
            0 => Math.Pow(value, p[0]),
            1 => value >= -p[2] / p[1] ? Math.Pow(p[1] * value + p[2], p[0]) : 0,
            2 => value >= -p[2] / p[1] ? Math.Pow(p[1] * value + p[2], p[0]) + p[3] : p[3],
            3 => value >= p[4] ? Math.Pow(p[1] * value + p[2], p[0]) : p[3] * value,
            4 => value >= p[4] ? Math.Pow(p[1] * value + p[2], p[0]) + p[5] : p[3] * value + p[6],
            _ => value,
        });
}
