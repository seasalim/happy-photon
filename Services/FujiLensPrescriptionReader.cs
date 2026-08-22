using System.Buffers.Binary;

namespace HappyPhoton.Services;

internal sealed class FujiLensPrescriptionReader
{
    private const ushort DistortionTag = 0xf00b;
    private const ushort ChromaticAberrationTag = 0xf00f;
    private const ushort VignettingTag = 0xf010;
    private const ushort RawIfdTag = 0xf000;
    private const int RafHeaderBytes = 108;

    // Tag identities and field types are from exiftool.org's published
    // FujiFilm tag table. Layout interpretation is pinned against our own RAFs.
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
            if (distortionEntry == null || caEntry == null || vignetteEntry == null)
                return Reject("The Fujifilm correction table set is incomplete.");

            var distortion = ReadRadial(reader, distortionEntry, 23);
            var ca = ReadCa(reader, caEntry);
            var vignetting = ReadRadial(reader, vignetteEntry, 23);
            var tables = new FujiLensTables(distortion, ca, vignetting);

            // Available describes successful table parsing. Empty correction lists
            // deliberately keep Summary.HasAny false until the in-file camera-preview
            // geometry validator accepts this private camera/table layout.
            return LensPrescriptionReadResult.Available(new LensPrescription(
                LensPrescriptionSource.FujifilmMakerNote,
                lensName,
                [],
                [],
                LensFrameWindow.Full,
                LensFrameWindow.Full,
                tables));
        }
        catch (Exception exception) when (exception is IOException or
            OverflowException or DcpProfileException or ArgumentException)
        {
            return Reject($"Invalid Fujifilm correction tables: {exception.Message}");
        }
    }

    private static LensRadialTable ReadRadial(
        DcpTiffReader reader,
        TiffEntry entry,
        int expectedCount)
    {
        var values = reader.ReadRationals(entry, expectedCount);
        var table = new LensRadialTable(
            values[0],
            values[1..12],
            values[12..23]);
        ValidateTable(table.Scale, table.Radii, table.Values);
        return table;
    }

    private static LensChromaticAberrationTable ReadCa(
        DcpTiffReader reader,
        TiffEntry entry)
    {
        var values = reader.ReadRationals(entry, 31);
        var table = new LensChromaticAberrationTable(
            values[0],
            values[1..11],
            values[11..21],
            values[21..31]);
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
}
