using System.Buffers.Binary;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;
using static HappyPhoton.Tests.RawBaseLoaderTestSupport;

namespace HappyPhoton.Tests;

public sealed class DcpAsShotNeutralIntegrationTests
{
    private const ushort AsShotNeutral = 50728;

    [Fact]
    public void ShortEncodedAsShotNeutralRemainsActiveThroughRawBaseLoader()
    {
        using var directory = new TemporaryDirectory();
        var dngPath = Path.Combine(directory.Path, "short-neutral.dng");
        File.Copy(Asset("pentax-k-r.dng"), dngPath);
        RewriteAsShotNeutralAsShort(dngPath, [2, 1, 2]);
        var cameraData = new DcpProfileReader().ReadCameraData(dngPath);
        Assert.NotNull(cameraData.AsShotNeutral);
        Assert.Equal([2.0, 1.0, 2.0], cameraData.AsShotNeutral!);

        var profilePath = SyntheticDcpFactory.WriteTemporary(
            directory.Path,
            new SyntheticDcpOptions
            {
                Name = "SHORT neutral integration",
                ForwardMatrix1 = DcpProfileReaderTests.D50Forward(1)
            });
        var reader = new DcpProfileReader();
        var snapshot = reader.ReadExternalSnapshot(profilePath);
        var profile = reader.ParseExternal(snapshot, "short-neutral");
        var selection = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = profilePath,
            ContentHash = snapshot.ContentHash
        };
        var resolution = DcpProfileResolution.Success(selection, profile);
        var decode = BaseDecodeSettings.From(new EditSettings
        {
            RawProfile = selection
        }).WithProfileResolution(resolution);

        using var loaded = new RawBaseLoader().LoadPreviewBase(
            new ImageFile(dngPath),
            decode,
            CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(DcpProfileErrorCode.None, loaded!.Info.ProfileStatus);
        Assert.Equal(resolution.Token, loaded.Info.ProfileToken);
        Assert.NotNull(loaded.Info.DcpProfile);
    }

    private static void RewriteAsShotNeutralAsShort(
        string path,
        ReadOnlySpan<ushort> values)
    {
        var bytes = File.ReadAllBytes(path);
        var littleEndian = bytes[0] == (byte)'I' && bytes[1] == (byte)'I';
        Assert.True(littleEndian ||
            bytes[0] == (byte)'M' && bytes[1] == (byte)'M');
        var firstIfd = checked((int)ReadUInt32(bytes.AsSpan(4, 4), littleEndian));
        var entryCount = ReadUInt16(bytes.AsSpan(firstIfd, 2), littleEndian);
        var entryOffset = -1;
        for (var index = 0; index < entryCount; index++)
        {
            var candidate = checked(firstIfd + 2 + index * 12);
            if (ReadUInt16(bytes.AsSpan(candidate, 2), littleEndian) ==
                AsShotNeutral)
            {
                entryOffset = candidate;
                break;
            }
        }
        Assert.True(entryOffset >= 0, "The DNG fixture has no AsShotNeutral tag.");

        var payloadOffset = (bytes.Length + 1) & ~1;
        Array.Resize(ref bytes, checked(payloadOffset + values.Length * 2));
        WriteUInt16(bytes.AsSpan(entryOffset + 2, 2), 3, littleEndian);
        WriteUInt32(bytes.AsSpan(entryOffset + 4, 4),
            checked((uint)values.Length), littleEndian);
        WriteUInt32(bytes.AsSpan(entryOffset + 8, 4),
            checked((uint)payloadOffset), littleEndian);
        for (var index = 0; index < values.Length; index++)
        {
            WriteUInt16(
                bytes.AsSpan(payloadOffset + index * 2, 2),
                values[index],
                littleEndian);
        }
        File.WriteAllBytes(path, bytes);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> value, bool little) =>
        little
            ? BinaryPrimitives.ReadUInt16LittleEndian(value)
            : BinaryPrimitives.ReadUInt16BigEndian(value);

    private static uint ReadUInt32(ReadOnlySpan<byte> value, bool little) =>
        little
            ? BinaryPrimitives.ReadUInt32LittleEndian(value)
            : BinaryPrimitives.ReadUInt32BigEndian(value);

    private static void WriteUInt16(Span<byte> target, ushort value, bool little)
    {
        if (little) BinaryPrimitives.WriteUInt16LittleEndian(target, value);
        else BinaryPrimitives.WriteUInt16BigEndian(target, value);
    }

    private static void WriteUInt32(Span<byte> target, uint value, bool little)
    {
        if (little) BinaryPrimitives.WriteUInt32LittleEndian(target, value);
        else BinaryPrimitives.WriteUInt32BigEndian(target, value);
    }
}
