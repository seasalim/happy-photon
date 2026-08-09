using System.Text.Json;

namespace HappyPhoton.Services;

internal readonly record struct RenderedThumbnailMetadata(
    int Version,
    string SettingsHash,
    int PixelWidth,
    int PixelHeight)
{
    public const int CurrentVersion = 1;
    public int LongEdge => Math.Max(PixelWidth, PixelHeight);

    public static bool TryRead(
        string metadataPath,
        string imagePath,
        out RenderedThumbnailMetadata metadata)
    {
        metadata = default;
        try
        {
            var text = File.ReadAllText(metadataPath).Trim();
            if (text.StartsWith('{'))
            {
                var parsed = JsonSerializer.Deserialize<MetadataDocument>(text);
                if (parsed == null ||
                    parsed.Version != CurrentVersion ||
                    string.IsNullOrWhiteSpace(parsed.SettingsHash) ||
                    parsed.PixelWidth <= 0 ||
                    parsed.PixelHeight <= 0)
                {
                    return false;
                }

                metadata = new RenderedThumbnailMetadata(
                    parsed.Version,
                    parsed.SettingsHash,
                    parsed.PixelWidth,
                    parsed.PixelHeight);
                return true;
            }

            if (string.IsNullOrWhiteSpace(text) ||
                !JpegDimensions.TryRead(imagePath, out var dimensions))
            {
                return false;
            }

            metadata = new RenderedThumbnailMetadata(
                CurrentVersion,
                text,
                dimensions.Width,
                dimensions.Height);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string Serialize(
        string settingsHash,
        int pixelWidth,
        int pixelHeight) =>
        JsonSerializer.Serialize(new MetadataDocument
        {
            Version = CurrentVersion,
            SettingsHash = settingsHash,
            PixelWidth = pixelWidth,
            PixelHeight = pixelHeight
        });

    private sealed class MetadataDocument
    {
        public int Version { get; set; }
        public string SettingsHash { get; set; } = string.Empty;
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
    }
}
