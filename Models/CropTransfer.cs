namespace HappyPhoton.Models;

internal static class CropTransfer
{
    internal static bool Matches((int Width, int Height) source, (int Width, int Height) target) =>
        (source.Width >= source.Height) == (target.Width >= target.Height) &&
        Math.Abs(source.Width / (double)source.Height / (target.Width / (double)target.Height) - 1) <= .01;

    internal static CropRegion Apply(CropRegion crop, (int Width, int Height) source,
        (int Width, int Height) target)
    {
        if (Matches(source, target)) return crop.Clone();

        var ratio = (crop.Right - crop.Left) * source.Width / ((crop.Bottom - crop.Top) * source.Height);
        ratio = target.Width >= target.Height ? Math.Max(ratio, 1 / ratio) : Math.Min(ratio, 1 / ratio);
        var normalizedRatio = ratio / (target.Width / (double)target.Height);
        var width = Math.Min(1, normalizedRatio);
        var height = Math.Min(1, 1 / normalizedRatio);
        var left = Math.Clamp((crop.Left + crop.Right - width) / 2, 0, 1 - width);
        var top = Math.Clamp((crop.Top + crop.Bottom - height) / 2, 0, 1 - height);

        return new CropRegion { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }
}
