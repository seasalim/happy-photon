using HappyPhoton.Models;

namespace HappyPhoton.Services;

public enum CatalogImportPolicy
{
    LightroomWins,
    FillEmptyOnly
}

public enum CatalogImportFactKind
{
    Empty,
    Value,
    Unsupported,
    NotCarried
}

public readonly record struct CatalogImportFact<T>(
    CatalogImportFactKind Kind,
    T Value,
    string? SourceToken = null)
{
    public static CatalogImportFact<T> Empty =>
        new(CatalogImportFactKind.Empty, default!);
    public static CatalogImportFact<T> NotCarried =>
        new(CatalogImportFactKind.NotCarried, default!);
    public static CatalogImportFact<T> Unsupported(string? token = null) =>
        new(CatalogImportFactKind.Unsupported, default!, token);
    public static CatalogImportFact<T> Mapped(T value) =>
        new(CatalogImportFactKind.Value, value);
}

public sealed record CatalogSourceRoot(string SourcePath, int PhotoCount);

public sealed record CatalogImportRecord(
    string SourceRoot,
    string RelativePath,
    CatalogImportFact<int> Rating,
    CatalogImportFact<ImageFlag> Flag,
    CatalogImportFact<ColorLabel> ColorLabel,
    bool IsVirtualCopy);

public sealed record LightroomCatalogContents(
    string CatalogPath,
    long DatabaseVersion,
    int MajorVersion,
    bool IsVerifiedVersion,
    AssessmentAxes CarriedAxes,
    IReadOnlyList<CatalogSourceRoot> Roots,
    IReadOnlyList<CatalogImportRecord> Records,
    IReadOnlyList<string> SchemaWarnings);

public sealed record CatalogImportBaseline(
    bool Exists,
    long ImageId,
    ImageFlag Flag,
    int Rating,
    ColorLabel ColorLabel,
    long Revision,
    DateTime? AssessedUtc,
    AssessmentAxes PendingAxes);

public sealed record CatalogImportChange(
    string FilePath,
    CatalogImportBaseline Baseline,
    AssessmentAxes ComparedAxes,
    AssessmentAxes Axes,
    ImageFlag? Flag,
    int? Rating,
    ColorLabel? ColorLabel);

public sealed record CatalogImportAxisSummary(
    int Written,
    int Unchanged,
    int PreservedByPolicy,
    int Unsupported,
    int NotImported);

public sealed record CatalogImportReport(
    int SourceVerdictPhotos,
    int MatchedPhotos,
    int UpdatedPhotos,
    int ExistingCatalogRows,
    int NewlyStoredPaths,
    int UnresolvedRootPhotos,
    int UnsupportedFilePhotos,
    int VirtualCopyPhotos,
    CatalogImportAxisSummary Rating,
    CatalogImportAxisSummary Flag,
    CatalogImportAxisSummary ColorLabel,
    IReadOnlyDictionary<string, int> UnsupportedLabelTokens,
    IReadOnlyList<string> ActionableOutcomes,
    IReadOnlyList<string> InformationalOutcomes,
    bool IsUnverifiedVersion)
{
    public bool HasChanges => UpdatedPhotos > 0;
    public bool NothingToImport => SourceVerdictPhotos == 0;
    public bool NothingMatched => SourceVerdictPhotos > 0 && MatchedPhotos == 0;
}

public sealed record CatalogImportPreview(
    string SourceCatalogPath,
    CatalogImportPolicy Policy,
    IReadOnlyDictionary<string, string> RootMappings,
    IReadOnlyList<CatalogImportChange> Changes,
    CatalogImportReport Report,
    string SettingsKey,
    string? BaselineSettingsJson,
    string SettingsJson,
    IReadOnlyList<string> ImportedPaths);

public sealed record CatalogImportAdoption(
    long BaselineRevision,
    AssessmentSnapshot Snapshot);

public sealed record CatalogImportApplyResult(
    CatalogImportReport Report,
    IReadOnlyList<CatalogImportAdoption> Adoptions,
    int DatabaseWrites);

public sealed record CatalogImportStoredSettings(
    string CatalogPath,
    IReadOnlyDictionary<string, string> RootMappings,
    IReadOnlyDictionary<string, CatalogImportPolicy> Policies);

public sealed class CatalogImportConflictException : InvalidOperationException
{
    public CatalogImportConflictException()
        : base("The Happy Photon catalog changed after the import preview. Refresh the preview and try again.")
    {
    }
}
