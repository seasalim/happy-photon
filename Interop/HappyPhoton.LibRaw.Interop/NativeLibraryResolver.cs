using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HappyPhoton.LibRaw.Interop;

internal static class NativeLibraryResolver
{
    internal static string? LoadedBridgePath { get; private set; }

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
        var directory = Environment.GetEnvironmentVariable("HAPPY_PHOTON_LIBRAW_BRIDGE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return 0;
        var fileName = OperatingSystem.IsWindows()
            ? "happyphoton_libraw_bridge.dll"
            : OperatingSystem.IsMacOS()
                ? "libhappyphoton_libraw_bridge.dylib"
                : "libhappyphoton_libraw_bridge.so";
        var fullPath = Path.GetFullPath(Path.Combine(directory, fileName));
        var expectedDirectory = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(expectedDirectory, StringComparison.OrdinalIgnoreCase))
            throw new DllNotFoundException("Bridge path escaped its staging directory.");
        var handle = NativeLibrary.Load(fullPath);
        LoadedBridgePath = fullPath;
        return handle;
    }
}
