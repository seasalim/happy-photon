using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal static class BaseLoadMeasurement
{
    internal sealed record Iteration(int Index, double ElapsedMs, string Trace, Dictionary<string, double> Steps);
    internal sealed record Step(string Stage, double WarmMedianMs, int WarmSamples);
    internal sealed record Report(string Fixture, double FirstMs, double WarmMedianMs, Iteration[] Iterations, Step[] Steps);

    internal static Report Measure(string label, Func<IDisposable> load, ITestOutputHelper output)
    {
        var iterations = new List<Iteration>();
        var console = Console.Out;
        for (var index = 0; index <= 5; index++)
        {
            using var log = new StringWriter(CultureInfo.InvariantCulture);
            double elapsed;
            Console.SetOut(log);
            try
            {
                var watch = Stopwatch.StartNew();
                using var loaded = load();
                elapsed = watch.Elapsed.TotalMilliseconds;
            }
            finally { Console.SetOut(console); }
            var trace = log.ToString();
            var steps = Regex.Matches(trace, @" - (Preview\.[^:]+): (\d+)ms")
                .ToDictionary(match => match.Groups[1].Value,
                    match => double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
            iterations.Add(new Iteration(index, elapsed, trace, steps));
        }
        var summaries = iterations.Skip(1).SelectMany(item => item.Steps)
            .GroupBy(item => item.Key)
            .Select(group => new Step(group.Key, Median(group.Select(item => item.Value)), group.Count())).ToArray();
        var report = new Report(label, iterations[0].ElapsedMs,
            Median(iterations.Skip(1).Select(item => item.ElapsedMs)), iterations.ToArray(), summaries);
        output.WriteLine($"base-load fixture={label} firstMs={report.FirstMs:F1} warmMedianMs={report.WarmMedianMs:F1} warmSamples=5");
        foreach (var step in summaries)
            output.WriteLine($"  stage={step.Stage} warmMedianMs={step.WarmMedianMs:F1} warmSamples={step.WarmSamples}");
        // Full traces stay in the JSON; console output keeps only the cold one so VSTest
        // does not truncate later fixtures' summaries.
        output.WriteLine($"  iteration=0 elapsedMs={iterations[0].ElapsedMs:F1}");
        foreach (var line in iterations[0].Trace.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            output.WriteLine("    " + line.Trim());
        return report;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
