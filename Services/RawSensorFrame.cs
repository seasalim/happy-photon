using HappyPhoton.LibRaw.Interop;

namespace HappyPhoton.Services;

/// <summary>
/// Owns a typed lease over LibRaw's unpacked sensor mosaic.
/// </summary>
internal interface IRawSensorFrame
{
    int Colors { get; }
    uint Filters { get; }
    IReadOnlyList<sbyte> XTrans { get; }
    uint RawPitch { get; }
    uint VisibleWidth { get; }
    uint VisibleHeight { get; }
    uint TopMargin { get; }
    uint LeftMargin { get; }
    uint Black { get; }
    uint Maximum { get; }
    uint RepeatingRows { get; }
    uint RepeatingColumns { get; }
    IReadOnlyList<uint> CBlack { get; }
    Span<ushort> Samples { get; }
}

public sealed class RawSensorFrame : IDisposable, IRawSensorFrame
{
    private LibRawMosaicLease? _lease;

    private RawSensorFrame(
        LibRawSensorIdentity identity,
        LibRawMosaicLease lease)
    {
        Identity = identity;
        _lease = lease;
    }

    public LibRawSensorIdentity Identity { get; }
    public int Colors => Identity.Colors;
    public uint Filters => Identity.Filters;
    public IReadOnlyList<sbyte> XTrans => Identity.XTrans;
    public string ColorDescription => Identity.ColorDescription;
    public uint RawPitch => Lease.RawPitch;
    public uint RawWidth => Lease.RawWidth;
    public uint RawHeight => Lease.RawHeight;
    public uint VisibleWidth => Lease.VisibleWidth;
    public uint VisibleHeight => Lease.VisibleHeight;
    public uint TopMargin => Lease.TopMargin;
    public uint LeftMargin => Lease.LeftMargin;
    public uint Black => Lease.Black;
    public uint Maximum => Lease.Maximum;
    public uint RepeatingRows => Lease.RepeatingRows;
    public uint RepeatingColumns => Lease.RepeatingColumns;
    public IReadOnlyList<uint> CBlack => Lease.CBlack;
    public Span<ushort> Samples => Lease.Samples;

    public static RawSensorFrame? TryCreate(
        LibRawContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var identity = context.GetSensorIdentity(cancellationToken);
        var lease = context.BorrowMosaic(cancellationToken);
        if (lease == null) return null;

        try
        {
            if (!IsValid(identity, lease))
            {
                lease.Dispose();
                return null;
            }

            return new RawSensorFrame(identity, lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _lease, null)?.Dispose();
        GC.SuppressFinalize(this);
    }

    private LibRawMosaicLease Lease =>
        _lease ?? throw new ObjectDisposedException(nameof(RawSensorFrame));

    private static bool IsValid(
        LibRawSensorIdentity identity,
        LibRawMosaicLease lease)
    {
        if (identity.XTrans.Length != 36 ||
            lease.RawPitch == 0 || (lease.RawPitch & 1) != 0 ||
            lease.VisibleWidth == 0 || lease.VisibleHeight == 0 ||
            (ulong)lease.TopMargin + lease.VisibleHeight > lease.RawHeight ||
            (ulong)lease.LeftMargin + lease.VisibleWidth > lease.RawWidth ||
            lease.Maximum == 0)
        {
            return false;
        }

        var blockCount = (ulong)lease.RepeatingRows * lease.RepeatingColumns;
        return blockCount == 0 ||
            blockCount <= (ulong)Math.Max(0, lease.CBlack.Length - 6);
    }
}
