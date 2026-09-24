namespace HappyPhoton.Tests;

// Test-side contract types, unrelated to LocalAdjustment's production JSON.
internal readonly record struct BrushPoint(double U, double V);
internal sealed record BrushStroke(BrushPoint[] Points, double Radius, double Feather, double Flow, bool Erase);
internal sealed record BrushDocument(BrushStroke[] Strokes);
