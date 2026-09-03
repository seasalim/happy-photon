using System.Buffers.Binary;
using System.Text;

namespace HappyPhoton.Tests;

internal static class SoftProofIccFixtureWriter
{
    private static readonly double[,] D65ToD50 =
    {
        { 1.04788603, 0.02291869, -0.05021606 },
        { 0.02958179, 0.99048358, -0.01707873 },
        { -0.00925190, 0.01507256, 0.75167814 }
    };

    private static readonly double[,] SrgbToXyzD65 =
    {
        { 0.4123907992659595, 0.3575843393838780, 0.1804807884018343 },
        { 0.2126390058715104, 0.7151686787677560, 0.0721923153607337 },
        { 0.0193308187155918, 0.1191947797946259, 0.9505321522496607 }
    };

    private static readonly double[,] DisplayP3ToXyzD65 =
    {
        { 0.4865709486482162, 0.2656676931690931, 0.1982172852343625 },
        { 0.2289745640697488, 0.6917385218365064, 0.0792869140937450 },
        { 0.0000000000000000, 0.0451133818589026, 1.0439443689009760 }
    };

    internal static IReadOnlyDictionary<string, byte[]> CreateProfiles() =>
        new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["softproof-srgb.icc"] = CreateProfile(
                "SoftProof synthetic sRGB",
                SrgbToXyzD65,
                CreateSrgbCurve()),
            ["softproof-p3-gamma22.icc"] = CreateProfile(
                "SoftProof synthetic Display P3 gamma 2.2",
                DisplayP3ToXyzD65,
                CreateGammaCurve(2.2)),
            ["softproof-p3-curv1024.icc"] = CreateProfile(
                "SoftProof synthetic Display P3 1024-point curve",
                DisplayP3ToXyzD65,
                CreateSampledCurve(2.2, 1024)),
            ["softproof-p3-a2b1.icc"] = CreateProfile(
                "SoftProof synthetic Display P3 with A2B1",
                DisplayP3ToXyzD65,
                CreateGammaCurve(2.2),
                ("A2B1", CreateMarkerTag("mAB "))),
            ["softproof-p3-d2b0.icc"] = CreateProfile(
                "SoftProof synthetic Display P3 with D2B0",
                DisplayP3ToXyzD65,
                CreateGammaCurve(2.2),
                ("D2B0", CreateMarkerTag("mAB "))),
            ["softproof-p3-mhc2.icc"] = CreateProfile(
                "SoftProof synthetic Display P3 with MHC2",
                DisplayP3ToXyzD65,
                CreateGammaCurve(2.2),
                ("MHC2", CreateMarkerTag("MHC2")))
        };

    private static byte[] CreateProfile(
        string description,
        double[,] rgbToXyzD65,
        byte[] curve,
        params (string Signature, byte[] Data)[] extraTags)
    {
        var colorants = Multiply(D65ToD50, rgbToXyzD65);
        var tags = new List<(string Signature, byte[] Data)>
        {
            ("desc", CreateMluc(description)),
            ("cprt", CreateMluc("Copyright 2026 Happy Photon contributors")),
            ("wtpt", CreateXyz(0.9642, 1.0, 0.8249)),
            ("chad", CreateSf32(D65ToD50)),
            ("rXYZ", CreateXyz(colorants[0, 0], colorants[1, 0], colorants[2, 0])),
            ("gXYZ", CreateXyz(colorants[0, 1], colorants[1, 1], colorants[2, 1])),
            ("bXYZ", CreateXyz(colorants[0, 2], colorants[1, 2], colorants[2, 2])),
            ("rTRC", curve),
            ("gTRC", curve),
            ("bTRC", curve)
        };
        tags.AddRange(extraTags);

        const int headerLength = 128;
        var tableLength = 4 + tags.Count * 12;
        var offsets = new int[tags.Count];
        var length = headerLength + tableLength;
        for (var index = 0; index < tags.Count; index++)
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
        WriteUInt16(profile, 26, 9);
        WriteUInt16(profile, 28, 3);
        WriteSignature(profile, 36, "acsp");
        WriteSignature(profile, 40, "MSFT");
        WriteUInt32(profile, 64, 1);
        WriteFixed(profile, 68, 0.9642);
        WriteFixed(profile, 72, 1.0);
        WriteFixed(profile, 76, 0.8249);
        WriteSignature(profile, 80, "HPHP");
        WriteUInt32(profile, headerLength, checked((uint)tags.Count));
        for (var index = 0; index < tags.Count; index++)
        {
            var entry = headerLength + 4 + index * 12;
            WriteSignature(profile, entry, tags[index].Signature);
            WriteUInt32(profile, entry + 4, checked((uint)offsets[index]));
            WriteUInt32(profile, entry + 8, checked((uint)tags[index].Data.Length));
            tags[index].Data.CopyTo(profile, offsets[index]);
        }
        return profile;
    }

    private static byte[] CreateSrgbCurve()
    {
        var data = new byte[40];
        WriteSignature(data, 0, "para");
        WriteUInt16(data, 8, 4);
        var values = new[]
        {
            2.4, 1.0 / 1.055, 0.055 / 1.055,
            1.0 / 12.92, 0.04045, 0.0, 0.0
        };
        for (var index = 0; index < values.Length; index++)
        {
            WriteFixed(data, 12 + index * 4, values[index]);
        }
        return data;
    }

    private static byte[] CreateGammaCurve(double gamma)
    {
        var data = new byte[16];
        WriteSignature(data, 0, "curv");
        WriteUInt32(data, 8, 1);
        WriteUInt16(data, 12, checked((ushort)Math.Round(gamma * 256)));
        return data;
    }

    private static byte[] CreateSampledCurve(double gamma, int count)
    {
        var data = new byte[12 + count * 2];
        WriteSignature(data, 0, "curv");
        WriteUInt32(data, 8, checked((uint)count));
        for (var index = 0; index < count; index++)
        {
            var input = index / (double)(count - 1);
            WriteUInt16(data, 12 + index * 2,
                checked((ushort)Math.Round(Math.Pow(input, gamma) * ushort.MaxValue)));
        }
        return data;
    }

    private static byte[] CreateMarkerTag(string type)
    {
        var data = new byte[16];
        WriteSignature(data, 0, type);
        return data;
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

    private static double[,] Multiply(double[,] left, double[,] right)
    {
        var result = new double[3, 3];
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        for (var index = 0; index < 3; index++)
        {
            result[row, column] += left[row, index] * right[index, column];
        }
        return result;
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
