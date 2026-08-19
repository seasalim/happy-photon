namespace HappyPhoton.Models;

public sealed class WaveformData
{
    public const int ColumnCount = 256;
    public const int LevelCount = 128;

    public ushort[] Luminance { get; } =
        new ushort[ColumnCount * LevelCount];
    public ushort[] ColumnSampleCounts { get; } = new ushort[ColumnCount];
}
