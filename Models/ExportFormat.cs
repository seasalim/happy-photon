namespace HappyPhoton.Models;

public enum ExportFormat { Jpeg, Png, Webp, Tiff }

/// <summary>One export output size. MaxDimension null = original size.</summary>
public record ExportVariant(string Name, int? MaxDimension);
