using System.Buffers.Binary;

namespace HappyPhoton.Services;

internal sealed class FujiLensPrescriptionReader
{
    internal static bool IncludeUnqualifiedTables { get; set; }

    private const ushort DistortionTag = 0xf00b;
    private const ushort ChromaticAberrationTag = 0xf00f;
    private const ushort VignettingTag = 0xf010;
    private const ushort RawIfdTag = 0xf000;
    private const int RafHeaderBytes = 108;

    private static readonly FujiLayout[] Layouts =
    [
        new("23/31/23", 23, 31, 23, 11, 10, false, true, false, false),
        new("19/29/19", 19, 29, 19, 9, 9, true, false, false, false)
    ];

    // Tag identities and field types are from exiftool.org's published
    // FujiFilm tag table. Layout interpretation is derived from our own RAFs.
    internal LensPrescriptionReadResult Read(string path, string? lensName = null)
    {
        try
        {
            Span<byte> header = stackalloc byte[RafHeaderBytes];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                       FileShare.Read | FileShare.Delete))
            {
                stream.ReadExactly(header);
            }
            if (!header[..16].SequenceEqual("FUJIFILMCCD-RAW "u8))
                return LensPrescriptionReadResult.None;
            var rawOffset = BinaryPrimitives.ReadUInt32BigEndian(header[100..104]);
            using var reader = DcpTiffReader.Open(path, rawOffset);
            var ifd = reader.ReadFirstIfd();
            var rawIfdEntry = ifd.Find(RawIfdTag);
            if (rawIfdEntry is { Type: 4 or 13, Count: 1 })
                ifd = reader.ReadIfdAtOffset(rawIfdEntry.ValueOffset);

            var distortionEntry = ifd.Find(DistortionTag);
            var caEntry = ifd.Find(ChromaticAberrationTag);
            var vignetteEntry = ifd.Find(VignettingTag);
            if (distortionEntry == null && caEntry == null && vignetteEntry == null)
                return LensPrescriptionReadResult.None;
            var layout = FindLayout(distortionEntry, caEntry, vignetteEntry);
            if (layout == null)
            {
                return Reject(
                    "The Fujifilm correction table count tuple is not qualified: " +
                    $"{CountOf(distortionEntry)}/{CountOf(caEntry)}/{CountOf(vignetteEntry)}.");
            }

            var distortion = TryRead(
                "geometric distortion", distortionEntry,
                entry => ReadRadial(reader, entry, layout.RadialKnots));
            var ca = TryRead(
                "chromatic aberration", caEntry,
                entry => ReadCa(reader, entry, layout));
            var vignetting = TryRead(
                "vignetting", vignetteEntry,
                entry => ReadRadial(reader, entry, layout.RadialKnots));
            if (distortion.Value == null && ca.Value == null && vignetting.Value == null)
            {
                return Reject(string.Join(" ", new[]
                {
                    distortion.Message, ca.Message, vignetting.Message
                }.Where(message => message != null)));
            }

            var tables = new FujiLensTables(
                layout.Name,
                distortion.Value,
                ca.Value,
                vignetting.Value,
                QualificationMessage(distortion.Message, layout.DistortionQualified),
                QualificationMessage(ca.Message, layout.ChromaticAberrationQualified),
                QualificationMessage(vignetting.Message, layout.VignettingQualified));
            var tableWarp = new LensTableWarp(
                (layout.DistortionQualified || IncludeUnqualifiedTables) &&
                    HasDistortionEffect(distortion.Value) ? distortion.Value : null,
                (layout.ChromaticAberrationQualified || IncludeUnqualifiedTables) &&
                    HasCaEffect(ca.Value) ? ca.Value : null);
            var tableWarps = tableWarp.Distortion != null ||
                tableWarp.ChromaticAberration != null
                ? new[] { tableWarp }
                : [];
            var tableVignettes = (layout.VignettingQualified || IncludeUnqualifiedTables) &&
                HasVignetteEffect(vignetting.Value)
                ? new[] { new LensTableVignette(vignetting.Value!) }
                : [];

            return LensPrescriptionReadResult.Available(new LensPrescription(
                LensPrescriptionSource.FujifilmMakerNote,
                lensName,
                [],
                [],
                LensFrameWindow.Full,
                LensFrameWindow.Full,
                tables,
                tableWarps,
                tableVignettes));
        }
        catch (Exception exception) when (exception is IOException or
            OverflowException or DcpProfileException or ArgumentException)
        {
            return Reject($"Invalid Fujifilm correction tables: {exception.Message}");
        }
    }

    private static FujiLayout? FindLayout(
        TiffEntry? distortion,
        TiffEntry? ca,
        TiffEntry? vignetting)
    {
        var matches = Layouts.Where(layout =>
            (distortion == null || distortion.Count == layout.DistortionCount) &&
            (ca == null || ca.Count == layout.ChromaticAberrationCount) &&
            (vignetting == null || vignetting.Count == layout.VignettingCount)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string CountOf(TiffEntry? entry) =>
        entry == null ? "missing" : entry.Count.ToString();

    private static string? QualificationMessage(string? parseMessage, bool qualified) =>
        parseMessage ?? (qualified ? null :
            "The Fujifilm table interpretation is parsed but not qualified for application.");

    private static bool HasDistortionEffect(LensRadialTable? table) =>
        table != null && table.Values.Any(value => Math.Abs(value) > 1e-12);

    private static bool HasCaEffect(LensChromaticAberrationTable? table) =>
        table != null && (table.Red.Any(value => Math.Abs(value) > 1e-12) ||
            table.Blue.Any(value => Math.Abs(value) > 1e-12));

    private static bool HasVignetteEffect(LensRadialTable? table) =>
        table != null && table.Values.Any(value => Math.Abs(value - 100) > 1e-12);

    private static TableRead<T> TryRead<T>(
        string name,
        TiffEntry? entry,
        Func<TiffEntry, T> read) where T : class
    {
        if (entry == null)
            return new(null, $"The Fujifilm {name} table is missing.");
        try
        {
            return new(read(entry), null);
        }
        catch (Exception exception) when (exception is OverflowException or
            DcpProfileException or ArgumentException)
        {
            return new(null, $"Invalid Fujifilm {name} table: {exception.Message}");
        }
    }

    private static LensRadialTable ReadRadial(
        DcpTiffReader reader,
        TiffEntry entry,
        int knotCount)
    {
        var values = reader.ReadRationals(entry, 1 + knotCount * 2);
        var table = new LensRadialTable(
            values[0],
            values[1..(1 + knotCount)],
            values[(1 + knotCount)..]);
        ValidateTable(table.Scale, table.Radii, table.Values);
        return table;
    }

    private static LensChromaticAberrationTable ReadCa(
        DcpTiffReader reader,
        TiffEntry entry,
        FujiLayout layout)
    {
        var values = reader.ReadRationals(
            entry,
            1 + layout.CaKnots * 3 + (layout.CaHasTrailingScale ? 1 : 0));
        var redStart = 1 + layout.CaKnots;
        var blueStart = redStart + layout.CaKnots;
        if (layout.CaHasTrailingScale && values[^1] != values[0])
            throw new ArgumentException(
                "The Fujifilm chromatic-aberration scale sentinels differ.");
        var table = new LensChromaticAberrationTable(
            values[0],
            values[1..redStart],
            values[redStart..blueStart],
            values[blueStart..(blueStart + layout.CaKnots)]);
        ValidateTable(table.Scale, table.Radii, table.Red);
        ValidateTable(table.Scale, table.Radii, table.Blue);
        return table;
    }

    private static void ValidateTable(
        double scale,
        IReadOnlyList<double> radii,
        IReadOnlyList<double> values)
    {
        if (!double.IsFinite(scale) || scale <= 0 || radii.Count != values.Count ||
            radii.Count < 2 || radii.Any(value => !double.IsFinite(value)) ||
            values.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("A Fujifilm correction table contains invalid values.");
        for (var index = 1; index < radii.Count; index++)
        {
            if (radii[index] <= radii[index - 1])
                throw new ArgumentException("Fujifilm correction radii are not increasing.");
        }
    }

    private static LensPrescriptionReadResult Reject(string message) =>
        LensPrescriptionReadResult.Reject(LensPrescriptionStatus.Invalid, message);

    private sealed record FujiLayout(
        string Name,
        uint DistortionCount,
        uint ChromaticAberrationCount,
        uint VignettingCount,
        int RadialKnots,
        int CaKnots,
        bool CaHasTrailingScale,
        bool DistortionQualified,
        bool ChromaticAberrationQualified,
        bool VignettingQualified);

    private sealed record TableRead<T>(T? Value, string? Message) where T : class;
}
