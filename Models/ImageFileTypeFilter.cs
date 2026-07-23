namespace HappyPhoton.Models;

public enum ImageFileTypeFilter
{
    All,
    Raw,
    Jpeg
}

public static class ImageFileTypeFilterExtensions
{
    private static readonly HashSet<string> CameraRawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3",
        ".nef", ".nrw",
        ".arw", ".srf", ".sr2",
        ".dng",
        ".raf",
        ".orf",
        ".rw2",
        ".pef"
    };

    public static bool Matches(this ImageFileTypeFilter filter, ImageFile image) =>
        filter switch
        {
            ImageFileTypeFilter.Raw => CameraRawExtensions.Contains(image.Extension),
            ImageFileTypeFilter.Jpeg => image.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                        image.Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
}
