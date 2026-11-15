using System.Runtime.CompilerServices;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal static class OpenMpStartup
{
    internal const string ConfigurationRootEnvironmentVariable = "HAPPY_PHOTON_TEST_MAGICK_ROOT";

    internal static string ConfigurationRoot { get; private set; } = null!;

    [ModuleInitializer]
    internal static void Initialize()
    {
        NativeLibraryResolver.ConfigureOpenMpThreadLimit();
        var root = Environment.GetEnvironmentVariable(ConfigurationRootEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(root))
        {
            var scratch = new TemporaryDirectory();
            root = scratch.Path;
            // Children reuse the host-owned scratch root, including deliberately killed probes.
            Environment.SetEnvironmentVariable(ConfigurationRootEnvironmentVariable, root);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => scratch.Dispose();
        }

        ConfigurationRoot = root;

        // The native-load-order probe applies the policy at its first Magick use.
        if (!Environment.GetCommandLineArgs().Contains("--openmp-child"))
        {
            MagickSecurityPolicy.Initialize(ConfigurationRoot);
        }
    }
}
