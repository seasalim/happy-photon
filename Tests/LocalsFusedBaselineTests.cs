using System.Diagnostics;
using System.Reflection;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

// Opt-in prototype baselines and Qualified* production locals gates. One test per fresh process.
// Set HAPPY_PHOTON_LOCALS_FIXTURE=raw|standard|synthetic; LOCALS_SAMPLES=1 for a bounded probe.
// Default samples=5. All settings neutral; full-tick/export retain production detail defaults.
// Observed 2026-09-05, c9e767442170c648c920732a682920d74f1e57bd, CPU=24, Release.
// These are observations for gate approval, NOT approved bounds. theta=30 degrees, local EV=+2.
// Rerun: ./Tests/RunLocalsBaseline.ps1 -Gate G1|G2|G5|G9|G9Stage -Fixture raw|standard|synthetic
// Each invocation is a fresh process, 120s ceiling; samples=5, or -Samples 1 for a probe.
// G1 RAW PID57444, coverage=.45: off/on/delta=11.0334/40.9134/29.8800 ms, 38.8577 ns/adjusted pixel.
// G1 HEIC PID44044, coverage=.45: off/on/delta=6.6066/32.1120/25.5054 ms, 29.5201 ns/adjusted pixel.
// Full production off ticks (coverage=0): RAW 28.0903 ms; HEIC 18.5903 ms, five medians, same PIDs.
// G1 timings drift with the specified one warmup: RAW on=52.7873,51.5381,40.9134,34.2349,34.3000 ms;
// HEIC on=63.8852,75.2319,32.1120,25.5002,25.8635 ms. Do not interpret as stable hot-kernel floors.
// G2 deltas at .20/.45/1: RAW PID42740=14.1218/27.7274/57.5557 ms (monotone).
// HEIC PID54696 at .20000052083333333/.45/1=31.4979/19.8160/33.3255 ms (NOT monotone; warmup drift).
// G5 frozen reference and on observations, five comparisons, mean/p99 display-sRGB deltaE:
// Fixture (PID)                  OFF coverage=0       f=.25 coverage=.45    f=.001 coverage=.45
// Canon RAW (52732)              1.663034/9.950900     2.762855/13.530304     3.282461/16.192560
// iPhone HEIC (31824)            0.941215/10.633908    1.151285/13.087108     1.397294/16.967430
// Synthetic (56172)              5.303898/28.787708    6.053626/31.183451     6.505908/31.426815
// Full-base adjusted fractions: RAW=.44999995042220453 both; HEIC=.44999995079050137/.4500000328063324;
// synthetic=.45 both. Stored normalized centers are preserved across differing base aspect ratios.
// G7 G1 paired incremental private peak delta median=0 MiB both; caller allocation median RAW=2720,
// HEIC=2592 bytes. This requested caller-thread metric excludes allocations on parallel workers.
// G9 export-off, coverage=0: RAW PID48836 three JPEG variants=2911.2468 ms; HEIC PID52572=865.4567 ms.
// G9Stage RAW PID51032 5496x3670, coverage=.44999995042220453: 72.9965/402.6928/329.6963 ms off/on/delta,
// 36.3236 ns/adjusted pixel. All timing medians use five samples after one warmup per arm.
public sealed partial class LocalsFusedBaselineTests(ITestOutputHelper output)
{
    private static readonly EditSettings Settings = new();
    private static int Samples => int.Parse(Environment.GetEnvironmentVariable("LOCALS_SAMPLES") ?? "5");
    private static string Fixture => Environment.GetEnvironmentVariable("HAPPY_PHOTON_LOCALS_FIXTURE") switch
    {
        "standard" => "iphone-14-pro-iso-1000.heic", "synthetic" => "synthetic-4000x2667",
        _ => "canon-eos-6d-iso-6400.cr2"
    };
    private static void OptIn()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in performance baseline");
        Assert.Equal("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_FULL_CPU"));
        PerfEnvironment.AssertFullCpu();
#if DEBUG
        Assert.Fail("Use Release");
#endif
    }
    private void Print(double coverage, string message) => output.WriteLine(
        $"fixture={Fixture} coverage={coverage:R} cpu={Environment.ProcessorCount} process={Environment.ProcessId} {message}");
    private static BaseLoaderRouter Loader() => new(new RawBaseLoader(), new StandardBaseLoader());
    private static BaseImage Load(bool full)
    {
        var path = GoldenTestPaths.Asset(Fixture);
        var attributes = File.GetAttributes(path); // Live check: never hydrate offline/recall-on-access fixtures.
        Assert.True(((int)attributes & (0x1000 | 0x40000 | 0x400000)) == 0, "Fixture must be locally available");
        var file = new ImageFile(path);
        return (full ? Loader().LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None)
            : Loader().LoadPreviewBase(file, BaseDecodeSettings.Default, CancellationToken.None))
            ?? throw new InvalidOperationException("Fixture decode failed");
    }
    [Fact] public void G1() { OptIn(); using var b = Load(false); Stage(b, .45); Tick(b); }
    [Fact] public void G2() { OptIn(); using var b = Load(false); foreach (var c in new[] { .20, .45, 1.0 }) Stage(b, c); }
    [Fact] public void G9Stage() { OptIn(); using var b = Load(true); Stage(b, .45); }
    private void Tick(BaseImage b)
    {
        void Run() { using var result = new RenderPipeline().Render(new(b, Settings, RenderIntent.Preview, 1600, new(false, false))); }
        Run();
        var times = Enumerable.Range(0, Samples).Select(_ => Time(Run)).ToArray();
        Print(0, $"full_tick_off_ms={Median(times):F4} samples={Samples} raw_ms=[{string.Join(',', times)}]");
    }
    private void Stage(BaseImage b, double coverage)
    {
        using var geometry = RenderGeometry.Apply(b.Pixels, Settings, out _);
        using var pixels = geometry.GetPixels();
        var source = pixels.GetArea(0, 0, geometry.Width, geometry.Height)!;
        Assert.Equal(3u, pixels.Channels);
        var values = new ushort[source.Length];
        var kernel = new Kernel(b.Info);
        var mask = Mask.Create((int)geometry.Width, (int)geometry.Height, .25, coverage);
        var fraction = mask.Fraction();
        var off = kernel.Production(values);
        source.CopyTo(values, 0); off();
        var expected = (ushort[])values.Clone();
        source.CopyTo(values, 0); kernel.Apply(values, mask, false);
        Assert.Equal(expected, values);
        // Also anchor the reflection-bound standard worker to its public image stage.
        kernel.ProductionImage(geometry);
        Assert.Equal(expected, pixels.GetArea(0, 0, geometry.Width, geometry.Height));
        Print(fraction, $"bypass_differing_codes=0 width={geometry.Width} height={geometry.Height} {mask}");
        Action on = () => kernel.Apply(values, mask, true);
        source.CopyTo(values, 0); off(); source.CopyTo(values, 0); on();
        var a = new double[Samples]; var z = new double[Samples];
        var memory = new double[Samples]; var allocated = new double[Samples];
        for (var i = 0; i < Samples; i++)
        {
            source.CopyTo(values, 0); var before = Measure(off);
            source.CopyTo(values, 0); var after = Measure(on);
            a[i] = before.Ms; z[i] = after.Ms; memory[i] = (after.Peak - before.Peak) / 1048576.0;
            allocated[i] = after.Allocated;
        }
        var delta = Median(z) - Median(a);
        Print(fraction, $"stage_off_ms={Median(a):F4} on_ms={Median(z):F4} delta_ms={delta:F4} " +
            $"ns_per_adjusted_pixel={delta * 1e6 / (source.Length / 3 * fraction):F4} samples={Samples} " +
            $"paired_delta_ms={Median(z.Zip(a, (onMs, offMs) => onMs - offMs).ToArray()):F4} " +
            $"private_increment_delta_MiB={Median(memory):F4} caller_alloc_bytes={Median(allocated):F0} " +
            $"off_samples=[{string.Join(',', a)}] on_samples=[{string.Join(',', z)}] " +
            $"private_delta_samples=[{string.Join(',', memory)}] caller_alloc_samples=[{string.Join(',', allocated)}]");
    }
    private static (double Ms, long Peak, long Allocated) Measure(Action operation)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var process = Process.GetCurrentProcess(); process.Refresh();
        var baseline = process.PrivateMemorySize64; var peak = baseline;
        using var stop = new ManualResetEventSlim();
        // Deliberate 10ms sampling cadence, not a sleep-based correctness assertion.
        var sampler = Task.Run(() => { while (!stop.Wait(10)) { process.Refresh(); peak = Math.Max(peak, process.PrivateMemorySize64); } });
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        double ms;
        try { ms = Time(operation); allocated = GC.GetAllocatedBytesForCurrentThread() - allocated; }
        finally { stop.Set(); sampler.GetAwaiter().GetResult(); }
        process.Refresh(); peak = Math.Max(peak, process.PrivateMemorySize64);
        return (ms, Math.Max(0, peak - baseline), allocated);
    }
    private static double Time(Action action) { var start = Stopwatch.GetTimestamp(); action(); return Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    private static double Median(double[] values) => values.Order().ElementAt(values.Length / 2);

    [Fact] public void G5()
    {
        OptIn();
        using var full = Fixture.StartsWith("synthetic") ? Synthetic() : Load(true);
        using var preview = Fixture.StartsWith("synthetic") ? SyntheticPreview(full) : Load(false);
        // feather < 0 is the control arm: locals off, global Exposure +2 through the production stage.
        foreach (var feather in new[] { 0.0, -1.0, .25, .001 })
        {
            var settings = feather < 0 ? new EditSettings { Exposure = 2 } : Settings;
            var on = feather > 0;
            var mask = Mask.Create((int)preview.Pixels.Width, (int)preview.Pixels.Height, on ? feather : .25, .45);
            var means = new double[Samples]; var p99 = new double[Samples];
            for (var i = 0; i < Samples; i++)
            {
                using var p = Geometric(preview, mask, on, settings);
                var fullMask = mask.ForSize((int)full.Pixels.Width, (int)full.Pixels.Height);
                using var e = Geometric(full, fullMask, on, settings);
                WysiwygTests.AlignForComparison(e, p);
                var comparison = GoldenImageComparer.Compare(e, p, GoldenComparisonDomain.DisplaySrgb);
                means[i] = comparison.MeanDeltaE; p99[i] = comparison.P99DeltaE;
            }
            var label = feather < 0 ? "global+2EV-control" : feather == 0 ? "off" : "on";
            Print(on ? mask.Fraction() : 0, $"G5 case={label} feather={feather:R} mean_deltaE={Median(means):F6} p99_deltaE={Median(p99):F6} samples={Samples} " +
                $"full_coverage={(on ? mask.ForSize((int)full.Pixels.Width, (int)full.Pixels.Height).Fraction() : 0):R} {mask}");
        }
    }
    private static MagickImage Geometric(BaseImage b, Mask mask, bool on, EditSettings settings)
    {
        using var geometry = RenderGeometry.Apply(b.Pixels, settings, out _);
        var kernel = new Kernel(b.Info, settings);
        if (!on) kernel.ProductionImage(geometry);
        else
        {
            using var pixels = geometry.GetPixels();
            var values = pixels.GetArea(0, 0, geometry.Width, geometry.Height)!;
            Assert.Equal(3u, pixels.Channels); kernel.Apply(values, mask, true);
            pixels.SetArea(0, 0, geometry.Width, geometry.Height, values);
        }
        RenderColorEncoding.RetagAsSrgb(geometry);
        return RenderFinalizer.Finalize(geometry, null, OutputColorSpace.Srgb, OutputSharpeningMode.Off, false, effects: null);
    }
    private static BaseImage Synthetic()
    {
        var v = new ushort[4000 * 2667 * 3];
        for (var y = 0; y < 2667; y++) for (var x = 0; x < 4000; x++)
        {
            var high = ((x / 16 + y / 16) & 1) == 0;
            for (var c = 0; c < 3; c++) v[(y * 4000 + x) * 3 + c] =
                (ushort)(x < 500 ? 0 : x > 3500 ? 65535 : y < 300 ? (c == 0 ? 65535 : 0) : high ? 60000 : 1000);
        }
        return RenderPipelineTestSupport.CreateBase(v, isRaw: true, height: 2667);
    }
    private static BaseImage SyntheticPreview(BaseImage full)
    {
        var image = new MagickImage(full.Pixels);
        // ResizeInLinearLight expects encoded input: encode the linear base and decode its output.
        ToneLutApplicator.Apply(image, Enumerable.Range(0, 65536).Select(i => ToneLut.SrgbEncode(i / 65535.0)).ToArray());
        RenderColorEncoding.ResizeInLinearLight(image, 1600);
        ToneLutApplicator.Apply(image, Enumerable.Range(0, 65536).Select(i => ToneLut.SrgbDecode(i / 65535.0)).ToArray());
        return new BaseImage(image, full.Info);
    }
    [Fact] public async Task G9()
    {
        OptIn();
        using (Load(false)) { } // Availability/decode check outside export timing.
        using var directory = new TemporaryDirectory();
        var raw = Fixture.EndsWith("cr2");
        var settings = new ExportSettings { OutputFolder = directory.Path, Format = ExportFormat.Jpeg,
            Quality = 85, OutputColorSpace = OutputColorSpace.Srgb, ExportWeb = raw, ExportSmall = raw,
            WebMaxSize = 2048, SmallMaxSize = 1024 };
        settings.OutputSharpening = raw ? OutputSharpeningMode.Screen : OutputSharpeningMode.Off;
        var service = new ImageExportService(new RenderPipeline(), Loader(), new ExportMetadataService());
        var file = new ImageFile(GoldenTestPaths.Asset(Fixture)) { EditSettings = new EditSettings() };
        var times = new double[Samples];
        for (var i = -1; i < Samples; i++)
        {
            if (i >= 0) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            settings.NamingPattern = "{name}-" + (i + 1);
            var start = Stopwatch.GetTimestamp();
            var result = await service.ExportBatchAsync([file], settings);
            Assert.True(result.ExportedCount == 1, string.Join(';', result.FailedTargets.Select(t => t.FailureReason)));
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Print(0, $"export_sample={i} wall_ms={ms:F4}");
            if (i >= 0) times[i] = ms;
        }
        Print(0, $"export_off_median_ms={Median(times):F4} samples={Samples} variants={(raw ? 3 : 1)}");
    }
    private readonly record struct Mask(int Width, int Height, double Feather, double Cx, double Cy, bool Full)
    {
        private static readonly double Cos = Math.Cos(Math.PI / 6);
        private static readonly double Sin = Math.Sin(Math.PI / 6);
        public double Gain(int pixel)
        {

            var l = Math.Max(Width, Height);
            var s = ((pixel % Width + .5) / l - Cx) * Cos + ((pixel / Width + .5) / l - Cy) * Sin;
            var t = Math.Clamp((s + Feather / 2) / Feather, 0, 1);
            return 1 + (1 - t * t * (3 - 2 * t)) * 3;
        }
        public Mask ForSize(int w, int h) => this with { Width = w, Height = h,
            Cx = Cx * Math.Max(Width, Height) / Width * w / Math.Max(w, h),
            Cy = Cy * Math.Max(Width, Height) / Height * h / Math.Max(w, h) };
        public double Fraction()
        {
            var count = 0; for (var p = 0; p < Width * Height; p++) if (Gain(p) != 1) count++;
            return count / (double)(Width * Height);
        }
        public static Mask Create(int w, int h, double f, double coverage)
        {
            if (coverage == 1) return new(w, h, f, 1.5 * w / Math.Max(w, h), 1.5 * h / Math.Max(w, h), true);
            // Solve projected rectangle area analytically, then report exact pixel-center coverage.
            double lo = -2, hi = 2;
            var l = Math.Max(w, h); var a = w / (double)l * Cos; var b = h / (double)l * Sin;
            for (var i = 0; i < 60; i++)
            {
                var q = (lo + hi) / 2;
                double Sq(double x) => Math.Pow(Math.Max(x, 0), 2);
                var area = (Sq(q) - Sq(q - a) - Sq(q - b) + Sq(q - a - b)) / (2 * a * b);
                if (area < coverage) lo = q; else hi = q;
            }
            var cy = h / (double)l / 2;
            return new(w, h, f, ((lo + hi) / 2 - f / 2 - cy * Sin) / Cos, cy, false);
        }
    }
    // Adapted from AgxCrossing, ToneLutApplicator and prototype candidate A; scalar registers, no mask frame.
    private sealed class Kernel
    {
        private readonly bool raw;
        private readonly AgxToneParameters parameters;
        private readonly ToneParams standard;
        private readonly AgxCrossing crossing;
        private readonly double[,] matrix;
        private readonly DcpRenderMatrix input, unnormalized, outset = new(AgxToneEngine.OutsetMatrix);
        private readonly ToneLuts luts;
        private readonly double gain, slope, toe, shoulder;
        public Kernel(BaseImageInfo info, EditSettings? settings = null)
        {
            settings ??= Settings;
            raw = info.IsRawSource;
            var wb = RenderChromaticStage.CreateWhiteBalanceMatrix(info, settings);
            parameters = new(settings.Exposure, info.SourceExposureBiasEv, 0, 0, 0, settings.Curve);
            crossing = new(parameters, wb);
            var m = raw ? ChromaticAdaptation.Multiply(AgxToneEngine.InsetMatrix, wb) : wb;
            var normalized = ChromaticAdaptation.NormalizeForRender(m);
            matrix = normalized.Matrix; input = new(matrix); unnormalized = new(m);
            standard = new(settings.Exposure + info.SourceExposureBiasEv, normalized.Fold, 0, 0, 0, 0, false, settings.Curve);
            luts = raw ? AgxToneLut.ComposeCached(parameters, normalized.Fold) : ToneLut.ComposeCached(standard);
            gain = Math.Pow(2, settings.Exposure + info.SourceExposureBiasEv); slope = AgxToneEngine.Slope(0);
            toe = AgxToneEngine.ToePower(0); shoulder = AgxToneEngine.ShoulderPower(0);
        }
        public void ProductionImage(MagickImage image)
        { if (raw) crossing.Apply(image); else ToneLutApplicator.Apply(image, matrix, luts); }
        public Action Production(ushort[] values)
        {
            if (raw) return () => crossing.Apply(values);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var worker = typeof(ToneLutApplicator).GetNestedTypes(BindingFlags.NonPublic).SelectMany(t => t.GetMethods(flags))
                .Single(m => m.Name.StartsWith("<ApplyCore>b__", StringComparison.Ordinal));
            var closure = Activator.CreateInstance(worker.DeclaringType!, nonPublic: true)!;
            var count = values.Length / 3; var workers = Math.Min(Environment.ProcessorCount, Math.Max(1, count / 8192));
            var fields = new Dictionary<string, object?> { ["values"] = values, ["pixelCount"] = count, ["channels"] = 3u,
                ["red"] = 0, ["green"] = 1, ["blue"] = 2, ["workers"] = workers, ["matrix"] = matrix, ["luts"] = luts, ["execution"] = null };
            foreach (var field in worker.DeclaringType!.GetFields(flags))
            {
                Assert.True(fields.TryGetValue(field.Name, out var value), $"Unexpected worker field {field.Name}");
                field.SetValue(closure, value is IConvertible ? Convert.ChangeType(value, field.FieldType) : value);
            }
            var body = worker.CreateDelegate<Action<int>>(closure);
            return () => Parallel.For(0, workers, new ParallelOptions(), body);
        }
        public void Apply(ushort[] values, Mask mask, bool enabled)
        {
            var count = values.Length / 3;
            var workers = Math.Min(Environment.ProcessorCount, Math.Max(1, raw ? (count + 32767) / 32768 : count / 8192));
            Parallel.For(0, workers, new ParallelOptions(), worker =>
            {
                var start = count * worker / workers; var end = count * (worker + 1) / workers;
                for (var p = start; p < end; p++)
                {
                    var o = p * 3; var g = enabled ? mask.Gain(p) : 1;
                    var r = raw ? values[o] * (1.0 / 65535) : values[o] / 65535.0;
                    var green = raw ? values[o + 1] * (1.0 / 65535) : values[o + 1] / 65535.0;
                    var b = raw ? values[o + 2] * (1.0 / 65535) : values[o + 2] / 65535.0;
                    double tr, tg, tb;
                    if (g == 1)
                    {
                        tr = Lut(luts.Red, input.Row0(r, green, b)); tg = Lut(luts.Green, input.Row1(r, green, b));
                        tb = Lut(luts.Blue, input.Row2(r, green, b));
                    }
                    else if (raw)
                    {
                        tr = Tone(Math.Max(0, unnormalized.Row0(r, green, b) * g) * gain);
                        tg = Tone(Math.Max(0, unnormalized.Row1(r, green, b) * g) * gain);
                        tb = Tone(Math.Max(0, unnormalized.Row2(r, green, b) * g) * gain);
                    }
                    else
                    {
                        var scale = standard.Fold * gain * g;
                        tr = Tone(input.Row0(r, green, b) * scale); tg = Tone(input.Row1(r, green, b) * scale);
                        tb = Tone(input.Row2(r, green, b) * scale);
                    }
                    values[o] = Code(raw ? ToneLut.SrgbEncode(Math.Clamp(outset.Row0(tr, tg, tb), 0, 1)) : tr);
                    values[o + 1] = Code(raw ? ToneLut.SrgbEncode(Math.Clamp(outset.Row1(tr, tg, tb), 0, 1)) : tg);
                    values[o + 2] = Code(raw ? ToneLut.SrgbEncode(Math.Clamp(outset.Row2(tr, tg, tb), 0, 1)) : tb);
                }
            });
        }
        private double Lut(double[] lut, double v) => raw ? AgxToneLut.InterpolateUnchecked(lut, Math.Clamp(v, 0, 1)) : ToneLutApplicator.Interpolate(lut, v);
        private double Tone(double exposed)
        {
            if (!raw) return ToneLut.Evaluate(standard with { ExposureEv = 0, Fold = 1 }, exposed);
            var x = exposed <= 0 ? 0 : Math.Clamp((Math.Log2(exposed) - Math.Log2(.18) + 10) / 16.5, 0, 1);
            var u = AgxToneEngine.EvaluateSigmoid(x, slope, toe, shoulder);
            return ToneLut.SrgbDecode(Math.Clamp(ToneLut.EvaluateComposedCurve(parameters.Curve, null,
                ToneLut.SrgbEncode(Math.Pow(u, 2.2))), 0, 1));
        }
        private static ushort Code(double v) => (ushort)Math.Round(Math.Clamp(v, 0, 1) * 65535, MidpointRounding.AwayFromZero);
    }
}




