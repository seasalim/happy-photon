using System.Diagnostics;
using System.Text.Json;
using HappyPhoton.MlSpike;
using Microsoft.ML.OnnxRuntime;

namespace MlSpike.Harness;

internal static class StressModes
{
    internal static object Cancel(RunContext context)
    {
        using var l1 = JsonDocument.Parse(LocalFiles.Read(context.Required("latency-result")));
        var record = l1.RootElement;
        var identity = record.GetProperty("identity");
        if (record.GetProperty("mode").GetString() != "latency" ||
            record.GetProperty("status").GetString() != "measured" ||
            identity.GetProperty("manifest_sha256").GetString() != RunContext.FrozenManifest ||
            identity.GetProperty("model_sha256").GetString() != context.Config.ModelSha256.ToLowerInvariant() ||
            identity.GetProperty("environment").GetString() != context.Required("environment") ||
            identity.GetProperty("config_sha256").GetString() != LocalFiles.Hash(context.Required("config")) ||
            !record.GetProperty("result").GetProperty("rig_valid").GetBoolean())
            throw new InvalidDataException("C1 requires this model/environment's valid L1 result.");
        var median = record.GetProperty("result").GetProperty("median_seconds").GetDouble();
        if (!double.IsFinite(median) || median <= 0) throw new InvalidDataException("Invalid L1 median.");
        var input = context.Load(context.SelectedImage());
        using var session = context.Session();
        session.Infer(input);
        session.Infer(input);
        var probe = Enumerable.Range(0, 10).Select(_ => PerformanceModes.Measure(() => session.Infer(input))).ToArray();
        var mean = probe.Average();
        var cv = Math.Sqrt(probe.Average(v => Math.Pow(v - mean, 2))) / mean;
        if (cv > 0.10) return new { RigValid = false, ProbeCoefficientOfVariation = cv, Cycles = 0 };
        PerformanceModes.Collect();
        var before = PerformanceModes.Resident();
        var cycles = new List<object>();
        var validLatencies = new List<double>();
        for (var i = 0; i < 30; i++)
        {
            var fraction = new[] { 0.25, 0.50, 0.75 }[i % 3];
            using var options = new RunOptions();
            using var finished = new ManualResetEventSlim();
            long requested = 0, entered = 0, returned = 0;
            var start = Stopwatch.GetTimestamp();
            var canceller = new Thread(() =>
            {
                var delay = TimeSpan.FromSeconds(median * fraction) - Stopwatch.GetElapsedTime(start);
                if (delay > TimeSpan.Zero && finished.Wait(delay)) return;
                if (finished.IsSet) return;
                Interlocked.Exchange(ref requested, Stopwatch.GetTimestamp());
                options.Terminate = true;
            }) { IsBackground = true };
            canceller.Start();
            var terminated = false;
            try
            {
                session.Infer(input, options, () => entered = Stopwatch.GetTimestamp(),
                    () => returned = Stopwatch.GetTimestamp());
            }
            catch (OnnxRuntimeException error) when (
                error.Message.Contains("terminat", StringComparison.OrdinalIgnoreCase) && options.Terminate)
            {
                terminated = true;
            }
            finally
            {
                finished.Set();
                canceller.Join();
            }
            var valid = terminated && requested >= entered && entered > 0 && returned >= requested && requested > 0;
            double? milliseconds = valid ? Stopwatch.GetElapsedTime(requested, returned).TotalMilliseconds : null;
            if (milliseconds.HasValue) validLatencies.Add(milliseconds.Value);
            cycles.Add(new { Cycle = i + 1, Fraction = fraction, Terminated = terminated,
                RequestedDuringInference = valid, ReturnLatencyMs = milliseconds,
                RequestOffsetMs = requested > 0 ? Stopwatch.GetElapsedTime(start, requested).TotalMilliseconds : (double?)null });
        }
        PerformanceModes.Collect();
        return new { Cycles = cycles, RigValid = true, ProbeCoefficientOfVariation = cv,
            All30CancelledDuringInference = validLatencies.Count == 30,
            P95Ms = validLatencies.Count == 30 ? PerformanceModes.P95(validLatencies) : (double?)null,
            MaxMs = validLatencies.Count == 30 ? validLatencies.Max() : (double?)null,
            ResidentGrowthBytes = PerformanceModes.Resident() - before,
            LatencyResultSha256 = LocalFiles.Hash(context.Required("latency-result")) };
    }

    internal static object Interaction(RunContext context)
    {
        var sample = context.SelectedImage();
        using var source = PreviewInput.LoadBase(LocalFiles.Resolve(context.SampleRoot, sample.File));
        var input = PreviewInput.Render(source);
        using var session = context.Session();
        session.Infer(input);
        session.Infer(input);
        // Fixed 30-step sweep: -3 EV through +3 EV, inclusive, in all four blocks.
        var exposures = Enumerable.Range(0, 30).Select(i => -3.0 + 6.0 * i / 29).ToArray();
        var blocks = new List<double[]>();
        var inferenceCounts = new List<int>();
        for (var block = 0; block < 4; block++)
        {
            using var stop = new CancellationTokenSource();
            using var entered = new ManualResetEventSlim();
            using var options = new RunOptions();
            var count = 0;
            Task? worker = null;
            if (block % 2 == 1)
                worker = Task.Factory.StartNew(() =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        try { session.Infer(input, options, () => entered.Set()); Interlocked.Increment(ref count); }
                        catch (OnnxRuntimeException error) when (stop.IsCancellationRequested &&
                            error.Message.Contains("terminat", StringComparison.OrdinalIgnoreCase)) { break; }
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var values = new double[30];
            try
            {
                if (worker != null)
                {
                    // A bound on broken inference, not a measurement interval.
                    if (!entered.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Inference did not start.");
                }
                for (var step = 0; step < exposures.Length; step++)
                {
                    var start = Stopwatch.GetTimestamp();
                    _ = PreviewInput.Render(source, exposures[step]);
                    values[step] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                }
            }
            finally
            {
                stop.Cancel();
                options.Terminate = true;
                worker?.GetAwaiter().GetResult();
            }
            blocks.Add(values);
            inferenceCounts.Add(count);
        }
        var alone1 = PerformanceModes.P95(blocks[0]);
        var alone2 = PerformanceModes.P95(blocks[2]);
        var agreement = Math.Abs(alone1 - alone2) / alone1;
        var alone = PerformanceModes.P95(blocks[0].Concat(blocks[2]));
        var concurrent = PerformanceModes.P95(blocks[1].Concat(blocks[3]));
        return new { sample.Id, ExposuresEv = exposures, BlocksMs = blocks, InferencesCompleted = inferenceCounts,
            Order = "ABAB", AloneBlockP95Ms = new[] { alone1, alone2 }, AloneAgreementFraction = agreement,
            RigValid = agreement <= 0.10, AloneP95Ms = alone, WithInferenceP95Ms = concurrent, Ratio = concurrent / alone };
    }
}
