using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HappyPhoton.LibRaw.Interop;

internal static class NativeLibraryResolver
{
    internal static string? LoadedBridgePath { get; private set; }
    internal static string? LoadedLibRawPath { get; private set; }
    private static nint _libRawHandle;

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Install()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, Resolve);
    }
#pragma warning restore CA2255

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? path)
    {
        if (libraryName != NativeMethods.LibraryName) return 0;
        ConfigureOpenMpThreadLimit();
        var directory = ResolveDirectory();
        var bridgeName = OperatingSystem.IsWindows()
            ? "happyphoton_libraw_bridge.dll"
            : OperatingSystem.IsMacOS()
                ? "libhappyphoton_libraw_bridge.dylib"
                : "libhappyphoton_libraw_bridge.so";
        var libRawName = OperatingSystem.IsWindows()
            ? "raw_r.dll"
            : OperatingSystem.IsMacOS() ? "libraw.25.dylib" : "libraw_r.so.25";
        var bridgePath = ContainedPath(
            directory,
            bridgeName,
            LibRawRuntimeComponent.Bridge);
        var libRawPath = ContainedPath(
            directory,
            libRawName,
            LibRawRuntimeComponent.LibRawCompanion);
        if (!File.Exists(bridgePath))
            throw Failure(
                LibRawRuntimeComponent.Bridge,
                LibRawDeploymentStage.Resolution,
                $"LibRaw bridge was not found at '{bridgePath}'.");
        if (!File.Exists(libRawPath))
            throw Failure(
                LibRawRuntimeComponent.LibRawCompanion,
                LibRawDeploymentStage.Resolution,
                $"LibRaw companion was not found at '{libRawPath}'.");

        _libRawHandle = Load(
            libRawPath,
            LibRawRuntimeComponent.LibRawCompanion);
        LoadedLibRawPath = libRawPath;
        var handle = Load(bridgePath, LibRawRuntimeComponent.Bridge);
        LoadedBridgePath = bridgePath;
        return handle;
    }

    private static void ConfigureOpenMpThreadLimit()
    {
        if (!string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("OMP_NUM_THREADS")))
        {
            return;
        }

        Environment.SetEnvironmentVariable(
            "OMP_NUM_THREADS",
            GetDefaultOpenMpThreadCount(Environment.ProcessorCount).ToString(
                CultureInfo.InvariantCulture));
    }

    internal static int GetDefaultOpenMpThreadCount(int processorCount) =>
        Math.Clamp(processorCount, 1, 8);

    private static string ResolveDirectory()
    {
        try
        {
            return ResolveDirectoryCore();
        }
        catch (LibRawDeploymentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or
            NotSupportedException or PathTooLongException)
        {
            throw new LibRawDeploymentException(
                LibRawRuntimeComponent.Bridge,
                LibRawDeploymentStage.Resolution,
                $"The native runtime directory could not be resolved: {exception.Message}",
                exception);
        }
    }

    private static string ResolveDirectoryCore()
    {
        var staged = Environment.GetEnvironmentVariable("HAPPY_PHOTON_LIBRAW_BRIDGE_DIR");
        if (!string.IsNullOrWhiteSpace(staged)) return Path.GetFullPath(staged);

        var nativeSearch = (string?)AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES");
        if (!string.IsNullOrWhiteSpace(nativeSearch))
        {
            foreach (var candidate in nativeSearch.Split(Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (File.Exists(Path.Combine(candidate, BridgeFileName())))
                    return Path.GetFullPath(candidate);
            }
        }

        var baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        if (File.Exists(Path.Combine(baseDirectory, BridgeFileName()))) return baseDirectory;
        var ridDirectory = Path.Combine(baseDirectory, "runtimes", RuntimeIdentifier(), "native");
        if (File.Exists(Path.Combine(ridDirectory, BridgeFileName()))) return ridDirectory;
        throw Failure(
            LibRawRuntimeComponent.Bridge,
            LibRawDeploymentStage.Resolution,
            "The package-local Happy Photon LibRaw runtime was not found.");
    }

    private static string ContainedPath(
        string directory,
        string fileName,
        LibRawRuntimeComponent component)
    {
        try
        {
            directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            var fullPath = Path.GetFullPath(Path.Combine(directory, fileName));
            var expectedDirectory = directory + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(expectedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw Failure(
                    component,
                    LibRawDeploymentStage.Resolution,
                    "Native path escaped its runtime directory.");
            }
            return fullPath;
        }
        catch (LibRawDeploymentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or
            NotSupportedException or PathTooLongException)
        {
            throw new LibRawDeploymentException(
                component,
                LibRawDeploymentStage.Resolution,
                $"The native runtime path could not be resolved: {exception.Message}",
                exception);
        }
    }

    private static string BridgeFileName() => OperatingSystem.IsWindows()
        ? "happyphoton_libraw_bridge.dll"
        : OperatingSystem.IsMacOS()
            ? "libhappyphoton_libraw_bridge.dylib"
            : "libhappyphoton_libraw_bridge.so";

    private static string RuntimeIdentifier() => OperatingSystem.IsWindows()
        ? "win-x64"
        : OperatingSystem.IsMacOS() ? "osx-arm64" : "linux-x64";

    private static nint Load(string path, LibRawRuntimeComponent component)
    {
        try
        {
            return NativeLibrary.Load(path);
        }
        catch (Exception exception) when (exception is DllNotFoundException or
            BadImageFormatException or FileLoadException)
        {
            throw new LibRawDeploymentException(
                component,
                LibRawDeploymentStage.Load,
                $"The {ComponentName(component)} could not be loaded from '{path}': " +
                exception.Message,
                exception);
        }
    }

    private static LibRawDeploymentException Failure(
        LibRawRuntimeComponent component,
        LibRawDeploymentStage stage,
        string detail) => new(component, stage, detail);

    private static string ComponentName(LibRawRuntimeComponent component) =>
        component == LibRawRuntimeComponent.Bridge
            ? "LibRaw bridge"
            : "LibRaw companion";
}
