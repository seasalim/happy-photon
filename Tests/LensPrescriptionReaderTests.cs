using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LensPrescriptionReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"happy-photon-lens-{Guid.NewGuid():N}");

    [Fact]
    public void DngReaderParsesWarpVignetteCropAndTrim()
    {
        var path = WriteDng(BuildOpcodeList(
            Opcode(1, WarpPayload()),
            Opcode(3, VignettePayload(0.25)),
            Opcode(6, TrimPayload(20, 10, 80, 90))));

        var result = new DngLensPrescriptionReader().Read(path);

        Assert.True(result.Status == LensPrescriptionStatus.Available, result.Message);
        var prescription = Assert.IsType<LensPrescription>(result.Prescription);
        Assert.Single(prescription.Warps);
        Assert.Single(prescription.Vignettes);
        Assert.True(prescription.HasDistortion);
        Assert.False(prescription.HasChromaticAberration);
        Assert.True(prescription.HasVignetting);
        Assert.Equal(0, prescription.SourceWindow.Left, 10);
        Assert.Equal(0, prescription.SourceWindow.Top, 10);
        Assert.Equal(0.9, prescription.OutputWindow.Right, 10);
        Assert.Equal(0.8, prescription.OutputWindow.Bottom, 10);
    }

    [Fact]
    public void DngReaderRejectsTrimBeforeGeometryButAcceptsGeometryBeforeTrim()
    {
        var accepted = WriteDng(BuildOpcodeList(
            Opcode(1, WarpPayload()),
            Opcode(3, VignettePayload(0.25)),
            Opcode(6, TrimPayload(20, 10, 80, 90))));
        var rejected = WriteDng(BuildOpcodeList(
            Opcode(6, TrimPayload(20, 10, 80, 90)),
            Opcode(1, WarpPayload())));

        Assert.Equal(
            LensPrescriptionStatus.Available,
            new DngLensPrescriptionReader().Read(accepted).Status);
        var result = new DngLensPrescriptionReader().Read(rejected);
        Assert.Equal(LensPrescriptionStatus.Unsupported, result.Status);
        Assert.Contains("TrimBounds", result.Message);
    }

    [Fact]
    public void DngReaderRejectsMandatoryUnknownAndList2Warp()
    {
        var mandatory = WriteDng(BuildOpcodeList(Opcode(99, [])));
        var list2Warp = WriteDng(BuildOpcodeList(),
            BuildOpcodeList(Opcode(1, WarpPayload())));

        var unknownResult = new DngLensPrescriptionReader().Read(mandatory);
        var list2Result = new DngLensPrescriptionReader().Read(list2Warp);

        Assert.Equal(LensPrescriptionStatus.Unsupported, unknownResult.Status);
        Assert.Contains("Mandatory opcode 99", unknownResult.Message);
        Assert.Equal(LensPrescriptionStatus.Unsupported, list2Result.Status);
        Assert.Contains("OpcodeList2 geometry", list2Result.Message);
    }

    [Fact]
    public void DngReaderSkipsOptionalUnknownOpcode()
    {
        var path = WriteDng(BuildOpcodeList(
            Opcode(99, [], flags: 1),
            Opcode(3, VignettePayload(0.1))));

        var result = new DngLensPrescriptionReader().Read(path);

        Assert.Equal(LensPrescriptionStatus.Available, result.Status);
        Assert.Single(result.Prescription!.Vignettes);
    }

    [Fact]
    public void DngReaderSkipsOptionalList1ButRejectsMandatoryList1()
    {
        var optional = WriteDng(
            BuildOpcodeList(Opcode(3, VignettePayload(0.1))),
            list1: BuildOpcodeList(Opcode(4, [], flags: 1)));
        var mandatory = WriteDng(
            BuildOpcodeList(Opcode(3, VignettePayload(0.1))),
            list1: BuildOpcodeList(Opcode(4, [])));

        var optionalResult = new DngLensPrescriptionReader().Read(optional);
        var mandatoryResult = new DngLensPrescriptionReader().Read(mandatory);

        Assert.Equal(LensPrescriptionStatus.Available, optionalResult.Status);
        Assert.Single(optionalResult.Prescription!.Vignettes);
        Assert.Equal(LensPrescriptionStatus.Unsupported, mandatoryResult.Status);
        Assert.Contains("Mandatory OpcodeList1", mandatoryResult.Message);
    }

    [Fact]
    public void ThreePlaneWarpAdvertisesDistortionAndChromaticAberration()
    {
        var red = new LensWarpCoefficients(1, -0.03, 0, 0, 0, 0);
        var green = new LensWarpCoefficients(1, -0.02, 0, 0, 0, 0);
        var blue = new LensWarpCoefficients(1, -0.01, 0, 0, 0, 0);
        var prescription = new LensPrescription(
            LensPrescriptionSource.DngOpcode,
            null,
            [new LensWarp([red, green, blue], 0.5, 0.5)],
            [],
            LensFrameWindow.Full,
            LensFrameWindow.Full);

        Assert.True(prescription.HasDistortion);
        Assert.True(prescription.HasChromaticAberration);
        Assert.True(prescription.Summary.HasDistortion);
        Assert.True(prescription.Summary.HasChromaticAberration);
    }

    [Fact]
    public void FujiReaderPinsCommittedX30Tables()
    {
        var path = GoldenTestPaths.Asset("fujifilm-x30.raf");
        Assert.True(File.Exists(path), $"Missing fixture: {path}");

        var result = new FujiLensPrescriptionReader().Read(path, "X30 lens");

        Assert.Equal(LensPrescriptionStatus.Available, result.Status);
        var tables = Assert.IsType<FujiLensTables>(result.Prescription!.FujiTables);
        var distortion = Assert.IsType<LensRadialTable>(tables.Distortion);
        var ca = Assert.IsType<LensChromaticAberrationTable>(tables.ChromaticAberration);
        var vignetting = Assert.IsType<LensRadialTable>(tables.Vignetting);
        Assert.Equal("23/31/23", tables.Layout);
        Assert.Equal(11, distortion.Radii.Count);
        Assert.Equal(10, ca.Radii.Count);
        Assert.Equal(11, vignetting.Radii.Count);
        Assert.Equal(2500d / 11, distortion.Scale, 10);
        Assert.Equal(0, distortion.Radii[0], 10);
        Assert.Equal(1, distortion.Radii[^1], 10);
        Assert.Equal(250, ca.Scale, 10);
        Assert.Equal(0.1, ca.Radii[0], 10);
        Assert.Equal(1, ca.Radii[^1], 10);
        Assert.True(result.Prescription.Summary.HasDistortion);
        Assert.False(result.Prescription.Summary.HasChromaticAberration);
        Assert.False(result.Prescription.Summary.HasVignetting);

        var fixtureIdentity = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(path)));
        var scalarSnapshot = string.Join("|", EnumerateScalars(tables)
            .Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
        var snapshotHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{fixtureIdentity}|{scalarSnapshot}")));
        Assert.Equal(
            "FD9D0760D259263913014FA73FA5F0B8611100E0C5845721F53E844CCF95586A",
            snapshotHash);
    }

    [Fact]
    public void FujiReaderPinsModernLayoutAndTrailingCaScale()
    {
        var result = new FujiLensPrescriptionReader().Read(
            WriteFujiRaf(19, 29, 19));

        Assert.Equal(LensPrescriptionStatus.Available, result.Status);
        var tables = Assert.IsType<FujiLensTables>(result.Prescription!.FujiTables);
        Assert.Equal("19/29/19", tables.Layout);
        Assert.Equal(9, Assert.IsType<LensRadialTable>(tables.Distortion).Radii.Count);
        Assert.Equal(9, Assert.IsType<LensChromaticAberrationTable>(
            tables.ChromaticAberration).Radii.Count);
        Assert.Equal(9, Assert.IsType<LensRadialTable>(tables.Vignetting).Radii.Count);
        Assert.False(result.Prescription.Summary.HasAny);
        Assert.Contains("not qualified", tables.DistortionMessage);
    }

    [Fact]
    public void FujiReaderRejectsUnqualifiedWholeCountTuple()
    {
        var result = new FujiLensPrescriptionReader().Read(
            WriteFujiRaf(21, 29, 19));

        Assert.Equal(LensPrescriptionStatus.Invalid, result.Status);
        Assert.Contains("21/29/19", result.Message);
    }

    [Fact]
    public void FujiReaderKeepsValidClassesWhenOneTableIsCorrupt()
    {
        var result = new FujiLensPrescriptionReader().Read(
            WriteFujiRaf(19, 29, 19, corruptCaType: true));

        Assert.Equal(LensPrescriptionStatus.Available, result.Status);
        var tables = Assert.IsType<FujiLensTables>(result.Prescription!.FujiTables);
        Assert.NotNull(tables.Distortion);
        Assert.Null(tables.ChromaticAberration);
        Assert.NotNull(tables.Vignetting);
        Assert.Contains("unsupported rational type", tables.ChromaticAberrationMessage);
        Assert.False(result.Prescription.Summary.HasDistortion);
        Assert.False(result.Prescription.Summary.HasChromaticAberration);
        Assert.False(result.Prescription.Summary.HasVignetting);
    }

    [Fact]
    [Trait("Category", "Compatibility")]
    public void FujiReaderParsesOptInLocalCorpus()
    {
        var directory = Environment.GetEnvironmentVariable("HAPPY_PHOTON_RAF_CORPUS");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(directory),
            "Set HAPPY_PHOTON_RAF_CORPUS to the local RAF corpus directory.");
        var paths = Directory.EnumerateFiles(directory!, "*.raf",
            new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).ToArray();
        Assert.NotEmpty(paths);
        foreach (var path in paths)
        {
            var result = new FujiLensPrescriptionReader().Read(path);
            Assert.True(result.Status == LensPrescriptionStatus.Available,
                $"{Path.GetFileName(path)}: {result.Message}");
            Assert.NotNull(result.Prescription!.FujiTables);
        }
    }

    [Fact]
    [Trait("Category", "Compatibility")]
    public void FujiReaderParsesOptInXt50Fixture()
    {
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("HAPPY_PHOTON_COMPAT")),
            "Set HAPPY_PHOTON_COMPAT to run the X-T50 compatibility alarm.");
        var path = Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "artifacts",
            "compatibility-fixtures",
            "fuji-xt50-compressed.RAF");
        Assert.SkipWhen(!File.Exists(path),
            "The opt-in X-T50 compatibility fixture is not cached.");

        var result = new FujiLensPrescriptionReader().Read(path);

        Assert.True(result.Status == LensPrescriptionStatus.Available, result.Message);
        Assert.Equal("19/29/19", result.Prescription!.FujiTables!.Layout);
    }

    private static IEnumerable<double> EnumerateScalars(FujiLensTables tables)
    {
        var distortion = tables.Distortion!;
        var ca = tables.ChromaticAberration!;
        var vignetting = tables.Vignetting!;
        yield return distortion.Scale;
        foreach (var value in distortion.Radii) yield return value;
        foreach (var value in distortion.Values) yield return value;
        yield return ca.Scale;
        foreach (var value in ca.Radii) yield return value;
        foreach (var value in ca.Red) yield return value;
        foreach (var value in ca.Blue) yield return value;
        yield return vignetting.Scale;
        foreach (var value in vignetting.Radii) yield return value;
        foreach (var value in vignetting.Values) yield return value;
    }

    private string WriteFujiRaf(
        int distortionCount,
        int caCount,
        int vignetteCount,
        bool corruptCaType = false)
    {
        Directory.CreateDirectory(_directory);
        const int tiffOffset = 108;
        const int ifdOffset = 8;
        var counts = new[] { distortionCount, caCount, vignetteCount };
        var dataOffset = ifdOffset + 2 + counts.Length * 12 + 4;
        var bytes = new byte[tiffOffset + dataOffset + counts.Sum() * 8];
        "FUJIFILMCCD-RAW "u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(100), tiffOffset);
        bytes[tiffOffset] = (byte)'I';
        bytes[tiffOffset + 1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(tiffOffset + 2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tiffOffset + 4), ifdOffset);
        var ifd = tiffOffset + ifdOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(ifd), 3);
        var data = dataOffset;
        WriteTable(0xf00b, distortionCount, type: 10);
        WriteTable(0xf00f, caCount, type: corruptCaType ? (ushort)5 : (ushort)10);
        WriteTable(0xf010, vignetteCount, type: 10);
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.raf");
        File.WriteAllBytes(path, bytes);
        return path;

        void WriteTable(ushort tag, int count, ushort type)
        {
            var index = tag == 0xf00b ? 0 : tag == 0xf00f ? 1 : 2;
            var entry = ifd + 2 + index * 12;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(entry), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(entry + 2), type);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 4), (uint)count);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 8), (uint)data);
            var values = SyntheticFujiValues(count, tag == 0xf00f);
            for (var valueIndex = 0; valueIndex < count; valueIndex++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    bytes.AsSpan(tiffOffset + data + valueIndex * 8),
                    (int)Math.Round(values[valueIndex] * 1000));
                BinaryPrimitives.WriteInt32LittleEndian(
                    bytes.AsSpan(tiffOffset + data + valueIndex * 8 + 4), 1000);
            }
            data += count * 8;
        }
    }

    private static double[] SyntheticFujiValues(int count, bool ca)
    {
        var knots = count switch { 23 => 11, 31 => 10, 19 or 29 => 9, _ => 9 };
        var values = new double[count];
        values[0] = 100;
        for (var index = 0; index < knots; index++)
            values[1 + index] = (index + 1d) / knots;
        for (var index = 1 + knots; index < count; index++)
            values[index] = ca ? 0.001 : 1;
        if (ca && count == 29) values[^1] = values[0];
        return values;
    }

    private string WriteDng(
        byte[] list3,
        byte[]? list2 = null,
        byte[]? list1 = null)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.dng");
        var entries = 7 + (list2 == null ? 0 : 1) + (list1 == null ? 0 : 1);
        var ifdSize = 2 + entries * 12 + 4;
        var activeOffset = 8 + ifdSize;
        var cropOriginOffset = activeOffset + 16;
        var cropSizeOffset = cropOriginOffset + 8;
        var list3Offset = cropSizeOffset + 8;
        var list2Offset = list3Offset + list3.Length;
        var list1Offset = list2Offset + (list2?.Length ?? 0);
        var bytes = new byte[list1Offset + (list1?.Length ?? 0)];
        bytes[0] = (byte)'I'; bytes[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)entries);
        var entry = 10;
        WriteEntry(256, 4, 1, 100);
        WriteEntry(257, 4, 1, 100);
        WriteEntry(50829, 4, 4, (uint)activeOffset);
        WriteEntry(50719, 4, 2, (uint)cropOriginOffset);
        WriteEntry(50720, 4, 2, (uint)cropSizeOffset);
        WriteEntry(51022, 7, (uint)list3.Length, (uint)list3Offset);
        WriteEntry(42036, 2, 4, 0x00545354); // "TST\0" inline
        if (list2 != null) WriteEntry(51009, 7, (uint)list2.Length, (uint)list2Offset);
        if (list1 != null) WriteEntry(51008, 7, (uint)list1.Length, (uint)list1Offset);
        WriteLongs(activeOffset, 0, 0, 100, 100);
        WriteLongs(cropOriginOffset, 10, 20);
        WriteLongs(cropSizeOffset, 80, 60);
        list3.CopyTo(bytes, list3Offset);
        list2?.CopyTo(bytes, list2Offset);
        list1?.CopyTo(bytes, list1Offset);
        File.WriteAllBytes(path, bytes);
        return path;

        void WriteEntry(ushort tag, ushort type, uint count, uint value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(entry), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(entry + 2), type);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 4), count);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 8), value);
            entry += 12;
        }
        void WriteLongs(int offset, params uint[] values)
        {
            foreach (var value in values)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
                offset += 4;
            }
        }
    }

    private static byte[] BuildOpcodeList(params byte[][] opcodes)
    {
        var bytes = new byte[4 + opcodes.Sum(opcode => opcode.Length)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)opcodes.Length);
        var offset = 4;
        foreach (var opcode in opcodes)
        {
            opcode.CopyTo(bytes, offset);
            offset += opcode.Length;
        }
        return bytes;
    }

    private static byte[] Opcode(uint id, byte[] payload, uint flags = 0)
    {
        var bytes = new byte[16 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, id);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), 0x01030000);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), (uint)payload.Length);
        payload.CopyTo(bytes, 16);
        return bytes;
    }

    private static byte[] WarpPayload()
    {
        var bytes = new byte[68];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 1);
        var offset = 4;
        foreach (var value in new[] { 1d, 0, 0, 0, 0, 0, 0.5, 0.5 })
        {
            BinaryPrimitives.WriteInt64BigEndian(
                bytes.AsSpan(offset), BitConverter.DoubleToInt64Bits(value));
            offset += 8;
        }
        return bytes;
    }

    private static byte[] VignettePayload(double k0)
    {
        var bytes = new byte[56];
        var offset = 0;
        foreach (var value in new[] { k0, 0, 0, 0, 0, 0.5, 0.5 })
        {
            BinaryPrimitives.WriteInt64BigEndian(
                bytes.AsSpan(offset), BitConverter.DoubleToInt64Bits(value));
            offset += 8;
        }
        return bytes;
    }

    private static byte[] TrimPayload(uint top, uint left, uint bottom, uint right)
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, top);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), left);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), bottom);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), right);
        return bytes;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
