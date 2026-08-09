namespace HappyPhoton.Models;

public readonly record struct ThumbnailSizeRequest
{
    public ThumbnailSizeRequest(int minimumDimension, int generationDimension)
    {
        if (minimumDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumDimension));
        }

        if (generationDimension < minimumDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(generationDimension),
                "Generation dimension must meet or exceed the minimum dimension.");
        }

        MinimumDimension = minimumDimension;
        GenerationDimension = generationDimension;
    }

    public int MinimumDimension { get; }
    public int GenerationDimension { get; }

    public static ThumbnailSizeRequest For(LibraryThumbnailSize size) => size switch
    {
        LibraryThumbnailSize.Small => new(150, 150),
        LibraryThumbnailSize.Medium => new(150, 192),
        LibraryThumbnailSize.Large => new(512, 512),
        _ => new(150, 192)
    };
}
