using System.Runtime.InteropServices;

namespace HappyPhoton.LibRaw.Interop;

public sealed record LibRawImageDescription(
    ulong ByteLength, uint Width, uint Height, uint BitsPerSample,
    uint Channels, int Format);

public sealed unsafe class LibRawImage : IDisposable
{
    private nint _data;
    private long _allocation;

    private LibRawImage(NativeImageDescriptor value)
    {
        _data = (nint)value.Data;
        _allocation = unchecked((long)value.Allocation);
        Description = new(value.ByteLength, value.Width, value.Height,
            value.BitsPerSample, value.Channels, value.Format);
    }

    public LibRawImageDescription Description { get; }

    internal static LibRawImage FromNative(NativeImageDescriptor value, bool processed)
    {
        if (value.Data is null || value.Allocation == 0 || value.ByteLength == 0)
            throw new InvalidDataException("Native image has no owned data.");
        if (processed && value.Format != 2)
            throw new InvalidDataException("Processed image is not a bitmap.");
        if (value.Format == 2)
        {
            if (value.Width == 0 || value.Height == 0 || value.Channels == 0 ||
                value.BitsPerSample is not (8 or 16))
                throw new InvalidDataException("Native bitmap shape is invalid.");
            ulong expected;
            try
            {
                expected = checked((ulong)value.Width * value.Height * value.Channels *
                    (value.BitsPerSample / 8));
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException("Native bitmap shape overflowed.", exception);
            }
            if (expected != value.ByteLength)
                throw new InvalidDataException("Native bitmap length does not match its shape.");
        }
        if (value.ByteLength > int.MaxValue)
            throw new InvalidDataException("Native image exceeds managed array limits.");
        return new(value);
    }

    public byte[] CopyData()
    {
        return AsSpan().ToArray();
    }

    public ReadOnlySpan<byte> AsSpan()
    {
        var data = _data;
        if (data == 0) throw new ObjectDisposedException(nameof(LibRawImage));
        return new ReadOnlySpan<byte>((void*)data, checked((int)Description.ByteLength));
    }

    public void Dispose()
    {
        var allocation = Interlocked.Exchange(ref _allocation, 0);
        if (allocation == 0) return;
        _data = 0;
        NativeApi.FreeImage(unchecked((ulong)allocation));
        GC.SuppressFinalize(this);
    }

    ~LibRawImage()
    {
        var allocation = Interlocked.Exchange(ref _allocation, 0);
        if (allocation == 0) return;
        try { NativeApi.FreeImage(unchecked((ulong)allocation)); }
        catch { }
    }
}
