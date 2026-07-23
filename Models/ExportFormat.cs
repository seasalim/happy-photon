namespace HappyPhoton.Models;

public enum ExportFormat { Jpeg, Png, Webp }

/// <summary>One export output size. MaxDimension null = original size.</summary>
public record ExportVariant(string Name, int? MaxDimension);
