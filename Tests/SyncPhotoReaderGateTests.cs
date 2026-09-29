using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class SyncPhotoReaderGateTests(ITestOutputHelper output)
{
    [Fact]
    public async Task G4_ColdBatchReadsAndTransfersEveryTargetWithoutDecode()
    {
        SyncPhotoGateSupport.RequirePerformance();
        using var fixture = new CatalogVmFixture("sync-photo-reader");
        using var catalog = await fixture.CreateCatalogAsync();
        var decodeEvents = new CullPerfRecorder();
        var loader = new BaseLoaderRouter(new RawBaseLoader { CullPerf = decodeEvents },
            new StandardBaseLoader { CullPerf = decodeEvents });
        await using var vm = fixture.CreateViewModel(catalog, loader, _ => Task.CompletedTask,
            new SourceAvailabilityService(), timeProvider: new TestTimeProvider());
        var source = new ImageFile(SyncPhotoGateSupport.LocalFixture(SyncPhotoGateSupport.Raw))
        {
            EditSettings = new() { Crop = new() { Left = .13, Top = .17, Right = .89, Bottom = .93 }, HorizonRotation = 3.25 }
        };
        var heic = SyncPhotoGateSupport.LocalFixture(SyncPhotoGateSupport.Heic);
        var otherHeic = SyncPhotoGateSupport.LocalFixture("reference.heic");
        var targets = new ImageFile[200];

        for (var index = 0; index < targets.Length; index++)
        {
            var original = index < 100 ? source.FilePath : index % 2 == 0 ? heic : otherHeic;
            var path = fixture.Path($"cold-{index:D3}{Path.GetExtension(original)}");
            File.Copy(original, path);
            targets[index] = new ImageFile(path) { CatalogId = await catalog.GetOrCreateImageAsync(path) };
        }

        var opens = new Dictionary<string, List<string>>();
        var started = new Dictionary<string, long>();
        var elapsed = new Dictionary<string, double>();
        vm.PasteFrameReader.Opening = (path, reason) =>
        {
            if (!opens.TryGetValue(path, out var reasons)) opens[path] = reasons = [];
            reasons.Add(reason);
            started[path] = Stopwatch.GetTimestamp();
        };
        vm.PasteFrameReader.ReadCompleted = path =>
        {
            if (started.Remove(path, out var start)) elapsed[path] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        };
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.Browse.SetImages(targets);
        vm.Browse.SelectAllVisible();
        vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(group => group.Name, group => group.Name == "Crop & Straighten"));
        vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(201, opens.Count);
        Assert.All(opens.Values, reasons => Assert.Equal(new[] { "frame" }, reasons));
        Assert.DoesNotContain(decodeEvents.Snapshot(), item => item.Kind == "NativeStart");
        Assert.Equal(0, decodeEvents.LostEvents);
        var persisted = await catalog.LoadImageStatesAsync(targets.Select(target => target.FilePath).ToArray());
        var portrait = 0;

        foreach (var target in targets)
        {
            var frame = vm.PasteFrameReader.Read(target, target.EditSettings, cachedOnly: true)!.Value;
            var expected = CropTransfer.Apply(source.EditSettings.Crop!, (5496, 3670), frame);
            Assert.Equal(expected.Left, target.EditSettings.Crop!.Left);
            Assert.Equal(expected.Top, target.EditSettings.Crop.Top);
            Assert.Equal(expected.Right, target.EditSettings.Crop.Right);
            Assert.Equal(expected.Bottom, target.EditSettings.Crop.Bottom);
            Assert.Equal(3.25, target.EditSettings.HorizonRotation);
            Assert.Equal(EditSettingsJson.Serialize(target.EditSettings),
                EditSettingsJson.Serialize(Assert.Single(persisted[target.FilePath]).EditSettings));
            if (frame.Width < frame.Height) portrait++;
        }

        Assert.True(portrait > 0);
        var rawMedian = Median(targets[..100].Select(target => elapsed[target.FilePath]));
        var heicMedian = Median(targets[100..].Select(target => elapsed[target.FilePath]));
        output.WriteLine($"SYNC_PHOTO gate=G4 pid={Environment.ProcessId} rawMedianMs={rawMedian:R} heicMedianMs={heicMedian:R} targetOpens=200 decodes=0 transferred=200 portrait={portrait}");
        Assert.True(rawMedian <= 6.62, $"RAW: {rawMedian} ms");
        Assert.True(heicMedian <= 11.57, $"HEIC: {heicMedian} ms");
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();

        return (ordered[49] + ordered[50]) / 2;
    }
}
