using System.Diagnostics;
using System.Globalization;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DcpAdobeProfileProbeTests
{
    public static bool RunTimingGates => Environment.GetEnvironmentVariable("DCP_PROBE_CONTROL_MS") != null;

    [Fact]
    public async Task ProbeStopsAtFirstCandidateWithoutReadingMetadataOrContent()
    {
        var calls = 0;
        IEnumerable<FileSystemInfo> Enumerate(string root)
        {
            calls++;
            yield return new FileInfo(Path.Combine(root, "does-not-exist.DCP"));
            throw new InvalidOperationException("Must stop at the first candidate.");
        }

        var result = await DcpAdobeProfileIndex.ProbeAsync([Path.GetTempPath(), "unused"],
            enumerateDirectory: Enumerate);

        Assert.Equal(DcpAdobeProfilePresence.Found, result);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProbeFindsMalformedAndPlaceholderCandidates(bool placeholder)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "malformed.dcp");
        File.WriteAllText(path, "not a profile");
        var availability = new TestSourceAvailabilityService(placeholder
            ? SourceAvailability.RequiresHydration : SourceAvailability.AvailableLocally);
        using var held = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Equal(DcpAdobeProfilePresence.Found,
            await DcpAdobeProfileIndex.ProbeAsync([directory.Path]));
        Assert.Equal(0, availability.CallCount);
    }

    [Fact]
    public async Task ProbeReportsUnknownOnAccessOrIoFailureIncludingLazyEnumeration()
    {
        IEnumerable<FileSystemInfo> FailLazily(string root)
        {
            yield return new FileInfo(Path.Combine(root, "unrelated.txt"));
            throw new IOException("Enumeration failed after an entry.");
        }

        Assert.Equal(DcpAdobeProfilePresence.Unknown,
            await DcpAdobeProfileIndex.ProbeAsync([Path.GetTempPath()],
                enumerateDirectory: _ => throw new UnauthorizedAccessException()));
        Assert.Equal(DcpAdobeProfilePresence.Unknown,
            await DcpAdobeProfileIndex.ProbeAsync([Path.GetTempPath()], enumerateDirectory: FailLazily));
    }

    [Fact]
    public async Task ProbeExpandsWildcardAgainAfterInstallation()
    {
        using var directory = new TemporaryDirectory();
        var roots = new[] { Path.Combine(directory.Path, "users", "*", "profiles") };
        Assert.Equal(DcpAdobeProfilePresence.None, await DcpAdobeProfileIndex.ProbeAsync(roots));
        var installed = Directory.CreateDirectory(Path.Combine(directory.Path, "users", "new-user", "profiles"));
        File.WriteAllText(Path.Combine(installed.FullName, "new.dcp"), "candidate");

        Assert.Equal(DcpAdobeProfilePresence.Found, await DcpAdobeProfileIndex.ProbeAsync(roots));
    }

    [Fact]
    public async Task ProbeCancellationStopsAStalledEnumerationAndItsLaterWalk()
    {
        using var cancellation = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        IEnumerable<FileSystemInfo> Stall(string root)
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            release.Wait(TestWaits.Condition);
            exited.TrySetResult();

            return [];
        }

        var task = DcpAdobeProfileIndex.ProbeAsync(["stalled", "never"], cancellation.Token,
            enumerateDirectory: Stall);

        try
        {
            await entered.Task.WaitAsync(TestWaits.Condition);
            cancellation.Cancel();
            Assert.Equal(DcpAdobeProfilePresence.Unknown, await task.WaitAsync(TestWaits.Condition));
        }
        finally
        {
            release.Set();
            await exited.Task.WaitAsync(TestWaits.Condition);
        }

        Assert.Equal(1, calls);
        Assert.Equal(DcpAdobeProfilePresence.Unknown,
            await DcpAdobeProfileIndex.ProbeAsync([], cancellation.Token));
    }

    // Claude supplies the same three-level fixture used by the parent control, and its
    // median, under the measure lock. Do not substitute a separately generated tree.
    [Fact(Skip = "Requires the measure lock and DCP_PROBE_CONTROL_MS from the parent", SkipUnless = nameof(RunTimingGates))]
    public async Task G1_WarmFiveHundredProfileProbeMeetsAbsoluteAndRelativeBudgets()
    {
        var root = Environment.GetEnvironmentVariable("DCP_PROBE_GATE_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(root), "Supply the parent control's fixture as DCP_PROBE_GATE_ROOT.");
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        var profiles = files.Where(path => Path.GetExtension(path).Equals(".dcp", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal(500, profiles.Length);
        Assert.Equal(1500, files.Length - profiles.Length);
        var control = double.Parse(Environment.GetEnvironmentVariable("DCP_PROBE_CONTROL_MS")!, CultureInfo.InvariantCulture);
        Assert.InRange(control, 20, 800);
        var median = await MedianProbeAsync([root], DcpAdobeProfilePresence.Found);

        Assert.True(median <= 10 && median <= control * .2, $"Probe median {median:F3} ms; parent {control:F3} ms.");
    }

    [Fact(Skip = "Requires the measure lock and DCP_PROBE_CONTROL_MS from the parent", SkipUnless = nameof(RunTimingGates))]
    public async Task G2_FourMissingRootsMeetBudget()
    {
        var root = Environment.GetEnvironmentVariable("DCP_PROBE_GATE_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(root), "Supply the parent control's fixture as DCP_PROBE_GATE_ROOT.");
        var roots = Enumerable.Range(0, 4).Select(i => Path.Combine(root, $"missing-{i}")).ToArray();
        Assert.All(roots, path => Assert.False(Directory.Exists(path)));
        var median = await MedianProbeAsync(roots, DcpAdobeProfilePresence.None);

        Assert.True(median <= 2, $"Probe median {median:F3} ms.");
    }

    private static async Task<double> MedianProbeAsync(string[] roots, DcpAdobeProfilePresence expected)
    {
        await DcpAdobeProfileIndex.ProbeAsync(roots);
        var times = new List<double>();

        for (var run = 0; run < 20; run++)
        {
            var watch = Stopwatch.StartNew();
            var result = await DcpAdobeProfileIndex.ProbeAsync(roots);
            times.Add(watch.Elapsed.TotalMilliseconds);
            Assert.Equal(expected, result);
        }

        times.Sort();

        return (times[9] + times[10]) / 2;
    }
}
