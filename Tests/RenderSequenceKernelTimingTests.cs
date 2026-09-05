using System.Diagnostics;
using System.Reflection;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

// Run alone in a fresh Release process; MeasureRenderSequence.ps1 supplies a hard timeout.
public sealed class RenderSequenceKernelTimingTests(ITestOutputHelper output)
{
    private const int PixelCount = 1600 * 1067;
    private const int Samples = 30;
    private const int Passes = 3;
    // Before-vs-before at 73d631c (HEAD 5aec65c, production unchanged), 2026-09-05.
    // Five fresh Release processes, FULL_CPU=1, CPU=24, min-of-30 three-pass samples.
    // Process log: process kernel ratio controlRatio sensitivityRatio disposition.
    // 1 crossing 1.011340246 0.991833125 1.323467844 SURVIVES
    // 1 matrix-lut 0.996637026 0.988926215 1.315674425 SURVIVES
    // 1 lut-only 0.975864843 0.992079070 1.335331377 SURVIVES
    // 2 crossing 0.968492399 1.015644831 1.328803103 SURVIVES
    // 2 matrix-lut 0.977180547 0.974622878 1.327721993 DISCARDED (>2% control)
    // 2 lut-only 0.996989154 1.004927850 1.349349361 SURVIVES
    // 3 crossing 1.020484771 1.002447485 1.338660317 SURVIVES
    // 3 matrix-lut 1.012991664 1.007967312 1.354226767 SURVIVES
    // 3 lut-only 0.992424677 0.987333935 1.346046736 SURVIVES
    // 4 crossing 0.987720604 0.991550593 1.364878577 SURVIVES
    // 4 matrix-lut 0.983005125 1.019592788 1.366112374 SURVIVES
    // 4 lut-only 0.987472189 1.026326311 1.335797821 DISCARDED (>2% control)
    // 5 crossing 1.009290362 0.980594047 1.328511143 SURVIVES
    // 5 matrix-lut 1.002691477 0.989432496 1.397506027 SURVIVES
    // 5 lut-only 0.996316738 1.003817952 1.315510250 SURVIVES
    // Surviving counts: crossing 5, matrix-lut 4, lut-only 4.
    // Resolution (2*floor): 0.063015203, 0.033989749, 0.048270314 respectively.
    // Every survivor rejects sensitivity against its final 1+2*floor bound.
    // Floors use every survivor; these observations still require user approval.
    private static readonly Dictionary<string, double> Floors = new()
    {
        ["crossing"] = Math.Abs(27.692200 / 28.593100 - 1),
        ["matrix-lut"] = Math.Abs(10.203200 / 10.379600 - 1),
        ["lut-only"] = Math.Abs(8.612300 / 8.825300 - 1)
    };

    [Fact]
    public void MeasurePairedKernels()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Opt in with HAPPY_PHOTON_PERF=1 and HAPPY_PHOTON_FULL_CPU=1.");
#if DEBUG
        Assert.Skip("Kernel measurements require Release.");
#endif
        Assert.Equal("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_FULL_CPU"));
        var source = new ushort[PixelCount * 3];
        uint state = 0x9E3779B9;
        for (var i = 0; i < source.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            source[i] = (ushort)state;
        }
        var values = new ushort[source.Length];
        var parameters = AgxToneEnginePropertyTests.Parameters(contrast: 25);
        var frozen = new FrozenBaseAgxCrossing(parameters);
        var current = new AgxCrossing(parameters);
        Action crossingA = () => frozen.Apply(values);
        Action crossingB = () => current.Apply(values);
        var lut = Enumerable.Range(0, ToneLut.Length)
            .Select(i => Math.Pow(i / 65535.0, 0.8)).ToArray();
        var luts = new ToneLuts(lut, lut, lut);
        double[,] matrix = { { 0.91, 0.06, 0.03 }, { 0.02, 0.95, 0.03 }, { 0.04, 0.02, 0.94 } };
        output.WriteLine($"CPU={Environment.ProcessorCount}; frame=1600x1067; samples={Samples}; passes={Passes}; statistic=minimum");
        Measure("crossing", crossingA, crossingB, crossingA, source, values);
        Measure("matrix-lut",
            ToneLoop(typeof(FrozenBaseToneLutApplicator), values, matrix, luts),
            ToneLoop(typeof(ToneLutApplicator), values, matrix, luts),
            crossingA, source, values);
        Measure("lut-only",
            ToneLoop(typeof(FrozenBaseToneLutApplicator), values, null, luts),
            ToneLoop(typeof(ToneLutApplicator), values, null, luts),
            crossingA, source, values);
    }

    private void Measure(string name, Action a, Action b, Action control,
        ushort[] source, ushort[] values)
    {
        // Reset outside the timer: every arm receives exactly the same Q16 input.
        // Control is paired too, bracketing A/B; its ratio detects within-run drift.
        for (var warm = 0; warm < 8; warm++)
        {
            Run(a); Run(b); Run(control);
        }
        source.CopyTo(values, 0);
        a();
        var expected = (ushort[])values.Clone();
        source.CopyTo(values, 0);
        b();
        Assert.Equal(expected, values);
        var times = Enumerable.Range(0, 5).Select(_ => new double[Samples]).ToArray();
        for (var sample = 0; sample < Samples; sample++)
        {
            times[2][sample] = Run(control);
            times[0][sample] = Run(a);
            times[1][sample] = Run(b);
            times[3][sample] = Run(control);
            // Sensitivity = the same kernel with one extra complete frame pass.
            times[4][sample] = Run(b, Passes + 1);
        }
        var minima = times.Select(t => t.Min()).ToArray();
        var baseline = Environment.GetEnvironmentVariable("HAPPY_PHOTON_BASELINE") == "1";
        var floor = Floors[name];
        var ratio = minima[1] / minima[0];
        var controlRatio = minima[3] / minima[2];
        var sensitivityRatio = minima[4] / minima[0];
        var valid = Math.Abs(controlRatio - 1) <= (baseline ? 0.02 : Math.Max(floor, 0.01));
        var threshold = Math.Min(1.05, 1 + 2 * floor);
        output.WriteLine($"KERNEL {name} A={minima[0]:F6} B={minima[1]:F6} " +
            $"controlA={minima[2]:F6} controlB={minima[3]:F6} sensitivity={minima[4]:F6} ms " +
            $"ratio={minima[1] / minima[0]:F9} controlRatio={minima[3] / minima[2]:F9} " +
            $"sensitivityRatio={minima[4] / minima[0]:F9} deviation={Math.Abs(minima[1] / minima[0] - 1):F9}");
        output.WriteLine($"GATE {name} baseline={baseline} floor={floor:F9} resolution={2 * floor:F9} threshold={threshold:F9} " +
            $"control={(valid ? "VALID" : "INVALID: repeat without widening floor")} " +
            $"cost={(ratio <= threshold ? "PASS" : "FAIL")} " +
            $"extraPass={(sensitivityRatio > 1 + 2 * floor ? "REJECTED" : "NOT DETECTED")}");
        for (var arm = 0; arm < times.Length; arm++)
            output.WriteLine($"SAMPLES {name} arm={arm} " + string.Join(",", times[arm].Select(t => t.ToString("F6"))));
        // This is a measurement harness: validity/cost are printed explicitly, not
        // conflated with xUnit success. The approved gate remains a human decision.
        Assert.True(baseline || !valid || sensitivityRatio > 1 + 2 * floor,
            $"{name}: the extra frame pass was not detected against the frozen floor.");

        double Run(Action action, int passes = Passes)
        {
            source.CopyTo(values, 0);
            var start = Stopwatch.GetTimestamp();
            for (var pass = 0; pass < passes; pass++) action();
            return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
    }

    private static Action ToneLoop(Type owner, ushort[] values, double[,]? matrix, ToneLuts luts)
    {
        // Bind the actual compiled production worker, excluding Magick GetArea/SetArea.
        // Reflection happens once, outside timing. Fail closed if a refactor changes the seam;
        // then update only this adapter, never the frozen implementation or measurement scope.
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var worker = owner.GetNestedTypes(BindingFlags.NonPublic)
            .SelectMany(t => t.GetMethods(flags))
            .Single(m => m.Name.StartsWith("<ApplyCore>b__", StringComparison.Ordinal));
        var closure = Activator.CreateInstance(worker.DeclaringType!, nonPublic: true)!;
        var workers = Math.Min(Environment.ProcessorCount, Math.Max(1, PixelCount / 8192));
        var fields = new Dictionary<string, object?>
        {
            ["values"] = values, ["pixelCount"] = PixelCount, ["channels"] = 3u,
            ["red"] = 0, ["green"] = 1, ["blue"] = 2, ["workers"] = workers,
            ["matrix"] = matrix, ["luts"] = luts, ["execution"] = null
        };
        foreach (var field in worker.DeclaringType!.GetFields(flags))
        {
            Assert.True(fields.TryGetValue(field.Name, out var value), $"Unexpected field {field.Name}");
            field.SetValue(closure, value is IConvertible ? Convert.ChangeType(value, field.FieldType) : value);
        }
        var body = worker.CreateDelegate<Action<int>>(closure);
        return () => Parallel.For(0, workers, body);
    }
}
