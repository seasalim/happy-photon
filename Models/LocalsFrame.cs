namespace HappyPhoton.Models;

public readonly record struct LocalsFrame(double Width, double Height,
    double CropX, double CropY, double CropWidth, double CropHeight)
{
    public double LongEdge => Math.Max(Width, Height);
}
