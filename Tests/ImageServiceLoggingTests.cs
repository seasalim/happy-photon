using HappyPhoton.Services;
using Xunit;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Tests;

public sealed class ImageServiceLoggingTests
{
    [Fact]
    public void DisabledDebugLogging_DoesNotEvaluateInterpolatedValues()
    {
        if (DebugLoggingEnabled) return;
        var evaluations = 0;

        LogDebug("test", $"value={Increment(ref evaluations)}");

        Assert.Equal(0, evaluations);
    }

    [Fact]
    public void DisabledPerformanceLogging_DoesNotEvaluateInterpolatedExtra()
    {
        if (PerfLoggingEnabled) return;
        var evaluations = 0;

        LogPerformance("test", "step", 1, null, $"value={Increment(ref evaluations)}");

        Assert.Equal(0, evaluations);
    }

    [Fact]
    public void DisabledDisplayTrace_DoesNotEvaluateBitmapDimensions()
    {
        if (DisplayTraceLoggingEnabled) return;
        var evaluations = 0;

        LogDisplayTrace($"bitmap={Increment(ref evaluations)}x1");

        Assert.Equal(0, evaluations);
    }

    [Fact]
    public void DisplayTraceFile_TruncatesOnFirstWriteThenAppends()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), Path.GetRandomFileName());
        var path = Path.Combine(directory, "display-trace.log");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "stale line from a previous session\n");
            using (OverrideDisplayTraceForTesting(enabled: true, sink: null))
            using (OverrideDisplayTraceFileForTesting(path))
            {
                LogDisplayTrace($"first");
                LogDisplayTrace($"second");
            }

            var lines = File.ReadAllLines(path);
            Assert.Equal(3, lines.Length);
            Assert.Matches(HeaderLine(), lines[0]);
            Assert.Matches(TimestampedLine("first"), lines[1]);
            Assert.Matches(TimestampedLine("second"), lines[2]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DisplayTraceFile_CreatesMissingLogDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), Path.GetRandomFileName());
        var path = Path.Combine(directory, "logs", "display-trace.log");
        try
        {
            using (OverrideDisplayTraceForTesting(enabled: true, sink: null))
            using (OverrideDisplayTraceFileForTesting(path))
            {
                LogDisplayTrace($"line");
            }

            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.Matches(HeaderLine(), lines[0]);
            Assert.Matches(TimestampedLine("line"), lines[1]);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DisplayTraceSinkOverride_BypassesFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), Path.GetRandomFileName());
        var path = Path.Combine(directory, "display-trace.log");
        var lines = new List<string>();
        try
        {
            using (OverrideDisplayTraceForTesting(enabled: true, lines.Add))
            using (OverrideDisplayTraceFileForTesting(path))
            {
                LogDisplayTrace($"captured");
            }

            Assert.Equal(["[DisplayChain] captured"], lines);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static int Increment(ref int value) => ++value;

    private static string HeaderLine() =>
        @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[DisplayChain\] " +
        "trace start version=.+ revision=.+$";

    private static string TimestampedLine(string message) =>
        @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[DisplayChain\] " +
        message + "$";
}
