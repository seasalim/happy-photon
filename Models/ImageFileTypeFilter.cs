namespace HappyPhoton.Models;

public enum ImageFileTypeFilter
{
    All,
    Raw,
    Jpeg
}

public static class ImageFileTypeFilterExtensions
{
    public static bool Matches(this ImageFileTypeFilter filter, ImageFile image) =>
        filter switch
        {
            ImageFileTypeFilter.Raw =>
                ImageFile.RawExtensions.Contains(image.Extension),
            ImageFileTypeFilter.Jpeg => image.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                        image.Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
}
