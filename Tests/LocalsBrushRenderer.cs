using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

// Full test-only pipeline: geometry, per-render index, fused brush/tone, chroma,
// default detail, finalization. Never adds a separately timed weight pass to a production tick.
internal sealed class LocalsBrushRenderer
{
    internal double IndexMilliseconds { get; private set; }
    internal string BuildReport { get; private set; } = "brushes-off";
    internal double KernelMilliseconds { get; private set; }

    internal MagickImage Display(BaseImage basis, EditSettings settings, BrushDocument[]? documents, RenderIntent intent)
    {
        var working = RenderGeometry.Apply(basis.Pixels, settings, out _);
        try
        {
            var plan = documents == null ? null : new LocalsBrushPlan(documents, settings, basis.Info,
                (int)working.Width, (int)working.Height);
            IndexMilliseconds = plan?.IndexMilliseconds ?? 0;
            BuildReport = plan?.BuildReport ?? "brushes-off";
            var start = Stopwatch.GetTimestamp();
            LocalsBrushKernel.Apply(working, basis.Info, settings, plan);
            KernelMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            RenderColorEncoding.RetagAsSrgb(working);
            if (!basis.Info.IsMonochrome) RenderChromaStage.Apply(working, settings);
            RenderNoiseReduction.Apply(working, basis.Info, settings.Detail);
            RenderSharpening.ApplyCapture(working, basis.Info, settings.Detail, intent);
            return working;
        }
        catch { working.Dispose(); throw; }
    }

    internal MagickImage Tick(BaseImage basis, EditSettings settings, BrushDocument[]? documents)
    {
        using var display = Display(basis, settings, documents, RenderIntent.Preview);
        return RenderFinalizer.Finalize(display, 1600, OutputColorSpace.Srgb, OutputSharpeningMode.Off,
            false, effects: settings.Effects);
    }
}
