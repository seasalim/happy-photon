namespace HappyPhoton.Models;

/// <summary>
/// Holds histogram data for RGB channels (256 values each, 0-255).
/// </summary>
public class HistogramData
{
    public int[] Red { get; } = new int[256];
    public int[] Green { get; } = new int[256];
    public int[] Blue { get; } = new int[256];
    public int[] Luminance { get; } = new int[256];

    public int MaxValue { get; private set; }

    public void Normalize()
    {
        MaxValue = 1;

        // Exclude bins 0 and 255 from max calculation
        // These bins accumulate clipped shadows/highlights and would
        // dominate the scale, making the rest of the histogram look "crunched"
        for (int i = 1; i < 255; i++)
        {
            MaxValue = Math.Max(MaxValue, Red[i]);
            MaxValue = Math.Max(MaxValue, Green[i]);
            MaxValue = Math.Max(MaxValue, Blue[i]);
        }
    }
}
