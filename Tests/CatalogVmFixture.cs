using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Tests;

internal sealed class CatalogVmFixture : IDisposable
{
    public CatalogVmFixture(string prefix = "vm")
    {
        Root = Directory.CreateDirectory(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"happy-photon-{prefix}-{Guid.NewGuid():N}")).FullName;
    }

    public string Root { get; }

    public string Path(string name) => System.IO.Path.Combine(Root, name);

    public CatalogService CreateCatalog(string? subdirectory = null) =>
        new(subdirectory == null ? Root : Path(subdirectory));

    public async Task<CatalogService> CreateCatalogAsync(
        string? subdirectory = null)
    {
        var catalog = CreateCatalog(subdirectory);
        await catalog.InitializeAsync();
        return catalog;
    }

    public Task<CatalogService> CreateUniqueCatalogAsync() =>
        CreateCatalogAsync(Guid.NewGuid().ToString("N"));

    public MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        IBaseImageLoader? baseLoader = null,
        Func<ImageFile, Task>? loadMetadataAsync = null,
        ISourceAvailabilityService? availabilityService = null,
        Action<Action>? postSelection = null,
        LibRawRuntimeHealth? rawRuntimeHealth = null,
        TimeProvider? timeProvider = null,
        IFileOperationService? fileOperationService = null,
        Func<long, Task<bool>>? deleteCatalogVersionAsync = null) =>
        new(
            catalog,
            baseLoader,
            loadMetadataAsync,
            availabilityService,
            postSelection,
            rawRuntimeHealth: rawRuntimeHealth,
            timeProvider: timeProvider,
            fileOperationService: fileOperationService,
            deleteCatalogVersionAsync: deleteCatalogVersionAsync);

    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
    }
}
