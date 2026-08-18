namespace HappyPhoton.Tests;

internal sealed record PrecisionEvidenceKey(
    string Case,
    string Population,
    string Boundary)
{
    public override string ToString() => $"{Case}|{Population}|{Boundary}";
}

internal static class PrecisionExpectedEvidence
{
    public static IReadOnlyList<PrecisionEvidenceKey> Create(
        PrecisionCensusManifest manifest)
    {
        var result = new List<PrecisionEvidenceKey>();
        var rawFull = manifest.Population("real-raw-full-frames").Id;
        var rawCrops = manifest.Population("real-raw-focused-crops").Id;
        foreach (var asset in manifest.FullFrameAssets)
        foreach (var setting in manifest.RawSettings)
        {
            result.Add(new PrecisionEvidenceKey(
                "case-5-real-raw",
                rawFull,
                $"post-tone/{asset.Id}/{setting.Id}/full-frame/{asset.Purpose}"));
            foreach (var roi in manifest.FocusedRois.Where(value =>
                value.AssetId == asset.Id))
            {
                result.Add(new PrecisionEvidenceKey(
                    "case-5-real-raw",
                    rawCrops,
                    $"post-tone/{asset.Id}/{setting.Id}/focused-roi/{roi.Id}"));
            }
        }

        var wide = manifest.Population("wide-space-representative-colors").Id;
        result.Add(new PrecisionEvidenceKey(
            "case-2-wide-primaries", wide, "rec2020-linear-d65"));
        result.Add(new PrecisionEvidenceKey(
            "case-2-wide-primaries", wide, "romm-linear-d50"));

        var exposure = manifest.Population("synthetic-exposure-sweep").Id;
        result.AddRange(manifest.ExposureSettings.Select(setting =>
            new PrecisionEvidenceKey(
                "case-3-exposure-swings",
                exposure,
                $"post-tone/{setting.Id}")));

        var stacked = manifest.Population("synthetic-stacked-pattern").Id;
        foreach (var boundary in new[]
        {
            "post-geometry",
            "post-chromatic-matrix",
            "post-tone",
            "post-chroma",
            "post-capture-sharpen",
            "post-chroma-nr",
            "post-resize"
        })
        {
            result.Add(new PrecisionEvidenceKey(
                "case-4-stacked-edits", stacked, boundary));
        }

        var synthetic = manifest.Population("synthetic-saturation-extreme").Id;
        result.AddRange(PrecisionBoundaryCensusTests.SyntheticEvidenceBoundaries()
            .Select(boundary => new PrecisionEvidenceKey(
                "case-1-synthetic-baseline", synthetic, boundary)));
        if (result.Count != result.Distinct().Count())
        {
            throw new InvalidOperationException(
                "The precision evidence inventory contains duplicate keys.");
        }
        return result;
    }
}
