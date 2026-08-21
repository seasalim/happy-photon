using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ProfileDecodeGuardTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-profile-guard-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task Coordinator_RejectsSelectionOnlyDecode()
    {
        var coordinator = new PreviewBaseCoordinator(new NullBaseLoader());
        var decode = BaseDecodeSettings.From(new EditSettings
        {
            RawProfile = new RawProfileSelection
            {
                Source = RawProfileSource.UserFile,
                Location = Path.Combine(_root, "missing.dcp"),
                ContentHash = new string('a', 64)
            }
        });

        Assert.NotNull(decode.ProfileSelection);
        Assert.Null(decode.ProfileResolution);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            coordinator.GetPreviewAsync(
                new ImageFile(Path.Combine(_root, "photo.cr2")),
                decode,
                CancellationToken.None));
    }

    [Fact]
    public async Task WhiteBalanceContext_ResolvesProfileBeforeDecoding()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        await using var service = new PreviewService(
            catalog,
            new NullBaseLoader(),
            new RenderPipeline());
        var settings = new EditSettings
        {
            RawProfile = new RawProfileSelection
            {
                Source = RawProfileSource.UserFile,
                Location = Path.Combine(_root, "missing.dcp"),
                ContentHash = new string('a', 64)
            }
        };

        // A selection-carrying request must resolve before it reaches the
        // coordinator; the guard above would throw if it did not. A missing
        // profile file resolves to a rejected outcome, which still satisfies
        // the guard — only the unresolved selection is forbidden.
        var context = await service.GetWhiteBalanceContextAsync(
            new ImageFile(Path.Combine(_root, "photo.cr2")),
            settings);

        Assert.Null(context);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
