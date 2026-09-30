using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncSpotControlTests(ITestOutputHelper output)
{
    [Fact]
    public async Task G1_RenderControl()
    {
        SyncPhotoGateSupport.RequirePerformance();
        var source = SyncSpotGateSupport.Source();
        var reset = SyncSpotGateSupport.Reset(source);
        var path = SyncSpotGateSupport.Fixture();
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var primary = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(primary, source);
        var version = await catalog.CreateVersionAsync(primary);
        Assert.NotNull(version);
        Assert.Equal(2, version.Version);
        await catalog.SaveEditSettingsAsync(version.CatalogId, reset);
        var versions = (await catalog.LoadImageStatesAsync([path]))[path];
        Assert.Equal(2, versions.Count);
        Assert.Equal(EditSettingsJson.Serialize(source), EditSettingsJson.Serialize(versions[0].EditSettings));
        Assert.Equal(EditSettingsJson.Serialize(reset), EditSettingsJson.Serialize(versions[1].EditSettings));
        Assert.Null(versions[1].EditSettings.Repairs);
        var loader = new GatedBaseImageLoader(new RawBaseLoader(), new SourceAvailabilityService());
        using var basis = loader.LoadFullBase(new ImageFile(path), BaseDecodeSettings.From(source), CancellationToken.None);
        Assert.NotNull(basis);
        var pipeline = new RenderPipeline();
        var request = new RenderRequest(basis, source, RenderIntent.Export, null, new(false));
        using var first = pipeline.Render(request);
        using var second = pipeline.Render(request);
        var differing = SyncPhotoGateSupport.DifferingCodes(first.Image, second.Image);
        Assert.Equal(0, differing);
        output.WriteLine($"SYNC_SPOT gate=G1 pid={Environment.ProcessId} differingCodes={differing} width={first.Image.Width} height={first.Image.Height} resetAsserted=true mode=render-control pastePending=IMPLEMENT");
    }

    [Fact]
    public async Task G2_EqualOrientationControl()
    {
        SyncPhotoGateSupport.RequirePerformance();
        var source = SyncSpotGateSupport.Source();
        var before = SyncSpotGateSupport.Reset(source);
        using var directory = new TemporaryDirectory();
        var original = SyncSpotGateSupport.Fixture();
        var path = Path.Combine(directory.Path, "equal.cr2");
        SyncProfileGateSupport.RequireLocal(original);
        File.Copy(original, path);
        SyncSpotGateSupport.AssertSerial(path);
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var target = new ImageFile(path) { CatalogId = await catalog.GetOrCreateImageAsync(path) };
        await catalog.SaveEditSettingsAsync(target.CatalogId, before);
        Assert.Empty((await catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
        var spot = Assert.Single(EditSettingsTransfer.Groups, group => group.Name == "Spot Removal");
        // Existing service control: the compatibility-aware UI paste arrives at IMPLEMENT.
        var actual = PhotoSettingsTransfer.Apply(new ImageFile(original), source, target, before, [spot],
            new PhotoFrameFactsReader(new SourceAvailabilityService()), out var reframed, out var unavailable);
        Assert.False(reframed);
        Assert.False(unavailable);
        Assert.Equal(EditSettingsJson.Serialize(source), EditSettingsJson.Serialize(actual));
        Assert.NotSame(source.Repairs, actual.Repairs);
        Assert.All(actual.Repairs!, repair => Assert.NotSame(source.Repairs![actual.Repairs!.IndexOf(repair)], repair));
        await catalog.SaveEditSettingsBatchWithHistoryAsync([new(target.CatalogId, actual, before)], "Paste settings");
        await SyncSpotGateSupport.AssertTransfer(catalog, target, before, source);
        await catalog.SaveEditSettingsBatchWithHistoryAsync([new(target.CatalogId, actual, actual)], "Paste settings");
        await SyncSpotGateSupport.AssertTransfer(catalog, target, before, source);
        var error = SyncSpotGateSupport.CenterError(source.Repairs!, actual.Repairs!);
        Assert.Equal(0, error);
        output.WriteLine($"SYNC_SPOT gate=G2 pid={Environment.ProcessId} centerErrorUnits={error} coordinates=256 historySteps=1 equalRepeatSteps=0 mode=existing-transfer-service-control");
    }

    [Fact]
    public void Sanity()
    {
        SyncPhotoGateSupport.RequirePerformance();
        var source = SyncSpotGateSupport.Source();
        SyncSpotGateSupport.Reset(source);
        SyncSpotGateSupport.PayloadBytes(output);
        var changed = source.Clone();
        changed.Repairs![0].U += 1d / 16384;
        Assert.Equal(1, SyncSpotGateSupport.CenterError(source.Repairs!, changed.Repairs));
        using var a = RenderPipelineTestSupport.CreateBase([0, 123, 65535]);
        using var b = RenderPipelineTestSupport.CreateBase([0, 124, 65535]);
        Assert.Equal(0, SyncPhotoGateSupport.DifferingCodes(a.Pixels, a.Pixels));
        Assert.Equal(1, SyncPhotoGateSupport.DifferingCodes(a.Pixels, b.Pixels));
        output.WriteLine($"SYNC_SPOT gate=Sanity pid={Environment.ProcessId} mutationChecks=2 resetAsserted=true");
    }
}


