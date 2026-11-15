using System.Runtime.CompilerServices;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal static class OpenMpStartup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        NativeLibraryResolver.ConfigureOpenMpThreadLimit();

        // The native-load-order probe applies the policy at its first Magick use.
        if (!Environment.GetCommandLineArgs().Contains("--openmp-child"))
        {
            MagickSecurityPolicy.Initialize();
        }
    }
}
