using System.Diagnostics;
using HappyPhoton.LibRaw.Interop;

if (args.Length != 1)
{
    return Fail("Expected one RAW fixture path.");
}

var fixture = Path.GetFullPath(args[0]);
if (!File.Exists(fixture))
{
    return Fail($"RAW fixture was not found at '{fixture}'.");
}

if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
{
    return Fail("The committed single-file smoke supports win-x64 and linux-x64.");
}

try
{
    var runtime = LibRawContext.Runtime;
    if (runtime.LibRawVersionNumber != 0x001602)
    {
        return Fail($"Expected LibRaw 0.22.2, observed {runtime.LibRawVersion}.");
    }

    using var context = LibRawContext.Open(fixture);
    context.Unpack();
    context.ConfigureOutput(LibRawOutputConfiguration.Linear(
        LibRawHighlightMode.Clip,
        LibRawFbddMode.Off,
        halfSize: true));
    context.Process();
    using var image = context.MakeProcessedImage();
    var description = image.Description;
    if (description.Width == 0 || description.Height == 0 ||
        description.BitsPerSample != 16 || description.Channels != 3)
    {
        return Fail("The published smoke decode returned an invalid image.");
    }

    var bridge = FindLoadedModule(BridgeName());
    var companion = FindLoadedModule(CompanionName());
    var bridgeDirectory = Path.GetDirectoryName(bridge)!;
    var companionDirectory = Path.GetDirectoryName(companion)!;
    if (!PathEquals(bridgeDirectory, companionDirectory))
    {
        return Fail("The bridge and LibRaw companion loaded from different directories.");
    }

    var nativeSearch = (string?)AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES");
    var searchDirectories = (nativeSearch ?? string.Empty).Split(
        Path.PathSeparator,
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (!searchDirectories.Any(directory => PathEquals(directory, bridgeDirectory)))
    {
        return Fail("The native modules did not load from a runtime extraction directory.");
    }

    if (PathEquals(bridgeDirectory, AppContext.BaseDirectory))
    {
        return Fail("The native modules loaded beside the executable instead of extraction.");
    }

    Console.WriteLine(
        $"LibRaw {runtime.LibRawVersion} decoded {description.Width}x{description.Height}; " +
        $"bridge={bridge}; companion={companion}");
    return 0;
}
catch (Exception exception)
{
    return Fail(exception.ToString());
}

static string FindLoadedModule(string name)
{
    var matches = Process.GetCurrentProcess().Modules
        .Cast<ProcessModule>()
        .Select(module => module.FileName)
        .Where(path => !string.IsNullOrWhiteSpace(path) &&
            Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase))
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    return matches.Length == 1
        ? matches[0]
        : throw new InvalidOperationException(
            $"Expected one loaded '{name}' module, observed {matches.Length}.");
}

static bool PathEquals(string left, string right) =>
    Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);

static string BridgeName() => OperatingSystem.IsWindows()
    ? "happyphoton_libraw_bridge.dll"
    : "libhappyphoton_libraw_bridge.so";

static string CompanionName() => OperatingSystem.IsWindows()
    ? "raw_r.dll"
    : "libraw_r.so.25";

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
