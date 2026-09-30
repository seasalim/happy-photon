using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class SyncSpotPasteGateTests(ITestOutputHelper output)
{
    [Fact]
    public async Task G1_VersionPasteRendersIdentically()
    {
        SyncPhotoGateSupport.RequirePerformance();
        var original = SyncSpotGateSupport.Fixture();
        using var fixture = new CatalogVmFixture("sync-spot-version");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new SourceAvailabilityService(), timeProvider: new TestTimeProvider());
        vm.ImageService.Previews.AdjacentWarmEnabled = false;
        var settings = SyncSpotGateSupport.Source();
        var source = new ImageFile(original) { CatalogId = await catalog.GetOrCreateImageAsync(original), EditSettings = settings };
        await catalog.SaveEditSettingsAsync(source.CatalogId, settings);
        var version = await catalog.CreateVersionAsync(source.CatalogId);
        Assert.NotNull(version);
        var reset = SyncSpotGateSupport.Reset(settings);
        await catalog.SaveEditSettingsAsync(version.CatalogId, reset);
        var target = new ImageFile(original) { CatalogId = version.CatalogId, Version = version.Version, EditSettings = reset };
        Assert.Null((await catalog.LoadImageStatesAsync([original]))[original].Single(item => item.Version == 2).EditSettings.Repairs);
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.PasteFrameReader.Opening = (_, _) => Assert.Fail("Same-file paste read a header");
        vm.Browse.SetImages([target]);
        vm.Browse.SelectAllVisible();
        vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(group => group.Name, group => group.Name == "Spot Removal"));
        vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        await SyncSpotGateSupport.AssertTransfer(catalog, target, reset, settings);
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        await SyncSpotGateSupport.AssertTransfer(catalog, target, reset, settings);
        var loader = new GatedBaseImageLoader(new RawBaseLoader(), new SourceAvailabilityService());
        using var basis = loader.LoadFullBase(source, BaseDecodeSettings.From(settings), CancellationToken.None);
        Assert.NotNull(basis);
        var pipeline = new RenderPipeline();
        var request = new RenderRequest(basis, settings, RenderIntent.Export, null, new(false));
        using var first = pipeline.Render(request);
        using var second = pipeline.Render(request with { Settings = target.EditSettings });
        var differing = SyncPhotoGateSupport.DifferingCodes(first.Image, second.Image);
        Assert.Equal(0, differing);
        output.WriteLine($"SYNC_SPOT gate=G1 differingCodes={differing} resetAsserted=true historySteps=1 equalRepeatSteps=0 mode=paste");
    }

    [Fact]
    public async Task G2_RotatedCopiesMapBothRepairCenters()
    {
        SyncPhotoGateSupport.RequirePerformance();
        var original = SyncSpotGateSupport.Fixture();
        using var fixture = new CatalogVmFixture("sync-spot-rotated");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new SourceAvailabilityService(), timeProvider: new TestTimeProvider());
        vm.ImageService.Previews.AdjacentWarmEnabled = false;
        var source = new ImageFile(original) { EditSettings = SyncSpotGateSupport.Source() };
        vm.PasteFrameReader.Read(source, source.EditSettings);
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(group => group.Name, group => group.Name == "Spot Removal"));
        vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
        var maximum = 0d;

        foreach (var orientation in new ushort[] { 3, 6, 8 })
        {
            var path = SyncSpotRotatedFixture.Create(original, fixture.Path(""), orientation);
            var before = SyncSpotGateSupport.Reset(source.EditSettings);
            var target = new ImageFile(path) { CatalogId = await catalog.GetOrCreateImageAsync(path), EditSettings = before };
            await catalog.SaveEditSettingsAsync(target.CatalogId, before);
            vm.Browse.SetImages([target]);
            vm.Browse.SelectAllVisible();
            await vm.PasteEditSettingsCommand.ExecuteAsync(null);
            var expected = source.EditSettings.Clone();
            expected.Repairs = source.EditSettings.Repairs!.Select(repair => orientation == 3 ? repair with { } : orientation == 6
                ? repair with { U = 1 - repair.V, V = repair.U, Su = 1 - repair.Sv, Sv = repair.Su }
                : repair with { U = repair.V, V = 1 - repair.U, Su = repair.Sv, Sv = 1 - repair.Su }).ToList();
            await SyncSpotGateSupport.AssertTransfer(catalog, target, before, expected);
            var error = SyncSpotGateSupport.CenterError(expected.Repairs, target.EditSettings.Repairs!);
            maximum = Math.Max(maximum, error);
            Assert.InRange(error, 0, 1);
            await vm.PasteEditSettingsCommand.ExecuteAsync(null);
            await SyncSpotGateSupport.AssertTransfer(catalog, target, before, expected);
        }

        output.WriteLine($"SYNC_SPOT gate=G2 centerErrorUnits={maximum} orientations=3,6,8 historySteps=3 equalRepeatSteps=0 mode=paste");
    }
}
