using System.Collections.Concurrent;
using System.Runtime;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class HealGateTests
{
    [WindowsFact]
    public async Task NativeRetentionDiagnostic()
    {
        OptIn();
        RequireWic();
        Assert.Equal(8, IntPtr.Size);
        using var resources = new HealMagickResourceProbe();
        var snapshots = new List<object>();
        void Capture(string stage) => snapshots.Add(new
        {
            stage,
            memory = HealNativeMemoryProbe.Capture(),
            magick = resources.Drain(),
            scratch = PipelineScratchBytes()
        });
        var loader = Loader();
        var prototype = new HealProductionStage();
        var production = Environment.GetEnvironmentVariable("HAPPY_PHOTON_HEAL_MEMORY_ARM") == "production";
        using var pair = loader.LoadPreviewBaseWithOutcome(LocalFile(Raw),
            BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair);
        using var fit = FitBitmap(pair.Interactive, prototype);
        Capture("fit");

        var full = loader.LoadFullBase(LocalFile(Raw), BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(full);
        try
        {
            Capture("full-loaded-context-closed");
            var displayed = RenderMemoryBitmap(full, prototype, production);
            try
            {
                Capture("refined");
                var replacement = RenderMemoryBitmap(full, prototype, production);
                displayed.Dispose();
                displayed = replacement;
                Capture("warm-refined");
            }
            finally
            {
                displayed.Dispose();
            }
        }
        finally
        {
            full.Dispose();
        }

        // test-wait-policy: allow - WP5 G7 explicitly samples five seconds after release.
        await Task.Delay(TimeSpan.FromSeconds(5));
        Capture("released-5s-no-gc");
        // Magick TrimMemory is unsupported on Windows; probe the owning heaps directly.
        var optimized = HealNativeMemoryProbe.OptimizeHeaps();
        Capture("heap-optimize");

        // Attribution only, after the uncollected G7 observation. Never gate samples.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        Capture("forced-gc-diagnostic-only");
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        Capture("aggressive-gc-diagnostic-only");
        Report("native-retention", new
        {
            diagnostic = true, production, configuration = GC.GetConfigurationVariables(), snapshots, optimized,
            magickLimits = new { ResourceLimits.Memory, ResourceLimits.Area, ResourceLimits.Disk, ResourceLimits.Thread }
        });
        GC.KeepAlive(fit);
        GC.KeepAlive(pair);
        GC.KeepAlive(prototype);
    }

    private static Avalonia.Media.Imaging.Bitmap RenderMemoryBitmap(BaseImage full,
        HealProductionStage prototype, bool production)
    {
        if (!production) return RefinedBitmap(full, prototype);

        var settings = HealWorkloads.LH8();
        settings.Repairs = HealWorkloads.Repairs(HealWorkloads.S64());
        using var rendered = new RenderPipeline().RenderResting(new(full, settings, RenderIntent.Export,
            null, new(false, false)), RenderExecutionOptions.Resting(CancellationToken.None, Environment.ProcessorCount));
        return BitmapConversionService.ConvertToBitmap(rendered.Image)!;
    }

    [WindowsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeContextLifetimeDiagnostic(bool recycle)
    {
        OptIn();
        Assert.Equal(8, IntPtr.Size);
        var snapshots = new List<object>();
        void Capture(string stage) => snapshots.Add(new { stage, memory = HealNativeMemoryProbe.Capture() });
        var file = LocalFile(Raw);
        Capture("before-open");
        using (var context = LibRawContext.Open(file.FilePath))
        {
            context.Unpack();
            context.ConfigureOutput(RawBaseLoader.ConfigureOutput(BaseDecodeSettings.Default, preview: false));
            context.Process();
            Capture("processed-context-live");
            using (var processed = context.MakeProcessedImage())
            {
                Assert.Equal(16u, processed.Description.BitsPerSample);
                Capture("processed-image-live");
            }
            Capture("processed-image-disposed");
            if (recycle)
            {
                context.Recycle();
                Capture("context-recycled");
            }
        }
        Capture("context-closed");
        Report("native-context-lifetime", new { diagnostic = true, recycle, snapshots });
    }
}

internal sealed class HealMagickResourceProbe : IDisposable
{
    private readonly ConcurrentQueue<string> events = new();

    internal HealMagickResourceProbe()
    {
        MagickNET.Log += OnLog;
        MagickNET.SetLogEvents(LogEventTypes.Resource | LogEventTypes.Cache);
    }

    internal string[] Drain()
    {
        var result = new List<string>();
        while (events.TryDequeue(out var message)) result.Add(message);
        return result.ToArray();
    }

    private void OnLog(object? sender, LogEventArgs args) => events.Enqueue(args.Message);

    public void Dispose()
    {
        MagickNET.SetLogEvents(LogEventTypes.None);
        MagickNET.Log -= OnLog;
    }
}
