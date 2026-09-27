namespace HappyPhoton.Services;

public enum CatalogLocationMoveKind
{
    Catalog,
    Cache,
    SetAside,
    Restore
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
    string? CatalogAsideRoot = null,
    CatalogRestoreState? Restore = null);

internal enum CatalogRestorePhase { Prepared, Verified, PayloadStaged, Preserved, Replacing, CacheReset, NoticeRecorded }
internal sealed record CatalogRestoreState(string BackupPath, string ArchiveHash, string ManifestHash,
    string BeforeStem, bool AcknowledgeDifferentCatalog, CatalogRestorePhase Phase = CatalogRestorePhase.Prepared,
    Dictionary<string, BackupEntry>? Entries = null, DateTimeOffset? BackupUtc = null);
