namespace HappyPhoton.Services;

public enum CatalogLocationMoveKind
{
    Catalog,
    Cache,
    SetAside
}

public enum CatalogLocationMovePhase
{
    Prepared,
    CatalogCopied,
    CacheMoved,
    Verified,
    ReplacementRootsCreated,
    PointerFlipped
}

internal sealed record CatalogFingerprint(
    long RowCount,
    string IdentityHash,
    Dictionary<string, string> Presets);

internal sealed record CatalogLocationMoveJournal(
    int Version,
    CatalogLocationMoveKind Kind,
    CatalogLocationMovePhase Phase,
    string CatalogRoot,
    string CacheRoot,
    AppDataLocationOrigin CatalogOrigin,
    AppDataLocationOrigin CacheOrigin,
    string? DestinationRoot,
    CatalogFingerprint? Fingerprint,
    bool? CacheWasRenamed,
    string? CacheAsideRoot,
    string? CatalogAsideRoot = null);
