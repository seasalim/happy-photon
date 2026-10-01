using System.Buffers.Binary;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal static class SyncSpotRotatedFixture
{
    internal static string Create(string original, string directory, ushort orientation)
    {
        Assert.InRange(orientation, (ushort)1, (ushort)8);
        SyncProfileGateSupport.RequireLocal(original);
        var bytes = File.ReadAllBytes(original);
        Assert.Equal((byte)'I', bytes[0]);
        Assert.Equal((byte)'I', bytes[1]);
        Assert.Equal(42, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
        var ifd = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(ifd));
        var offsets = Enumerable.Range(0, count).Select(index => ifd + 2 + index * 12)
            .Where(offset => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset)) == 0x112).ToArray();
        var entry = Assert.Single(offsets);
        Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entry + 2)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 4)));
        var value = entry + 8;
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(value)));
        var path = Path.Combine(directory, $"orientation-{orientation}.cr2");
        SyncProfileGateSupport.RequireLocal(original);
        File.Copy(original, path);

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            stream.Position = value;
            Span<byte> tag = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(tag, orientation);
            stream.Write(tag);
        }

        SyncProfileGateSupport.RequireLocal(path);
        var copy = File.ReadAllBytes(path);
        Assert.Equal(bytes.Length, copy.Length);
        Assert.Equal(orientation, BinaryPrimitives.ReadUInt16LittleEndian(copy.AsSpan(value)));
        // Restoring the sole authored SHORT must restore the entire original byte stream.
        bytes.AsSpan(value, 2).CopyTo(copy.AsSpan(value, 2));
        Assert.True(bytes.AsSpan().SequenceEqual(copy));
        SyncProfileGateSupport.RequireLocal(original);
        Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(original)));
        SyncSpotGateSupport.AssertSerial(path);

        return path;
    }
}
