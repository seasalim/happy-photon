using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HappyPhoton.Services;

internal sealed record DcpExternalSnapshot(byte[] Bytes, string ContentHash);

internal sealed class DcpProfileReader
{
    internal const int MaximumExternalBytes = 4 * 1024 * 1024;
    private const int MaximumTableEntries = 262_144;

    // Adobe Digital Negative Specification 1.7.1, tables 11-20:
    // https://helpx.adobe.com/camera-raw/digital-negative.html
    private const ushort UniqueCameraModel = 50708;
    private const ushort ColorMatrix1 = 50721;
    private const ushort ColorMatrix2 = 50722;
    private const ushort CameraCalibration1 = 50723;
    private const ushort CameraCalibration2 = 50724;
    private const ushort ReductionMatrix1 = 50725;
    private const ushort ReductionMatrix2 = 50726;
    private const ushort AnalogBalance = 50727;
    private const ushort AsShotNeutral = 50728;
    private const ushort CalibrationIlluminant1 = 50778;
    private const ushort CalibrationIlluminant2 = 50779;
    private const ushort CameraCalibrationSignature = 50931;
    private const ushort ProfileCalibrationSignature = 50932;
    private const ushort ExtraCameraProfiles = 50933;
    private const ushort ProfileName = 50936;
    private const ushort ProfileHueSatMapDims = 50937;
    private const ushort ProfileHueSatMapData1 = 50938;
    private const ushort ProfileHueSatMapData2 = 50939;
    private const ushort ProfileEmbedPolicy = 50941;
    private const ushort ForwardMatrix1 = 50964;
    private const ushort ForwardMatrix2 = 50965;
    private const ushort ProfileHueSatMapEncoding = 51107;
    private static readonly ushort[] UnsupportedProfileTags =
    [
        52529, // CalibrationIlluminant3
        52530, // CameraCalibration3
        52531, // ColorMatrix3
        52532, // ForwardMatrix3
        52533, // IlluminantData1
        52534, // IlluminantData2
        52535, // IlluminantData3
        52537, // ProfileHueSatMapData3
        52538, // ReductionMatrix3
        52551  // ProfileDynamicRange
    ];

    internal DcpExternalSnapshot ReadExternalSnapshot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = ValidateExternalFile(path);

        var bytes = GC.AllocateUninitializedArray<byte>((int)info.Length);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            4096,
            FileOptions.SequentialScan);
        stream.ReadExactly(bytes);
        return new DcpExternalSnapshot(bytes, Hash(bytes));
    }

    internal string? ReadExternalUniqueCameraModel(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var reader = DcpTiffReader.Open(path);
        if (reader.Length is <= 0 or > MaximumExternalBytes)
        {
            throw new DcpProfileException(
                DcpProfileErrorCode.TooLarge,
                "Camera profiles must be between 1 byte and 4 MiB.");
        }
        var ifd = reader.ReadFirstIfd();
        return NullIfEmpty(ReadOptionalString(reader, ifd.Find(UniqueCameraModel)));
    }

    internal DcpProfile ParseExternal(
        DcpExternalSnapshot snapshot,
        string fallbackName)
    {
        using var reader = DcpTiffReader.Open(snapshot.Bytes);
        var chain = reader.ReadIfdChain();
        if (chain.Count == 0)
        {
            throw new DcpProfileException(
                DcpProfileErrorCode.InvalidContainer,
                "The camera profile contains no IFD.");
        }
        var profile = ParseProfile(
            reader,
            chain[0],
            fallbackName,
            snapshot.ContentHash);
        return profile;
    }

    internal IReadOnlyList<DcpProfile> ReadEmbeddedProfiles(string dngPath)
    {
        using var rootReader = DcpTiffReader.Open(dngPath);
        var root = rootReader.ReadFirstIfd();
        var profiles = new List<DcpProfile>();
        if (root.Find(ColorMatrix1) != null)
        {
            profiles.Add(ParseProfile(
                rootReader,
                root,
                "Embedded profile",
                contentHash: null));
        }

        if (root.Find(ExtraCameraProfiles) is { } extras)
        {
            if (extras.Count > 32)
            {
                throw Unsupported("A DNG contains too many embedded camera profiles.");
            }
            foreach (var offset in rootReader.ReadLongs(
                extras,
                checked((int)extras.Count)))
            {
                profiles.Add(ParseProfile(
                    rootReader,
                    rootReader.ReadIfdAtOffset(offset),
                    "Embedded profile",
                    contentHash: null));
            }
        }
        return profiles;
    }

    internal DcpCameraData ReadCameraData(string dngPath)
    {
        using var reader = DcpTiffReader.Open(dngPath);
        var ifd = reader.ReadFirstIfd();
        return new DcpCameraData(
            ReadVector(reader, ifd.Find(AnalogBalance), allowUnsigned: true),
            ReadMatrix(reader, ifd.Find(CameraCalibration1)),
            ReadMatrix(reader, ifd.Find(CameraCalibration2)),
            ReadMatrix(reader, ifd.Find(ReductionMatrix1)),
            ReadMatrix(reader, ifd.Find(ReductionMatrix2)),
            ReadVector(
                reader,
                ifd.Find(AsShotNeutral),
                allowUnsigned: true,
                allowShort: true),
            ReadOptionalString(reader, ifd.Find(CameraCalibrationSignature)));
    }

    private static FileInfo ValidateExternalFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new DcpProfileException(
                DcpProfileErrorCode.Missing,
                "The selected camera profile no longer exists.");
        }
        if (info.Length is <= 0 or > MaximumExternalBytes)
        {
            throw new DcpProfileException(
                DcpProfileErrorCode.TooLarge,
                "Camera profiles must be between 1 byte and 4 MiB.");
        }
        return info;
    }

    private static DcpProfile ParseProfile(
        DcpTiffReader reader,
        TiffIfd ifd,
        string fallbackName,
        string? contentHash)
    {
        RejectUnsupportedVariants(ifd);
        var cm1 = ReadRequiredMatrix(reader, ifd, ColorMatrix1);
        var cm2 = ReadMatrix(reader, ifd.Find(ColorMatrix2));
        var illuminant1 = ifd.Find(CalibrationIlluminant1) is { } i1
            ? reader.ReadShort(i1)
            : 0;
        var illuminant2 = ifd.Find(CalibrationIlluminant2) is { } i2
            ? reader.ReadShort(i2)
            : (int?)null;
        _ = DcpMatrixCalculator.GetIlluminantTemperature(illuminant1);
        if (illuminant2.HasValue)
        {
            _ = DcpMatrixCalculator.GetIlluminantTemperature(illuminant2.Value);
        }
        if ((cm2 == null) != !illuminant2.HasValue)
        {
            throw Missing("ColorMatrix2 and CalibrationIlluminant2 must appear together.");
        }

        var fm1 = ReadMatrix(reader, ifd.Find(ForwardMatrix1));
        var fm2 = ReadMatrix(reader, ifd.Find(ForwardMatrix2));
        if (fm2 != null && (fm1 == null || cm2 == null) ||
            cm2 != null && (fm1 == null) != (fm2 == null))
        {
            throw Unsupported(
                "Dual-illuminant profiles must provide both ForwardMatrix tags or neither.");
        }

        var (h, s, v, table1, table2, encodeValue) = ReadHueSatMap(reader, ifd, cm2 != null);
        var embedPolicy = ifd.Find(ProfileEmbedPolicy) is { } policy
            ? checked((int)reader.ReadLong(policy))
            : 0;
        if (embedPolicy is < 0 or > 3)
        {
            throw Unsupported($"ProfileEmbedPolicy {embedPolicy} is not supported.");
        }

        var name = ReadOptionalString(reader, ifd.Find(ProfileName));
        if (string.IsNullOrWhiteSpace(name)) name = fallbackName;
        var profile = new DcpProfile(
            name,
            NullIfEmpty(ReadOptionalString(reader, ifd.Find(UniqueCameraModel))),
            cm1,
            cm2,
            fm1,
            fm2,
            illuminant1,
            illuminant2,
            ReadOptionalString(reader, ifd.Find(ProfileCalibrationSignature)),
            embedPolicy,
            h,
            s,
            v,
            encodeValue,
            table1,
            table2,
            contentHash ?? string.Empty);
        return profile with
        {
            ContentHash = contentHash ?? ComputeProfileFingerprint(profile)
        };
    }

    private static (int H, int S, int V, float[]? Table1,
        float[]? Table2, bool EncodeValue) ReadHueSatMap(
        DcpTiffReader reader,
        TiffIfd ifd,
        bool dualIlluminant)
    {
        var dimsEntry = ifd.Find(ProfileHueSatMapDims);
        var data1 = ifd.Find(ProfileHueSatMapData1);
        var data2 = ifd.Find(ProfileHueSatMapData2);
        if (dimsEntry == null && data1 == null && data2 == null)
        {
            return (0, 0, 0, null, null, false);
        }
        if (dimsEntry == null || data1 == null)
        {
            throw Missing("HueSatMap dimensions and Data1 are required together.");
        }

        var dims = reader.ReadLongs(dimsEntry, 3);
        if (dims[0] < 1 || dims[1] < 2 || dims[2] < 1 ||
            dims.Any(value => value > int.MaxValue))
        {
            throw new DcpProfileException(
                DcpProfileErrorCode.InvalidDimensions,
                "ProfileHueSatMapDims is outside the supported shape.");
        }
        var h = (int)dims[0];
        var s = (int)dims[1];
        var v = (int)dims[2];
        var entries = checked((long)h * s * v);
        if (entries > MaximumTableEntries)
        {
            throw new DcpProfileException(
                DcpProfileErrorCode.TooLarge,
                "The HueSatMap exceeds the supported table cap.");
        }
        if (data2 != null && !dualIlluminant)
        {
            throw Unsupported("HueSatMapData2 requires a second calibration.");
        }

        var count = checked((int)entries * 3);
        var table1 = reader.ReadFloats(data1, count);
        var table2 = data2 == null ? null : reader.ReadFloats(data2, count);
        ValidateHueSatTable(table1, s);
        if (table2 != null) ValidateHueSatTable(table2, s);

        var encoding = v > 1 &&
            ifd.Find(ProfileHueSatMapEncoding) is { } encodingEntry
            ? reader.ReadLong(encodingEntry)
            : 0;
        if (v > 1 && encoding > 1)
        {
            throw Unsupported($"ProfileHueSatMapEncoding {encoding} is not supported.");
        }
        return (h, s, v, table1, table2, v > 1 && encoding == 1);
    }

    private static void ValidateHueSatTable(
        float[] table,
        int saturationDivisions)
    {
        for (var entry = 0; entry < table.Length / 3; entry++)
        {
            var saturationIndex = entry % saturationDivisions;
            var hueShift = table[entry * 3];
            var saturationScale = table[entry * 3 + 1];
            var valueScale = table[entry * 3 + 2];
            if (!float.IsFinite(hueShift) || !float.IsFinite(saturationScale) ||
                !float.IsFinite(valueScale) || saturationScale < 0 || valueScale < 0)
            {
                throw Unsupported("The HueSatMap contains invalid deltas.");
            }
            if (saturationIndex == 0 && Math.Abs(valueScale - 1) > 1e-6)
            {
                throw Unsupported(
                    "Zero-saturation HueSatMap entries must preserve value.");
            }
        }
    }

    private static double[,] ReadRequiredMatrix(
        DcpTiffReader reader,
        TiffIfd ifd,
        ushort tag) => ReadMatrix(reader, ifd.Find(tag)) ??
        throw Missing($"Mandatory DCP tag {tag} is missing.");

    private static double[,]? ReadMatrix(DcpTiffReader reader, TiffEntry? entry)
    {
        if (entry == null) return null;
        var values = reader.ReadRationals(entry, 9);
        if (values.Any(value => !double.IsFinite(value)))
        {
            throw Unsupported($"Matrix tag {entry.Tag} contains a non-finite value.");
        }
        var matrix = new double[3, 3];
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            matrix[row, column] = values[row * 3 + column];
        }
        return matrix;
    }

    private static double[]? ReadVector(
        DcpTiffReader reader,
        TiffEntry? entry,
        bool allowUnsigned,
        bool allowShort = false)
    {
        if (entry == null) return null;
        var result = allowShort && entry.Type == 3
            ? reader.ReadShorts(entry, 3).Select(value => (double)value).ToArray()
            : reader.ReadRationals(entry, 3, allowUnsigned);
        if (result.Any(value => !double.IsFinite(value) || value <= 0))
        {
            throw Unsupported($"Vector tag {entry.Tag} must contain positive values.");
        }
        return result;
    }

    private static string ReadOptionalString(
        DcpTiffReader reader,
        TiffEntry? entry) => entry == null ? string.Empty : reader.ReadString(entry);

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static void RejectUnsupportedVariants(TiffIfd ifd)
    {
        var tag = UnsupportedProfileTags.FirstOrDefault(
            candidate => ifd.Find(candidate) != null);
        if (tag != 0)
        {
            throw Unsupported(
                $"DCP tag {tag} belongs to an unsupported custom, triple-illuminant, or HDR profile variant.");
        }
    }

    internal static string ComputeProfileFingerprint(DcpProfile profile)
    {
        var builder = new StringBuilder();
        Append(builder, profile.CalibrationIlluminant1);
        Append(builder, profile.CalibrationIlluminant2);
        Append(builder, profile.CalibrationSignature);
        Append(builder, profile.HueDivisions);
        Append(builder, profile.SaturationDivisions);
        Append(builder, profile.ValueDivisions);
        Append(builder, profile.EncodeValueAsSrgb);
        Append(builder, profile.ColorMatrix1);
        Append(builder, profile.ColorMatrix2);
        Append(builder, profile.ForwardMatrix1);
        Append(builder, profile.ForwardMatrix2);
        Append(builder, profile.HueSatTable1);
        Append(builder, profile.HueSatTable2);
        return Hash(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static void Append(StringBuilder value, object? item)
    {
        if (item is System.Collections.IEnumerable sequence && item is not string)
        {
            foreach (var member in sequence) Append(value, member);
        }
        else if (item is double doubleValue)
        {
            value.Append(doubleValue.ToString("R", CultureInfo.InvariantCulture));
        }
        else if (item is float floatValue)
        {
            value.Append(floatValue.ToString("R", CultureInfo.InvariantCulture));
        }
        else if (item is IFormattable formatted)
        {
            value.Append(formatted.ToString(null, CultureInfo.InvariantCulture));
        }
        else
        {
            value.Append(item?.ToString() ?? "null");
        }
        value.Append('|');
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static DcpProfileException Missing(string message) => new(
        DcpProfileErrorCode.MissingMandatoryTag,
        message);

    private static DcpProfileException Unsupported(string message) => new(
        DcpProfileErrorCode.UnsupportedVariant,
        message);
}
