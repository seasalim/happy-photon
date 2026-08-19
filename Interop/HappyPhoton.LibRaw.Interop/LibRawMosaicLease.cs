namespace HappyPhoton.LibRaw.Interop;

public sealed unsafe class LibRawMosaicLease : IDisposable
{
    private readonly LibRawHandle _handle;
    private nint _data;
    private long _lease;

    internal LibRawMosaicLease(LibRawHandle handle, NativeMosaicDescriptor value)
    {
        if (value.Data is null || value.Lease == 0 || value.CblackCount != 4104 ||
            value.RawPitch < value.RawWidth * sizeof(ushort) ||
            value.ByteLength != (ulong)value.RawPitch * value.RawHeight ||
            (value.ByteLength & 1) != 0 || value.ByteLength / 2 > int.MaxValue)
            throw new InvalidDataException("Native mosaic descriptor is invalid.");
        _handle = handle;
        _data = (nint)value.Data;
        _lease = unchecked((long)value.Lease);
        RawPitch = value.RawPitch;
        RawWidth = value.RawWidth;
        RawHeight = value.RawHeight;
        VisibleWidth = value.VisibleWidth;
        VisibleHeight = value.VisibleHeight;
        TopMargin = value.TopMargin;
        LeftMargin = value.LeftMargin;
        Black = value.Black;
        Maximum = value.Maximum;
        RepeatingRows = value.RepeatingRows;
        RepeatingColumns = value.RepeatingColumns;
        CBlack = new uint[4104];
        uint* source = value.Cblack;
        new ReadOnlySpan<uint>(source, CBlack.Length).CopyTo(CBlack);
        SampleCount = checked((int)(value.ByteLength / 2));
    }

    public uint RawPitch { get; }
    public uint RawWidth { get; }
    public uint RawHeight { get; }
    public uint VisibleWidth { get; }
    public uint VisibleHeight { get; }
    public uint TopMargin { get; }
    public uint LeftMargin { get; }
    public uint Black { get; }
    public uint Maximum { get; }
    public uint RepeatingRows { get; }
    public uint RepeatingColumns { get; }
    public uint[] CBlack { get; }
    private int SampleCount { get; }

    public Span<ushort> Samples => _data == 0
        ? throw new ObjectDisposedException(nameof(LibRawMosaicLease))
        : new((void*)_data, SampleCount);

    public void Dispose()
    {
        Release(throwOnFailure: true);
        GC.SuppressFinalize(this);
    }

    private void Release(bool throwOnFailure)
    {
        var lease = Interlocked.Exchange(ref _lease, 0);
        if (lease == 0) return;
        _data = 0;
        try { NativeApi.ReleaseMosaic(unchecked((ulong)lease)); }
        catch when (!throwOnFailure) { }
        finally { _handle.DangerousRelease(); }
    }

    ~LibRawMosaicLease() => Release(throwOnFailure: false);
}
