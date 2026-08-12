using System.Xml.Linq;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed class XmpSidecarReader
{
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

        await using var stream = new FileStream(
            candidate.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await XDocument.LoadAsync(
            stream, LoadOptions.PreserveWhitespace, cancellationToken);
        return XmpSidecarDocument.ReadFacts(document, labelNames);
    }
}
