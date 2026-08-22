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
        Assert.Equal(11, tables.Distortion.Radii.Count);
        Assert.Equal(10, tables.ChromaticAberration.Radii.Count);
        Assert.Equal(11, tables.Vignetting.Radii.Count);
        Assert.Equal(2500d / 11, tables.Distortion.Scale, 10);
        Assert.Equal(0, tables.Distortion.Radii[0], 10);
        Assert.Equal(1, tables.Distortion.Radii[^1], 10);
        Assert.Equal(250, tables.ChromaticAberration.Scale, 10);
        Assert.Equal(0.1, tables.ChromaticAberration.Radii[0], 10);
        Assert.Equal(1, tables.ChromaticAberration.Radii[^1], 10);
        Assert.False(result.Prescription.Summary.HasAny);

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

    private static IEnumerable<double> EnumerateScalars(FujiLensTables tables)
    {
        yield return tables.Distortion.Scale;
        foreach (var value in tables.Distortion.Radii) yield return value;
        foreach (var value in tables.Distortion.Values) yield return value;
        yield return tables.ChromaticAberration.Scale;
        foreach (var value in tables.ChromaticAberration.Radii) yield return value;
        foreach (var value in tables.ChromaticAberration.Red) yield return value;
        foreach (var value in tables.ChromaticAberration.Blue) yield return value;
        yield return tables.Vignetting.Scale;
        foreach (var value in tables.Vignetting.Radii) yield return value;
        foreach (var value in tables.Vignetting.Values) yield return value;
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
