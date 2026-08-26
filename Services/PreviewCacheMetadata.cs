using System.Text.Json;
using Avalonia;

namespace HappyPhoton.Services;

internal readonly record struct PreviewCacheIdentity(
    PixelSize OriginalViewSize,
    PixelSize OriginalImageSize);

internal readonly record struct PreviewCacheMetadata(
    string SettingsHash,
    PreviewCacheIdentity? Identity)
{
    private const int CurrentVersion = 1;

    public static bool TryRead(
        string metadataPath,
        out PreviewCacheMetadata metadata)
    {
        metadata = default;
        try
        {
            // Sidecars written before this format was introduced hold a bare
            // hash, which is not JSON: deserializing throws and the entry reads
            // as absent, so it re-renders once and is rewritten as a document.
            // The preview cache is derived data and already invalidates
            // wholesale on a render-version bump, so this needs no migration.
            var text = File.ReadAllText(metadataPath).Trim();
            var document = JsonSerializer.Deserialize<MetadataDocument>(text);
            if (document == null || document.Version != CurrentVersion ||
                string.IsNullOrWhiteSpace(document.SettingsHash))
            {
                return false;
            }

            PreviewCacheIdentity? identity = null;
            if (document.OriginalViewWidth > 0 &&
                document.OriginalViewHeight > 0)
            {
                identity = new PreviewCacheIdentity(
                    new PixelSize(
                        document.OriginalViewWidth,
                        document.OriginalViewHeight),
                    new PixelSize(
                        document.OriginalImageWidth,
                        document.OriginalImageHeight));
            }
            metadata = new PreviewCacheMetadata(document.SettingsHash, identity);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string Serialize(
        string settingsHash,
        PreviewCacheIdentity identity) =>
        JsonSerializer.Serialize(new MetadataDocument
        {
            Version = CurrentVersion,
            SettingsHash = settingsHash,
            OriginalViewWidth = identity.OriginalViewSize.Width,
            OriginalViewHeight = identity.OriginalViewSize.Height,
            OriginalImageWidth = identity.OriginalImageSize.Width,
            OriginalImageHeight = identity.OriginalImageSize.Height
        });

    private sealed class MetadataDocument
    {
        public int Version { get; set; }
        public string SettingsHash { get; set; } = string.Empty;
        public int OriginalViewWidth { get; set; }
        public int OriginalViewHeight { get; set; }
        public int OriginalImageWidth { get; set; }
        public int OriginalImageHeight { get; set; }
    }
}
