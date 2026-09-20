using System.Reflection;
using System.Xml.Linq;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class XmpPublicationTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("xmp-publication");

    [Fact]
    public async Task MixedSelection_PublishesFiveOfSixWithoutChangingRevisions_AndIsIdempotent()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var images = new[]
        {
            await SeedAsync(catalog, "rated", rating: 4),
            await SeedAsync(catalog, "rated2", rating: 2),
            await SeedAsync(catalog, "picked", flag: ImageFlag.Picked),
            await SeedAsync(catalog, "label", label: ColorLabel.Red),
            await SeedAsync(catalog, "crop", settings: new() { Crop = Crop() }),
            await SeedAsync(catalog, "untouched")
        };
        var before = await catalog.LoadAssessmentSnapshotsAsync(images.Select(i => i.CatalogId).ToArray());
        await using var vm = CreateVm(catalog, images);
        Assert.Empty(Directory.GetFiles(_fixture.Root, "*.xmp"));
        await vm.WriteXmpSidecarsCommand.ExecuteAsync(null);
        Assert.Equal("XMP sidecars written for 5 photos", vm.TransientStatus);
        Assert.Equal(5, Directory.GetFiles(_fixture.Root, "*.xmp").Length);
        Assert.False(File.Exists(Sidecar(images[5])));
        Assert.Equal(4, Facts(images[0]).Rating.Value);
        Assert.Equal(2, Facts(images[1]).Rating.Value);
        Assert.Equal(ImageFlag.Picked, Facts(images[2]).Flag.Value);
        Assert.Equal(ColorLabel.Red, Facts(images[3]).Label.Value);
        Assert.Equal(.1, Facts(images[4]).Crop.Value.Left);
        var after = await catalog.LoadAssessmentSnapshotsAsync(images.Select(i => i.CatalogId).ToArray());
        Assert.Equal(before, after);
        Assert.All(images, image => Assert.Equal(AssessmentAxes.None, image.PendingAssessmentAxes));
        var files = images.Take(5).Select(image =>
            (Path: Sidecar(image), Bytes: File.ReadAllBytes(Sidecar(image)), Time: File.GetLastWriteTimeUtc(Sidecar(image)))).ToArray();
        await vm.WriteXmpSidecarsCommand.ExecuteAsync(null);
        Assert.Equal("XMP sidecars written for 5 photos", vm.TransientStatus);
        foreach (var file in files)
        {
            Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path));
            Assert.Equal(file.Time, File.GetLastWriteTimeUtc(file.Path));
        }
    }

    [Fact]
    public async Task PersistedCropPresence_DistinguishesRotationOnly_AndPreservesForeignProperties()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var rotation = await SeedAsync(catalog, "rotation", settings: new() { Rotation = 90 });
        var rotatedCrop = await SeedAsync(catalog, "rotated-crop", settings: new() { Rotation = 90, Crop = Crop() });
        var rated = await SeedAsync(catalog, "rated-rotation", 4, ImageFlag.Picked, ColorLabel.Red,
            new() { Rotation = 90 });
        var document = XmpSidecarDocument.Create();
        var description = document.Descendants(XmpSidecarDocument.Rdf + "Description").Single();
        XmpSidecarDocument.Merge(document, new(rated.CatalogId, rated.FilePath,
            ImageFlag.Rejected, 1, ColorLabel.Blue, 0, DateTime.UnixEpoch, AssessmentAxes.None),
            AssessmentAxes.All | AssessmentAxes.Crop, ColorLabelNames.Defaults,
            new(XmpCropProjectionKind.Portable, Crop()));
        description.SetAttributeValue(XmpSidecarDocument.CameraRaw + "Exposure2012", "1.25");
        description.SetAttributeValue(XmpSidecarDocument.CameraRaw + "ProcessVersion", "11.0");
        document.Save(Sidecar(rated));
        await using var vm = CreateVm(catalog, [rotation, rotatedCrop, rated]);
        // Live settings are deliberately stale: persisted settings govern eligibility.
        rotatedCrop.EditSettings = new();
        await vm.WriteXmpSidecarsCommand.ExecuteAsync(null);
        Assert.False(File.Exists(Sidecar(rotation)));
        Assert.True(File.Exists(Sidecar(rotatedCrop)));
        Assert.Equal(4, Facts(rated).Rating.Value);
        Assert.Equal(ImageFlag.Picked, Facts(rated).Flag.Value);
        Assert.Equal(ColorLabel.Red, Facts(rated).Label.Value);
        Assert.Equal(.1, Facts(rated).Crop.Value.Left);
        var result = XDocument.Load(Sidecar(rated));
        Assert.Contains(result.Descendants().Attributes(), a => a.Name == XmpSidecarDocument.CameraRaw + "Exposure2012" && a.Value == "1.25");
        Assert.Contains(result.Descendants().Attributes(), a => a.Name == XmpSidecarDocument.CameraRaw + "ProcessVersion" && a.Value == "11.0");
        Assert.Equal("XMP sidecars written for 2 photos", vm.TransientStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedWriteOrRetryableCrop_RemainsPendingInCatalogAndLiveStatus(bool cropRetry)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var image = await SeedAsync(catalog, "pending", 3,
            settings: cropRetry ? new() { Crop = Crop() } : null);
        var availability = new TestSourceAvailabilityService(SourceAvailability.AvailableLocally)
        {
            Resolver = path => !cropRetry || path == image.FilePath
                ? SourceAvailability.Unavailable : SourceAvailability.AvailableLocally
        };
        await using var vm = CreateVm(catalog, [image], availability: availability);
        await vm.WriteXmpSidecarsCommand.ExecuteAsync(null);
        Assert.Equal("XMP sidecars written for 0 photos; XMP writes remain pending for 1 photo", vm.TransientStatus);
        Assert.Equal(cropRetry ? AssessmentAxes.Crop : AssessmentAxes.All, image.PendingAssessmentAxes);
        Assert.Equal(cropRetry, File.Exists(Sidecar(image)));
        if (cropRetry) Assert.Equal(3, Facts(image).Rating.Value);
    }

    [Fact]
    public async Task ActiveFallback_AndPrimaryOnly_AndClaimedTargets()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var active = await SeedAsync(catalog, "active", 3);
        var claimed = await SeedAsync(catalog, "claimed", 4);
        var version = await SeedAsync(catalog, "version", 5);
        version.Version = 2;
        await using var vm = CreateVm(catalog, [active, claimed, version]);
        Claim(vm, claimed.FilePath, true);
        await vm.WriteXmpSidecarsCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Sidecar(active)));
        Assert.False(File.Exists(Sidecar(claimed)));
        Assert.False(File.Exists(Sidecar(version)));
        Assert.Equal(AssessmentAxes.None, (await Snapshot(catalog, claimed)).PendingAxes);
        vm.DeselectAllCommand.Execute(null);
        vm.SelectedImage = active;
        await vm.WriteXmpSidecarsCommand.ExecuteAsync(null);
        Assert.Equal("XMP sidecars written for 1 photo", vm.TransientStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NothingPublishable_ReportsNoSidecars(bool emptySelection)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var untouched = await SeedAsync(catalog, "untouched");
        await using var vm = CreateVm(catalog, emptySelection ? [] : [untouched]);
        await vm.WriteXmpSidecarsCommand.ExecuteAsync(null);
        Assert.Equal("No XMP sidecars to write", vm.TransientStatus);
        Assert.False(File.Exists(Sidecar(untouched)));
        Assert.Equal(AssessmentAxes.None, (await Snapshot(catalog, untouched)).PendingAxes);
    }

    [Fact]
    public async Task CancelledPendingMark_LeavesCatalogUnchangedAndReleasesGate()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var image = await SeedAsync(catalog, "cancelled-mark", 3);
        var before = await Snapshot(catalog, image);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            catalog.MarkXmpPublicationPendingAsync([image.CatalogId], cancellation.Token));
        Assert.Equal(before, await Snapshot(catalog, image));
        var marked = Assert.Single(await catalog.MarkXmpPublicationPendingAsync([image.CatalogId]));
        Assert.Equal(AssessmentAxes.All, marked.Axes);
    }

    private async Task<ImageFile> SeedAsync(CatalogService catalog, string name,
        int rating = 0, ImageFlag flag = ImageFlag.Unflagged, ColorLabel label = ColorLabel.None,
        EditSettings? settings = null)
    {
        var image = new ImageFile(_fixture.Path(name + ".jpg"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        image.EditSettings = settings ?? new();
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        await catalog.MutateAssessmentsAsync([new(image.CatalogId, AssessmentAxes.All,
            flag, rating, label)]);
        return image;
    }

    private MainWindowViewModel CreateVm(CatalogService catalog, ImageFile[] images,
        int capacity = 4, TestSourceAvailabilityService? availability = null)
    {
        var vm = _fixture.CreateViewModel(catalog, new NullBaseLoader(),
            _ => Task.CompletedTask, timeProvider: new TestTimeProvider());
        vm.Browse.SetImages(images);
        foreach (var image in images) vm.ToggleImageSelection(image);
        vm.XmpSidecarMode = XmpSidecarMode.ReadWrite;
        var writer = new XmpSidecarWriter(catalog, ColorLabelNames.Defaults,
            availability ?? new(SourceAvailability.AvailableLocally), capacity,
            (string _, out int orientation) => { orientation = 1; return true; });
        writer.Start();
        SetField(vm, "_xmpWriter", writer);
        return vm;
    }

    private static void SetField(MainWindowViewModel vm, string name, object value) =>
        typeof(MainWindowViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);
    private static XmpSidecarWriter Writer(MainWindowViewModel vm) =>
        (XmpSidecarWriter)typeof(MainWindowViewModel).GetField("_xmpWriter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
    private static void Claim(MainWindowViewModel vm, string path, bool claimed) =>
        typeof(MainWindowViewModel).GetMethod("SetDeleteTargetsClaimed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [new[] { path }, claimed]);
    private static string Sidecar(ImageFile image) => image.FilePath + ".xmp";
    private static XmpSidecarFacts Facts(ImageFile image) =>
        XmpSidecarDocument.ReadFacts(XDocument.Load(Sidecar(image)), ColorLabelNames.Defaults);
    private static async Task<AssessmentSnapshot> Snapshot(CatalogService catalog, ImageFile image) =>
        Assert.Single(await catalog.LoadAssessmentSnapshotsAsync([image.CatalogId]));
    private static CropRegion Crop() => new() { Left = .1, Top = .2, Right = .8, Bottom = .9 };
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Dispose() => _fixture.Dispose();
}
