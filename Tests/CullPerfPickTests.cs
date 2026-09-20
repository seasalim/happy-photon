using System.Xml.Linq;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CullPerfPickTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FeedbackAndWriterIdleRequirePersistedPairsAndSuccessfulSidecars(bool failWrite)
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(directory.Path, "catalog"));
        await catalog.InitializeAsync();
        var images = new[] { "capture.jpg", "capture.cr2" }.Select(name =>
            new ImageFile(Path.Combine(directory.Path, name))).ToArray();
        foreach (var image in images)
        {
            File.WriteAllBytes(image.FilePath, []);
            await image.EnsureCatalogIdAsync(catalog);
        }
        var recorder = new CullPerfRecorder();
        await using var vm = new MainWindowViewModel(catalog, new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.Browse.SetImages(images);
        vm.RestoreShowCapturePairs(true);
        vm.SelectedImage = images[0];
        vm.XmpSidecarMode = XmpSidecarMode.ReadWrite;
        vm.ImageService.Previews.CullPerf = recorder;
        await vm.TogglePickedImageCommand.ExecuteAsync(null);
        Assert.Contains(recorder.Snapshot(), item => item.Kind == "Feedback");
        Assert.True(Assert.Single(recorder.Snapshot(), item => item.Kind == "PickReceipt").OperationId > 0);
        var snapshots = await catalog.LoadAssessmentSnapshotsAsync(images.Select(image => image.CatalogId).ToArray());
        Assert.Equal(2, snapshots.Count);
        Assert.All(snapshots, snapshot => Assert.Equal(ImageFlag.Picked, snapshot.Flag));
        Assert.All(snapshots, snapshot => Assert.Equal(AssessmentAxes.Flag, snapshot.PendingAxes));
        await using var writer = new XmpSidecarWriter(catalog, ColorLabelNames.Defaults) { CullPerf = recorder };
        writer.Start();
        if (failWrite)
            File.WriteAllBytes(images[0].FilePath + ".xmp", new byte[XmpSidecarReader.MaximumSidecarBytes + 1]);
        foreach (var snapshot in snapshots)
            Assert.True(writer.TryEnqueue(snapshot, snapshot.PendingAxes,
                images.Select(image => image.FilePath).ToArray(), XmpSidecarNaming.FullName));
        await writer.DrainAsync().WaitAsync(TestWaits.Condition);
        Assert.Contains(recorder.Snapshot(), item => item.Kind == "SidecarIdle");
        Assert.Contains(recorder.Snapshot(), item => item.Kind == (failWrite ? "SidecarFailed" : "SidecarSucceeded"));
        var final = await catalog.LoadAssessmentSnapshotsAsync(images.Select(image => image.CatalogId).ToArray());
        var allCleared = final.All(snapshot => snapshot.PendingAxes == AssessmentAxes.None);
        Assert.Equal(!failWrite, allCleared);
        foreach (var image in failWrite ? images.Skip(1) : images)
        {
            var document = XDocument.Load(image.FilePath + ".xmp");
            var facts = XmpSidecarDocument.ReadFacts(document, ColorLabelNames.Defaults);
            Assert.Equal(ImageFlag.Picked, facts.Flag.Value);
        }
        if (failWrite)
        {
            var pending = final.Single(snapshot => snapshot.ImageId == images[0].CatalogId);
            Assert.Equal(AssessmentAxes.Flag, pending.PendingAxes);
        }
    }
}
