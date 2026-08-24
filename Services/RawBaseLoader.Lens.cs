using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed partial class RawBaseLoader
{
    private static readonly LensIdentityResolver LensIdentityResolver = new();
    private static readonly EmbeddedLensReader[] EmbeddedLensReaders =
    [
        new(".dng", (path, _) => new DngLensPrescriptionReader().Read(path)),
        new(".raf", (path, lens) =>
            new FujiLensPrescriptionReader().Read(path, lens))
    ];

    internal static LensPrescriptionReadResult ReadLensPrescription(
        ImageFile file,
        LibRawMetadata metadata,
        LibRawLensIdentity? lensIdentity,
        LibRawDimensions dimensions)
    {
        var embedded = LensfunPrescriptionReader.ForceSource
            ? LensPrescriptionReadResult.None
            : EmbeddedLensReaders.FirstOrDefault(reader =>
                file.Extension.Equals(
                    reader.Extension, StringComparison.OrdinalIgnoreCase))
                ?.Read(file.FilePath, metadata.Lens) ?? LensPrescriptionReadResult.None;
        var prescription = embedded.Prescription;
        var lensfun = LensPrescriptionReadResult.None;
        if (NeedsLensfun(prescription))
        {
            var reader = new LensfunPrescriptionReader();
            lensfun = reader.Read(
                metadata,
                checked((int)dimensions.VisibleWidth),
                checked((int)dimensions.VisibleHeight));
            if (lensfun.Status == LensPrescriptionStatus.None)
            {
                var resolvedName = LensIdentityResolver.Resolve(
                    metadata.NormalizedMake ?? metadata.Make, lensIdentity);
                if (!string.IsNullOrWhiteSpace(resolvedName))
                {
                    var resolvedMetadata = metadata with { Lens = resolvedName };
                    lensfun = reader.Read(
                        resolvedMetadata,
                        checked((int)dimensions.VisibleWidth),
                        checked((int)dimensions.VisibleHeight));
                }
            }
            prescription = LensfunPrescriptionReader.Merge(
                prescription, lensfun.Prescription);
        }
        var result = prescription != null
            ? LensPrescriptionReadResult.Available(prescription)
            : lensfun.Status is LensPrescriptionStatus.Invalid or
                LensPrescriptionStatus.Unsupported
                ? lensfun
                : embedded.Status is LensPrescriptionStatus.Invalid or
                    LensPrescriptionStatus.Unsupported
                    ? embedded
                    : LensPrescriptionReadResult.None;
        if (result.Status is LensPrescriptionStatus.Invalid or
            LensPrescriptionStatus.Unsupported)
        {
            ImageServiceHelpers.LogDebug(
                nameof(RawBaseLoader),
                $"Lens prescription rejected: {result.Message}",
                file.FilePath);
        }
        return result;
    }

    // Resolution is settings-independent: the toggles gate application only.
    // Gating the lookup on them made capabilities vanish when a user turned
    // every class off, disabling the OPTICS section with no way back in.
    internal static bool NeedsLensfun(LensPrescription? prescription)
    {
        return prescription?.HasDistortion != true ||
            prescription?.HasChromaticAberration != true ||
            prescription?.HasVignetting != true;
    }

    private sealed record EmbeddedLensReader(
        string Extension,
        Func<string, string?, LensPrescriptionReadResult> Read);
}
