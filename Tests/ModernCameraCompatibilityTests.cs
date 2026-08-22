using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class ModernCameraCompatibilityTests
{
    private const string OptInVariable = "HAPPY_PHOTON_COMPAT";
    private readonly ITestOutputHelper _output;

    public ModernCameraCompatibilityTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    [Trait("Category", "Compatibility")]
    public async Task SelectedP0Fixtures_ExerciseCompleteApplicationPaths()
    {
        var mode = ReadMode();
        Assert.SkipWhen(
            mode == CompatibilityMode.Off,
            $"Set {OptInVariable}=1, discovery, or strict to run modern-camera fixtures.");
        Assert.SkipWhen(
            !OperatingSystem.IsWindows() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64,
            "The P0 compatibility harness is intentionally limited to Windows x64.");

        var repositoryRoot = FindRepositoryRoot();
        var manifest = CompatibilityFixtureManifest.Load(Path.Combine(
            repositoryRoot, "Tests", "compatibility-fixtures.json"));
        ValidateMode(mode, manifest);
        var selected = SelectFixtures(mode, manifest);
        var cacheDirectory = Path.Combine(
            repositoryRoot, "artifacts", "compatibility-fixtures");
        var resultsDirectory = Path.Combine(
            repositoryRoot, "artifacts", "compatibility-results");
        var observations = new List<CompatibilityObservation>();
        var failures = new List<string>();
        var missing = new List<string>();
        var terminalCount = 0;

        var health = LibRawNativeSupport.Health;
        if (!health.IsHealthy)
        {
            foreach (var fixture in selected)
            {
                WriteTerminal(
                    fixture.Slug!,
                    "infrastructure-failure",
                    health.DiagnosticText);
                terminalCount++;
            }
            _output.WriteLine(
                $"COMPAT COMPLETE observed={terminalCount} selected={selected.Count}");
            Assert.Fail($"LibRaw runtime was rejected: {health.DiagnosticText}");
        }

        foreach (var fixture in selected)
        {
            var path = Path.Combine(cacheDirectory, fixture.FileName);
            if (!File.Exists(path))
            {
                var message =
                    $"{fixture.Slug}: fixture is missing; run " +
                    "dotnet run --file scripts/fetch-compatibility-fixtures.cs.";
                if (mode == CompatibilityMode.Local)
                {
                    missing.Add(fixture.Slug!);
                    WriteTerminal(fixture.Slug!, "skipped", message);
                }
                else
                {
                    failures.Add(message);
                    WriteTerminal(fixture.Slug!, "infrastructure-failure", message);
                }
                terminalCount++;
                continue;
            }

            try
            {
                VerifyFixtureFile(fixture, path);
                var observation = await CompatibilityFixtureRunner.RunAsync(
                    fixture,
                    path,
                    resultsDirectory,
                    saveReviewImage: mode == CompatibilityMode.Discovery,
                    CancellationToken.None);
                VerifyFixtureFile(fixture, path);
                observations.Add(observation);
                var differences = fixture.Expected == null
                    ? []
                    : Compare(fixture, observation);
                var details = Summarize(observation, differences);
                if (mode == CompatibilityMode.Discovery)
                {
                    WriteTerminal(fixture.Slug!, "observed", details);
                }
                else if (differences.Count == 0)
                {
                    WriteTerminal(fixture.Slug!, "passed", details);
                }
                else
                {
                    failures.AddRange(differences.Select(difference =>
                        $"{fixture.Slug}: {difference}"));
                    WriteTerminal(fixture.Slug!, "failed", details);
                }
            }
            catch (Exception exception)
            {
                var message =
                    $"{fixture.Slug}: {exception.GetType().Name}: {exception.Message}";
                failures.Add(message);
                WriteTerminal(fixture.Slug!, "infrastructure-failure", message);
            }
            terminalCount++;
        }

        try
        {
            WriteReport(
                resultsDirectory,
                mode,
                selected.Count,
                terminalCount,
                observations);
        }
        catch (Exception exception)
        {
            failures.Add($"Could not write compatibility report: {exception.Message}");
        }

        _output.WriteLine(
            $"COMPAT COMPLETE observed={terminalCount} selected={selected.Count}");
        Assert.Equal(selected.Count, terminalCount);
        if (failures.Count > 0)
        {
            Assert.Fail(string.Join(Environment.NewLine, failures));
        }
        if (missing.Count > 0)
        {
            Assert.Skip(
                $"Missing compatibility fixtures: {string.Join(", ", missing)}. " +
                "Run dotnet run --file scripts/fetch-compatibility-fixtures.cs.");
        }
    }

    private static CompatibilityMode ReadMode()
    {
        var value = Environment.GetEnvironmentVariable(OptInVariable);
        if (string.IsNullOrEmpty(value))
        {
            return CompatibilityMode.Off;
        }
        return value switch
        {
            "1" => CompatibilityMode.Local,
            "discovery" => CompatibilityMode.Discovery,
            "strict" => CompatibilityMode.Strict,
            _ => throw new InvalidOperationException(
                $"Invalid {OptInVariable} value '{value}'. " +
                "Use 1, discovery, strict, or leave it unset.")
        };
    }

    private static void ValidateMode(
        CompatibilityMode mode,
        CompatibilityFixtureManifest manifest)
    {
        if (mode != CompatibilityMode.Strict)
        {
            return;
        }
        var incomplete = manifest.Fixtures!
            .Where(fixture => fixture.SelectionStatus == "pending" ||
                fixture.ExpectationStatus == "candidate")
            .Select(fixture => fixture.Slug)
            .ToArray();
        CompatibilityFixtureManifest.Require(
            incomplete.Length == 0,
            $"strict mode rejects pending or candidate fixtures: " +
            string.Join(", ", incomplete));
    }

    private static IReadOnlyList<CompatibilityFixture> SelectFixtures(
        CompatibilityMode mode,
        CompatibilityFixtureManifest manifest) =>
        manifest.SelectedFixtures
            .Where(fixture => mode != CompatibilityMode.Local ||
                fixture.ExpectationStatus == "reviewed")
            .ToArray();

    private static void VerifyFixtureFile(
        CompatibilityFixture fixture,
        string path)
    {
        var length = new FileInfo(path).Length;
        CompatibilityFixtureManifest.Require(
            length == fixture.SizeBytes,
            $"{fixture.Slug}: expected {fixture.SizeBytes} bytes, observed {length}.");
        CompatibilityFixtureManifest.Require(
            length <= CompatibilityFixtureManifest.MaximumFixtureBytes,
            $"{fixture.Slug}: fixture exceeds the 30 MiB cap.");
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        CompatibilityFixtureManifest.Require(
            actual == fixture.Sha256,
            $"{fixture.Slug}: SHA-256 mismatch; expected {fixture.Sha256}, " +
            $"observed {actual}. The cached file was not replaced.");
    }

    private static List<string> Compare(
        CompatibilityFixture fixture,
        CompatibilityObservation observation)
    {
        var expected = fixture.Expected!;
        var differences = new List<string>();
        ExpectCapability(differences, observation, "browseThumbnail",
            expected.Capabilities!.BrowseThumbnail!);
        ExpectCapability(differences, observation, "metadata",
            expected.Capabilities.Metadata!);
        ExpectCapability(differences, observation, "preview",
            expected.Capabilities.Preview!);
        ExpectCapability(differences, observation, "fullDecode",
            expected.Capabilities.FullDecode!);
        ExpectCapability(differences, observation, "cameraColor",
            expected.Capabilities.CameraColor!);
        ExpectCapability(differences, observation, "export",
            expected.Capabilities.Export!);

        var metadata = expected.Metadata!;
        CompareValue(differences, "metadata make", fixture.CameraMake,
            observation.Metadata?.Make);
        CompareValue(differences, "metadata model", fixture.CameraModel,
            observation.Metadata?.Model);
        CompareValue(differences, "visible width", metadata.VisibleWidth,
            observation.Metadata?.VisibleWidth);
        CompareValue(differences, "visible height", metadata.VisibleHeight,
            observation.Metadata?.VisibleHeight);
        CompareValue(differences, "native orientation", metadata.NativeOrientation,
            observation.Metadata?.NativeOrientation);
        if (metadata.RequiresIso && observation.Metadata?.HasIso != true)
            differences.Add("ISO metadata was absent");
        if (metadata.RequiresExposure && observation.Metadata?.HasExposure != true)
            differences.Add("exposure metadata was absent");
        if (metadata.RequiresCaptureTimestamp &&
            observation.Metadata?.HasCaptureTimestamp != true)
            differences.Add("capture timestamp was absent");

        if (expected.Overall == "supported")
        {
            CompareSupported(fixture, observation, differences);
        }
        else if (expected.Overall == "unsupported")
        {
            CompareUnsupported(expected, observation, differences);
        }
        return differences;
    }

    private static void CompareSupported(
        CompatibilityFixture fixture,
        CompatibilityObservation observation,
        List<string> differences)
    {
        var expected = fixture.Expected!;
        ExpectCapability(differences, observation, "render", "pass");
        ExpectCapability(differences, observation, "cleanup", "pass");
        CompareValue(differences, "full width", expected.Metadata!.VisibleWidth,
            observation.FullWidth);
        CompareValue(differences, "full height", expected.Metadata.VisibleHeight,
            observation.FullHeight);
        CompareValue(differences, "applied orientation",
            expected.Metadata.AppliedOrientation, observation.AppliedOrientation);
        if (observation.PreviewWidth > observation.FullWidth ||
            observation.PreviewHeight > observation.FullHeight)
            differences.Add("preview dimensions exceeded full dimensions");

        var sensor = expected.Sensor ?? expected.CameraFacts!.Sensor!;
        CompareValue(differences, "sensor colors", sensor.Colors,
            observation.Sensor?.Colors);
        CompareValue(differences, "sensor filters", sensor.Filters,
            observation.Sensor?.Filters);
        CompareValue(differences, "sensor DNG version", sensor.DngVersion,
            observation.Sensor?.DngVersion);
        CompareValue(differences, "sensor color description",
            sensor.ColorDescription, observation.Sensor?.ColorDescription);
        if (expected.CameraFacts is { } facts)
        {
            CompareNumbers(differences, "CamMul", facts.CamMul!.Values!,
                observation.CamMul, facts.CamMul.AbsoluteTolerance,
                facts.CamMul.RelativeTolerance);
            CompareNumbers(differences, "CamToSrgb", facts.CamToSrgb!.RowMajorValues!,
                observation.CamToSrgb, facts.CamToSrgb.AbsoluteTolerance,
                facts.CamToSrgb.RelativeTolerance);
        }
    }

    private static void CompareUnsupported(
        CompatibilityExpectation expected,
        CompatibilityObservation observation,
        List<string> differences)
    {
        ExpectCapability(differences, observation, "unpack", "unsupported");
        ExpectCapability(differences, observation, "render", "unsupported");
        ExpectCapability(differences, observation, "attribution", "pass");
        ExpectCapability(differences, observation, "cleanup", "pass");
        CompareValue(differences, "LibRaw error code",
            expected.UnpackError!.NativeCode, observation.UnpackError?.NativeCode);
        CompareValue(differences, "LibRaw error text",
            expected.UnpackError.NativeText, observation.UnpackError?.NativeText);
        if (observation.FullWidth != 0 || observation.FullHeight != 0)
            differences.Add("unsupported decode returned a base");
        if (!observation.RawDecodeFailed ||
            observation.UserStatus !=
            "This RAW file could not be decoded. It may use an unsupported encoding such as Nikon HE.")
            differences.Add("unsupported decode did not reach the existing Nikon-HE attribution");
    }

    private static void ExpectCapability(
        List<string> differences,
        CompatibilityObservation observation,
        string name,
        string expected)
    {
        observation.Capabilities.TryGetValue(name, out var actual);
        if (actual != expected)
        {
            differences.Add($"{name}: expected {expected}, observed {actual ?? "missing"}");
        }
    }

    private static void CompareNumbers(
        List<string> differences,
        string name,
        IReadOnlyList<double> expected,
        IReadOnlyList<double>? actual,
        double absoluteTolerance,
        double relativeTolerance)
    {
        if (actual == null || actual.Count != expected.Count)
        {
            differences.Add($"{name}: expected {expected.Count} values, " +
                $"observed {actual?.Count.ToString() ?? "none"}");
            return;
        }
        for (var index = 0; index < expected.Count; index++)
        {
            var tolerance = Math.Max(
                absoluteTolerance,
                relativeTolerance * Math.Abs(expected[index]));
            if (Math.Abs(actual[index] - expected[index]) > tolerance)
            {
                differences.Add(
                    $"{name}[{index}]: expected {expected[index]:R}, " +
                    $"observed {actual[index]:R}, tolerance {tolerance:R}");
            }
        }
    }

    private static void CompareValue<T>(
        List<string> differences,
        string name,
        T expected,
        T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            differences.Add($"{name}: expected {expected}, observed {actual}");
    }

    private static string Summarize(
        CompatibilityObservation observation,
        IReadOnlyCollection<string> differences) =>
        $"capabilities={string.Join(',', observation.Capabilities.Select(pair =>
            $"{pair.Key}:{pair.Value}"))}; elapsed_ms={observation.ElapsedMilliseconds}; " +
        $"decode_ms=preview:{observation.PreviewDecodeMilliseconds}," +
        $"full:{observation.FullDecodeMilliseconds}; " +
        $"export_ms={observation.ExportMilliseconds}; " +
        $"peak_working_set_bytes={observation.ProcessPeakWorkingSetBytes}; " +
        $"review={(differences.Count == 0 ? "match" : string.Join(" | ", differences))}";

    private void WriteTerminal(string slug, string status, string details) =>
        _output.WriteLine($"COMPAT TERMINAL {slug} status={status}; {details}");

    private static void WriteReport(
        string resultsDirectory,
        CompatibilityMode mode,
        int selectedCount,
        int observedCount,
        IReadOnlyList<CompatibilityObservation> observations)
    {
        Directory.CreateDirectory(resultsDirectory);
        var runtime = LibRawContext.Runtime;
        var report = new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            mode = mode.ToString().ToLowerInvariant(),
            rid = RuntimeInformation.RuntimeIdentifier,
            operatingSystem = RuntimeInformation.OSDescription,
            libRawVersion = runtime.LibRawVersion,
            bridgeAbiVersion = runtime.BridgeAbiVersion,
            selectedCount,
            observedCount,
            observations
        };
        File.WriteAllText(
            Path.Combine(resultsDirectory, "compatibility-results.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
            !File.Exists(Path.Combine(directory.FullName, "HappyPhoton.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the Happy Photon repository root.");
    }

    private enum CompatibilityMode
    {
        Off,
        Local,
        Discovery,
        Strict
    }
}
