using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

/// <summary>
/// Windowless qualification of a published build (`HappyPhoton --self-check &lt;folder&gt;`).
/// Trimming failures only show at run time, so each package runs the non-UI paths trimming
/// can break against one RAW and one JPEG, in a temporary root, before Avalonia starts.
/// </summary>
internal static partial class SelfCheck
{
    internal const string Argument = "--self-check";
    internal const int StepCount = 13;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(4);
    private static volatile string _step = "start";

    internal static int Run(string[] args)
    {
        if (args.Length != 2 || !Directory.Exists(args[1]))
        {
            Console.WriteLine("self-check: usage: HappyPhoton --self-check <folder with one RAW and one JPEG>");

            return 2;
        }

        Environment.SetEnvironmentVariable("HAPPY_PHOTON_DISPLAY_TRACE", null);
        var root = Path.Combine(Path.GetTempPath(), "happy-photon-self-check-" + Guid.NewGuid().ToString("N"));
        var run = Task.Run(() => RunStepsAsync(args[1], root));

        try
        {
            if (!run.Wait(Timeout))
            {
                Console.WriteLine($"self-check: FAIL {_step}: timeout");

                return 1;
            }

            Console.WriteLine($"self-check: ok ({StepCount} steps)");

            return 0;
        }
        catch (AggregateException error)
        {
            var cause = error.GetBaseException();
            Console.WriteLine($"self-check: FAIL {_step}: {cause.GetType().Name}: {cause.Message}");

            return 1;
        }
        finally
        {
            if (run.IsCompleted)
            {
                TryDelete(root);
            }
        }
    }

    private static async Task RunStepsAsync(string folder, string root)
    {
        var (rawSource, jpegSource) = FindFixtures(folder);
        var inputs = Path.Combine(root, "inputs");
        Directory.CreateDirectory(inputs);
        var raw = CopyInput(rawSource, inputs);
        var jpeg = CopyInput(jpegSource, inputs);
        var edits = SampleEdits();

        Step("edit-settings-json");
        CheckEditSettingsJson(edits);

        using var catalog = new CatalogService(Path.Combine(root, "catalog")) { SkipSnapshotSweep = true };
        Step("catalog");
        await catalog.InitializeAsync();
        var rawId = await CheckCatalogAsync(catalog, raw, edits);

        Step("app-settings");
        await CheckAppSettingsAsync(catalog);

        Step("built-in-looks");
        Require(new BuiltInLookLibrary().Looks.Count > 0, "no built-in looks loaded");

        Step("metadata");
        CheckMetadata(raw);
        CheckMetadata(jpeg);

        Step("libraw-health");
        var health = LibRawNativeSupport.Health;
        Require(health.IsHealthy, $"LibRaw runtime unhealthy: {health.DiagnosticText}");

        Step("decode");
        using var rawBase = Decode(new RawBaseLoader(), raw, edits);
        using var jpegBase = Decode(new StandardBaseLoader(), jpeg, new EditSettings());

        Step("render");
        CheckRender(rawBase, edits);

        Step("export");
        await CheckExportAsync(root, raw, edits);

        await CheckXmpAsync(catalog, rawId, raw);
        await CheckBackupAsync(catalog);
    }

    private static void Step(string name) => _step = name;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static (string Raw, string Jpeg) FindFixtures(string folder)
    {
        var files = Directory.GetFiles(folder);
        var jpegs = files.Where(IsJpeg).ToArray();
        var raws = files.Where(path => new ImageFile(path).IsRaw).ToArray();
        Require(raws.Length == 1 && jpegs.Length == 1, "the folder must hold exactly one RAW and one JPEG");

        return (raws[0], jpegs[0]);
    }

    private static bool IsJpeg(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg";

    // Reads under the background-read rule: local and unknown sources pass; a source that
    // would need hydrating is refused, never downloaded.
    private static string CopyInput(string source, string folder)
    {
        var availability = new SourceAvailabilityService().GetAvailability(source);
        Require(SourceAccessPolicy.CanRead(availability, SourceReadIntent.Background),
            $"{Path.GetFileName(source)} is not available locally ({availability})");
        var target = Path.Combine(folder, Path.GetFileName(source));
        File.Copy(source, target);

        return target;
    }

    private static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary folder must not turn a pass into a failure.
        }
    }
}
