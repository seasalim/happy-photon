using System.Xml.Linq;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed class XmpSidecarReader
{
    internal const long MaximumSidecarBytes = 4L * 1024 * 1024;
    private readonly ISourceAvailabilityService _availability;

    internal XmpSidecarReader(ISourceAvailabilityService availability) =>
        _availability = availability;

    public XmpSidecarReader() : this(new SourceAvailabilityService())
    {
    }

    public async Task<XmpSidecarFacts?> ReadAsync(
        XmpSidecarCandidate candidate,
        IReadOnlyDictionary<ColorLabel, string> labelNames,
        CancellationToken cancellationToken = default)
    {
        if (!SourceAccessPolicy.CanRead(
                _availability.GetAvailability(candidate.Path),
                SourceReadIntent.Background))
        {
            return null;
        }

        XmpSidecarReadStream.ThrowIfOversized(candidate.Path, candidate.Length);

        await using var stream = new FileStream(
            candidate.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var bounded = new XmpSidecarReadStream(
            stream, candidate.Path, MaximumSidecarBytes);
        var document = await XDocument.LoadAsync(
            bounded, LoadOptions.PreserveWhitespace, cancellationToken);
        return XmpSidecarDocument.ReadFacts(document, labelNames);
    }
}

internal sealed class XmpSidecarTooLargeException(string path)
    : IOException($"XMP sidecar exceeds the 4 MiB limit: {path}");

internal sealed class XmpSidecarReadStream(
    Stream inner,
    string path,
    long maximumBytes) : Stream
{
    private long _bytesRead;

    public static void ThrowIfOversized(string path, long knownLength = -1)
    {
        if (knownLength > XmpSidecarReader.MaximumSidecarBytes ||
            new FileInfo(path).Length > XmpSidecarReader.MaximumSidecarBytes)
            throw new XmpSidecarTooLargeException(path);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position
    {
        get => inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Count(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        Count(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken));

    public override int ReadByte()
    {
        var value = inner.ReadByte();
        return Count(value < 0 ? 0 : 1) == 0 ? -1 : value;
    }

    private int Count(int count)
    {
        _bytesRead += count;
        if (_bytesRead > maximumBytes)
            throw new XmpSidecarTooLargeException(path);
        return count;
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
