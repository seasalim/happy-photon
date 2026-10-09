using System.Runtime.CompilerServices;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal static class CatalogTestDurability
{
    // Test catalogs are disposable; process kills still keep OS-cached writes.
    [ModuleInitializer]
    internal static void Initialize() => CatalogService.SkipDurableSyncByDefaultForTests = true;
}
