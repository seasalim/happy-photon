using System.Diagnostics;
using System.Runtime.CompilerServices;
using HappyPhoton.MlSpike;

namespace MlSpike.Harness;

internal static class PerformanceModes
{
    internal static double Median(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        return values.Length % 2 == 0 ? (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2
            : values[values.Length / 2];
    }

    internal static double P95(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        return values[(int)Math.Ceiling(values.Length * 0.95) - 1];
    }

    internal static double Measure(Func<byte[]> action)
    {
        var start = Stopwatch.GetTimestamp();
        _ = action();
        return Stopwatch.GetElapsedTime(start).TotalSeconds;
    }

    internal static object Latency(RunContext context)
    {
        var samples = context.Edges();
        var inputs = samples.Select(context.Load).ToArray();
        using var session = context.Session();
        session.Infer(inputs[0]);
        session.Infer(inputs[0]);
        var probe = Enumerable.Range(0, 10).Select(_ => Measure(() => session.Infer(inputs[0]))).ToArray();
        var mean = probe.Average();
        var cv = Math.Sqrt(probe.Average(v => Math.Pow(v - mean, 2))) / mean;
        var seconds = inputs.Select(input => Measure(() => session.Infer(input))).ToArray();
        return new { Images = samples.Select((s, i) => new { s.Id, Seconds = seconds[i] }),
            ProbeImage = samples[0].Id, ProbeSeconds = probe, ProbeCoefficientOfVariation = cv,
            RigValid = cv <= 0.10, MedianSeconds = Median(seconds), MaxSeconds = seconds.Max(),
            TimingBoundary = "BGRA8 -> preprocess -> CPU inference -> resized binary mask; excludes decode and disk" };
    }

    internal static object Cold(RunContext context)
    {
        var sample = context.SelectedImage();
        var input = context.Load(sample);
        var started = Stopwatch.GetTimestamp();
        using var session = context.Session();
        var mask = session.Infer(input);
        var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        MaskFiles.Write(Path.Combine(context.Output, sample.Id + ".png"), mask, input.Width, input.Height);
        return new { sample.Id, Seconds = seconds, ProcessId = Environment.ProcessId,
            Boundary = "session creation + first BGRA8-to-mask; invoke in three fresh processes" };
    }

    internal static object Memory(RunContext context)
    {
        var inputs = context.Edges().Select(context.Load).ToArray();
        var runs = new List<MemoryRun>();
        for (var i = 0; i < 2; i++)
        {
            Collect();
            var before = Resident();
            long peak;
            using (var sampler = new ResidentSampler())
            {
                MemoryWorkload(context, inputs);
                peak = Math.Max(sampler.Peak, Resident());
            }
            Collect();
            runs.Add(new MemoryRun(before, peak, Resident()));
        }
        var added = runs.Select(r => (double)(r.PeakBytes - r.BeforeBytes)).ToArray();
        var scale = added[0];
        var agreement = scale <= 0 ? double.MaxValue : Math.Abs(added[0] - added[1]) / scale;
        return new { Runs = runs, PeakAgreementFraction = agreement, RigValid = agreement <= 0.10,
            SamplingIntervalMs = 5, Method = "sampled process resident set; baseline includes frozen BGRA buffers",
            FinalGrowthFromFirstBaselineBytes = runs[1].AfterGcBytes - runs[0].BeforeBytes };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MemoryWorkload(RunContext context, PreviewInput[] inputs)
    {
        using var session = context.Session();
        session.Infer(inputs[0]);
        session.Infer(inputs[0]);
        foreach (var input in inputs) session.Infer(input);
    }

    internal static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    internal static long Resident()
    {
        using var process = Process.GetCurrentProcess();
        return process.WorkingSet64;
    }

    internal sealed record MemoryRun(long BeforeBytes, long PeakBytes, long AfterGcBytes)
    {
        public long PeakAddedBytes => PeakBytes - BeforeBytes;
        public long RetainedBytes => AfterGcBytes - BeforeBytes;
    }

    private sealed class ResidentSampler : IDisposable
    {
        private readonly ManualResetEventSlim _stop = new();
        private readonly Thread _thread;
        private long _peak;
        public long Peak => Interlocked.Read(ref _peak);

        public ResidentSampler()
        {
            _peak = Resident();
            _thread = new Thread(() =>
            {
                do { Interlocked.Exchange(ref _peak, Math.Max(Peak, Resident())); }
                while (!_stop.Wait(5));
            }) { IsBackground = true };
            _thread.Start();
        }

        public void Dispose()
        {
            _stop.Set();
            _thread.Join();
            _stop.Dispose();
        }
    }
}
