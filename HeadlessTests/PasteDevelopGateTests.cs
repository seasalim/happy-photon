using System.Text.Json;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PasteDevelopGateTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public Task G1_Raw()
    {
        PasteGateSupport.RequirePerformance();

        return RunAsync("canon-eos-6d-iso-6400.cr2", real: true, measure: true);
    }

    [AvaloniaFact]
    public Task G1_Heic()
    {
        PasteGateSupport.RequirePerformance();

        return RunAsync("iphone-14-pro-iso-1000.heic", real: true, measure: true);
    }

    [AvaloniaFact]
    public Task Sanity_Raw() => RunAsync("canon-eos-6d-iso-6400.cr2", real: true);

    [AvaloniaFact]
    public Task Sanity_Heic() => RunAsync("iphone-14-pro-iso-1000.heic", real: true);

    [AvaloniaTheory]
    [InlineData("target.dng", false)]
    [InlineData("target.dng", true)]
    [InlineData("target.heic", false)]
    [InlineData("target.heic", true)]
    public async Task G2_NoDecode(string name, bool whiteBalanceOnly)
    {
        var counts = new List<int>();

        for (var run = 0; run < 5; run++)
        {
            counts.Add(await RunAsync(name, real: false, whiteBalanceOnly: whiteBalanceOnly));
        }

        output.WriteLine($"G2 {name} wbOnly={whiteBalanceOnly}: " +
            $"decodes=[{string.Join(",", counts)}]; median={counts.Order().ElementAt(2)}; runs=5");
    }

    private async Task<int> RunAsync(string name, bool real,
        bool measure = false, bool whiteBalanceOnly = false)
    {
        if (real && !measure)
        {
            Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PASTE_SANITY") != "1",
                "Set HAPPY_PHOTON_PASTE_SANITY=1 for untimed real-fixture checks.");
        }

        using var fixture = new CatalogVmFixture("paste-develop-gate");
        using var catalog = await fixture.CreateCatalogAsync();
        var loader = new PasteGateLoader(real
            ? new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()) : null);
        ISourceAvailabilityService availability = real ? new SourceAvailabilityService()
            : new TestSourceAvailabilityService(SourceAvailability.AvailableLocally);
        await using var vm = fixture.CreateViewModel(catalog, loader, _ => Task.CompletedTask,
            availability, timeProvider: new TestTimeProvider());
        vm.ImageService.Previews.AdjacentWarmEnabled = false;
        // A pinned 1600-device-pixel Fit demand uses the interactive base. No
        // shown MainWindow is needed to observe the installed preview bitmap.
        vm.PublishRequiredDeviceLongEdge(1600);
        var targetPath = real ? GoldenTestPaths.Asset(name) : fixture.Path(name);

        if (real)
        {
            Assert.True(File.Exists(targetPath), $"Missing gate fixture: {targetPath}");
            Assert.Equal(SourceAvailability.AvailableLocally, availability.GetAvailability(targetPath));
        }

        // Browse copy captures the source's in-memory sliders without decoding it.
        var source = await PasteGateSupport.StoreAsync(catalog, fixture.Path("source.dng"),
            PasteGateSupport.SourceLook(whiteBalanceOnly));
        vm.Browse.SetImages([source]);
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        Assert.True(vm.HasCopiedSettings);
        var target = await PasteGateSupport.StoreAsync(catalog, targetPath, new EditSettings());
        Assert.Equal(BaseDecodeSettings.From(source.EditSettings), BaseDecodeSettings.From(target.EditSettings));
        await PasteGateSupport.SelectLoadedAsync(vm, target);
        var expected = target.EditSettings.Clone();

        if (whiteBalanceOnly)
        {
            vm.RestorePasteGroups(EditSettingsTransfer.LookGroups.ToDictionary(
                group => group.Name, group => group.Name == "White Balance"));
            EditSettingsTransfer.ApplyGroups(source.EditSettings, expected,
                [Assert.Single(EditSettingsTransfer.Groups, group => group.Name == "White Balance")]);
        }
        else
        {
            EditSettingsTransfer.ApplyGroups(source.EditSettings, expected);
        }

        Assert.True(loader.Starts > 0, "The initial base must really have loaded.");
        var before = loader.Starts;
        var milliseconds = await PasteGateSupport.PasteAsync(vm, measure);
        await TestWaits.UntilAsync(() => vm.ImageService.Previews.PreviewActivityCount == 0);
        var decodes = loader.Starts - before;
        Assert.Equal(EditSettingsJson.Serialize(expected), EditSettingsJson.Serialize(target.EditSettings));
        Assert.Equal(0, decodes);
        output.WriteLine("PASTE_GATE " + JsonSerializer.Serialize(new
        {
            gate = measure ? "G1" : real ? "sanity" : "G2",
            fixture = name,
            pid = Environment.ProcessId,
            milliseconds,
            decodes,
            whiteBalanceOnly,
            fitDeviceLongEdge = vm.RequiredDeviceLongEdge
        }));

        return decodes;
    }
}

