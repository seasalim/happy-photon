using System.Runtime.CompilerServices;
using HappyPhoton.LibRaw.Interop;

namespace HappyPhoton.Tests;

internal static class OpenMpStartup
{
    [ModuleInitializer]
    internal static void Initialize() => NativeLibraryResolver.ConfigureOpenMpThreadLimit();
}
