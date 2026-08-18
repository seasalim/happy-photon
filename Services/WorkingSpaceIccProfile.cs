using System.Buffers.Binary;
using System.Text;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class WorkingSpaceIccProfile
{
    private static readonly double[,] D65ToD50 =
    {
        { 1.04788603, 0.02291869, -0.05021606 },
        { 0.02958179, 0.99048358, -0.01707873 },
        { -0.00925190, 0.01507256, 0.75167814 }
    };

    private static readonly Lazy<IColorProfile> LinearRec2020Profile = new(() =>
        new ColorProfile(CreateBytes(
            "Happy Photon linear Rec.2020 D65",
            RgbColorSpaceMatrices.LinearRec2020ToXyzD65DerivedExact)));

    internal static IColorProfile LinearRec2020 => LinearRec2020Profile.Value;

    private static byte[] CreateBytes(
        string description,
        double[,] rgbToXyzD65)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        var colorants = ChromaticAdaptation.Multiply(D65ToD50, rgbToXyzD65);
        var tags = new (string Signature, byte[] Data)[]
        {
            ("desc", CreateMluc(description)),
            ("cprt", CreateMluc("Copyright 2026 Happy Photon contributors")),
            ("wtpt", CreateXyz(0.9642, 1.0, 0.8249)),
            ("chad", CreateSf32(D65ToD50)),
            ("rXYZ", CreateXyz(colorants[0, 0], colorants[1, 0], colorants[2, 0])),
            ("gXYZ", CreateXyz(colorants[0, 1], colorants[1, 1], colorants[2, 1])),
            ("bXYZ", CreateXyz(colorants[0, 2], colorants[1, 2], colorants[2, 2])),
            ("rTRC", CreateLinearCurve()),
            ("gTRC", CreateLinearCurve()),
            ("bTRC", CreateLinearCurve())
        };

        const int headerLength = 128;
        var tableLength = 4 + tags.Length * 12;
        var offsets = new int[tags.Length];
        var length = headerLength + tableLength;
        for (var index = 0; index < tags.Length; index++)
        {
            length = Align4(length);
            offsets[index] = length;
            length += tags[index].Data.Length;
        }

        var profile = new byte[Align4(length)];
        WriteUInt32(profile, 0, checked((uint)profile.Length));
        WriteSignature(profile, 4, "HPHP");
        WriteUInt32(profile, 8, 0x04300000);
        WriteSignature(profile, 12, "mntr");
        WriteSignature(profile, 16, "RGB ");
        WriteSignature(profile, 20, "XYZ ");
        WriteUInt16(profile, 24, 2026);
        WriteUInt16(profile, 26, 8);
        WriteUInt16(profile, 28, 17);
        WriteUInt16(profile, 30, 0);
        WriteUInt16(profile, 32, 0);
        WriteUInt16(profile, 34, 0);
        WriteSignature(profile, 36, "acsp");
        WriteSignature(profile, 40, "MSFT");
        WriteUInt32(profile, 64, 1);
        WriteFixed(profile, 68, 0.9642);
        WriteFixed(profile, 72, 1.0);
        WriteFixed(profile, 76, 0.8249);
        WriteSignature(profile, 80, "HPHP");
        WriteUInt32(profile, headerLength, checked((uint)tags.Length));
        for (var index = 0; index < tags.Length; index++)
        {
            var entry = headerLength + 4 + index * 12;
            WriteSignature(profile, entry, tags[index].Signature);
            WriteUInt32(profile, entry + 4, checked((uint)offsets[index]));
            WriteUInt32(profile, entry + 8, checked((uint)tags[index].Data.Length));
            tags[index].Data.CopyTo(profile, offsets[index]);
        }

        return profile;
    }

    private static byte[] CreateMluc(string value)
    {
        var text = Encoding.BigEndianUnicode.GetBytes(value);
        var data = new byte[28 + text.Length];
        WriteSignature(data, 0, "mluc");
        WriteUInt32(data, 8, 1);
        WriteUInt32(data, 12, 12);
        data[16] = (byte)'e';
        data[17] = (byte)'n';
        data[18] = (byte)'U';
        data[19] = (byte)'S';
        WriteUInt32(data, 20, checked((uint)text.Length));
        WriteUInt32(data, 24, 28);
        text.CopyTo(data, 28);
        return data;
    }

    private static byte[] CreateXyz(double x, double y, double z)
    {
        var data = new byte[20];
        WriteSignature(data, 0, "XYZ ");
        WriteFixed(data, 8, x);
        WriteFixed(data, 12, y);
        WriteFixed(data, 16, z);
        return data;
    }

    private static byte[] CreateSf32(double[,] matrix)
    {
        var data = new byte[44];
        WriteSignature(data, 0, "sf32");
        var offset = 8;
        foreach (var value in matrix)
        {
            WriteFixed(data, offset, value);
            offset += 4;
        }
        return data;
    }

    private static byte[] CreateLinearCurve()
    {
        var data = new byte[16];
        WriteSignature(data, 0, "curv");
        WriteUInt32(data, 8, 1);
        WriteUInt16(data, 12, 256);
        return data;
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static void WriteSignature(byte[] bytes, int offset, string value) =>
        Encoding.ASCII.GetBytes(value).CopyTo(bytes, offset);

    private static void WriteFixed(byte[] bytes, int offset, double value) =>
        BinaryPrimitives.WriteInt32BigEndian(
            bytes.AsSpan(offset, 4),
            checked((int)Math.Round(value * 65536)));

    private static void WriteUInt16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset, 2), value);

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);
}
