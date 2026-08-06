namespace HappyPhoton.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Represents a crop region using normalized coordinates (0.0 to 1.0) for resolution independence.
/// </summary>
public class CropRegion
{
    /// <summary>Left edge of the crop region (0.0 to 1.0)</summary>
    [JsonPropertyName("left")]
    public double Left { get; set; } = 0.0;

    /// <summary>Top edge of the crop region (0.0 to 1.0)</summary>
    [JsonPropertyName("top")]
    public double Top { get; set; } = 0.0;

    /// <summary>Right edge of the crop region (0.0 to 1.0)</summary>
    [JsonPropertyName("right")]
    public double Right { get; set; } = 1.0;

    /// <summary>Bottom edge of the crop region (0.0 to 1.0)</summary>
    [JsonPropertyName("bottom")]
    public double Bottom { get; set; } = 1.0;

    /// <summary>
    /// Returns true if the crop region represents the full image (no cropping).
    /// </summary>
    [JsonIgnore]
    public bool IsFullImage => Left <= 0.001 && Top <= 0.001 &&
                               Right >= 0.999 && Bottom >= 0.999;

    /// <summary>
    /// Creates a deep copy of this CropRegion.
    /// </summary>
    public CropRegion Clone() => new()
    {
        Left = Left,
        Top = Top,
        Right = Right,
        Bottom = Bottom
    };

    /// <summary>
    /// Resets the crop region to the full image.
    /// </summary>
    public void Reset()
    {
        Left = 0.0;
        Top = 0.0;
        Right = 1.0;
        Bottom = 1.0;
    }

    /// <summary>
    /// Converts normalized coordinates to pixel coordinates.
    /// </summary>
    /// <param name="imageWidth">The width of the image in pixels</param>
    /// <param name="imageHeight">The height of the image in pixels</param>
    /// <returns>Pixel coordinates: X, Y, Width, Height</returns>
    public (int X, int Y, int Width, int Height) ToPixels(int imageWidth, int imageHeight)
    {
        int x = (int)Math.Round(Left * imageWidth);
        int y = (int)Math.Round(Top * imageHeight);
        int right = (int)Math.Round(Right * imageWidth);
        int bottom = (int)Math.Round(Bottom * imageHeight);

        // Ensure minimum size of 1x1
        int width = Math.Max(1, right - x);
        int height = Math.Max(1, bottom - y);

        // Clamp to image bounds
        x = Math.Clamp(x, 0, imageWidth - 1);
        y = Math.Clamp(y, 0, imageHeight - 1);
        width = Math.Min(width, imageWidth - x);
        height = Math.Min(height, imageHeight - y);

        return (x, y, width, height);
    }
}
