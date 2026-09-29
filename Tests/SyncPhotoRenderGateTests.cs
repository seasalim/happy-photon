using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncPhotoRenderGateTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(SyncPhotoGateSupport.Raw)]
    [InlineData(SyncPhotoGateSupport.Heic)]
    public async Task G1(string name)
    {
        SyncPhotoGateSupport.RequirePerformance();
        var source = SyncPhotoGateSupport.PhotoA();
        var resetVersion = SyncPhotoGateSupport.ResetPhotoGroups(source);
        var file = new ImageFile(SyncPhotoGateSupport.LocalFixture(name)) { EditSettings = source };
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var primary = await catalog.GetOrCreateImageAsync(file.FilePath);
        await catalog.SaveEditSettingsAsync(primary, source);
        var version = await catalog.CreateVersionAsync(primary);
        Assert.NotNull(version);
        Assert.Equal(2, version.Version);
        await catalog.SaveEditSettingsAsync(version.CatalogId, resetVersion);
        var versions = (await catalog.LoadImageStatesAsync([file.FilePath]))[file.FilePath];
        Assert.Equal(2, versions.Count);
        Assert.Equal(EditSettingsJson.Serialize(source), EditSettingsJson.Serialize(versions[0].EditSettings));
        Assert.Equal(EditSettingsJson.Serialize(resetVersion), EditSettingsJson.Serialize(versions[1].EditSettings));
        var loader = new GatedBaseImageLoader(new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()),
            new SourceAvailabilityService());
        using var basis = loader.LoadFullBase(file, BaseDecodeSettings.From(source), CancellationToken.None);
        Assert.NotNull(basis);
        var pipeline = new RenderPipeline();
        var request = new RenderRequest(basis, source, RenderIntent.Export, null, new(false));
        using var first = pipeline.Render(request);
        var target = new ImageFile(file.FilePath) { EditSettings = resetVersion, Version = version.Version, CatalogId = version.CatalogId };
        var groups = EditSettingsTransfer.Groups.Where(group =>
            group.Name is "Crop & Straighten" or "Geometry" or "Locals").ToArray();
        var transferred = PhotoSettingsTransfer.Apply(file, source, target, resetVersion, groups,
            new PhotoFrameFactsReader(new SourceAvailabilityService()), out var reframed, out var unavailable);
        Assert.False(reframed);
        Assert.False(unavailable);
        Assert.Equal(EditSettingsJson.Serialize(source), EditSettingsJson.Serialize(transferred));
        await catalog.SaveEditSettingsBatchWithHistoryAsync(
            [new(version.CatalogId, transferred, resetVersion)], "Paste settings");
        var persisted = (await catalog.LoadImageStatesAsync([file.FilePath]))[file.FilePath];
        Assert.Equal(EditSettingsJson.Serialize(source), EditSettingsJson.Serialize(persisted[1].EditSettings));
        using var second = pipeline.Render(request with { Settings = transferred });
        var differing = SyncPhotoGateSupport.DifferingCodes(first.Image, second.Image);
        output.WriteLine($"SYNC_PHOTO gate=G1 fixture={name} pid={Environment.ProcessId} differingCodes={differing} width={first.Image.Width} height={first.Image.Height} resetAsserted=true");
        Assert.Equal(0, differing);
        Assert.Null(resetVersion.Locals);
    }

    [Fact]
    public void Sanity()
    {
        SyncPhotoGateSupport.RequirePerformance();

        SyncPhotoGateSupport.ResetPhotoGroups(SyncPhotoGateSupport.PhotoA());
        using var a = RenderPipelineTestSupport.CreateBase([0, 123, 65535]);
        using var b = RenderPipelineTestSupport.CreateBase([0, 124, 65535]);
        Assert.Equal(0, SyncPhotoGateSupport.DifferingCodes(a.Pixels, a.Pixels));
        Assert.Equal(1, SyncPhotoGateSupport.DifferingCodes(a.Pixels, b.Pixels));
        output.WriteLine($"SYNC_PHOTO gate=Sanity pid={Environment.ProcessId} helperMutationDetected=true");
    }
}

