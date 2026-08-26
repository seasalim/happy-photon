using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class XmpSidecarWriterTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [Fact]
    public async Task CapacityCountsInFlightDistinctPathsWithoutClearingDebt()
    {
        using var catalog = await CreateCatalogAsync();
        var first = await PendingRatingAsync(catalog, "first.cr3", 3);
        var second = await PendingRatingAsync(catalog, "second.cr3", 4);
        var entered = NewSignal();
        var release = NewSignal();
        await using var writer = new XmpSidecarWriter(
            catalog, ColorLabelNames.Defaults, capacity: 1);
        writer.BeforePromotionAsync = async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        writer.Start();

        Assert.True(writer.TryEnqueue(first, AssessmentAxes.Rating,
            [first.FilePath], XmpSidecarNaming.FullName));
        await entered.Task.WaitAsync(TestWaits.Condition);
        Assert.False(writer.TryEnqueue(second, AssessmentAxes.Rating,
            [second.FilePath], XmpSidecarNaming.FullName));
        release.TrySetResult();
        await writer.DrainAsync();

        Assert.Equal(AssessmentAxes.Rating,
            Assert.Single(await catalog.LoadAssessmentSnapshotsAsync(
                [second.ImageId])).PendingAxes);
    }

    [Fact]
    public async Task AlternateCandidateAppearingBeforePromotionWinsRetry()
    {
        using var catalog = await CreateCatalogAsync();
        var snapshot = await PendingRatingAsync(catalog, "race.cr3", 5);
        var full = snapshot.FilePath + ".xmp";
        var baseName = Path.ChangeExtension(snapshot.FilePath, ".xmp");
        WriteRating(full, 1);
        var captured = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(full, captured);
        var promotions = 0;
        await using var writer = new XmpSidecarWriter(
            catalog, ColorLabelNames.Defaults);
        writer.BeforePromotionAsync = (_, _) =>
        {
            if (Interlocked.Increment(ref promotions) == 1)
            {
                WriteRating(baseName, 2);
                File.SetLastWriteTimeUtc(baseName, captured.AddMinutes(1));
            }
            return Task.CompletedTask;
        };
        writer.Start();

        Assert.True(writer.TryEnqueue(snapshot, AssessmentAxes.Rating,
            [snapshot.FilePath], XmpSidecarNaming.FullName));
        await writer.DrainAsync();

        Assert.Equal("1", ReadRating(full));
        Assert.Equal("5", ReadRating(baseName));
        Assert.True(promotions >= 2);
    }

    [Fact]
    public async Task RevisionMismatchCarriesForwardNewestSnapshotUntilMaskClears()
    {
        using var catalog = await CreateCatalogAsync();
        var original = await PendingRatingAsync(catalog, "interleaved.cr3", 2);
        var promotions = 0;
        await using var writer = new XmpSidecarWriter(
            catalog, ColorLabelNames.Defaults);
        writer.BeforePromotionAsync = async (_, _) =>
        {
            if (Interlocked.Increment(ref promotions) == 1)
            {
                await catalog.MutateAssessmentsAsync(
                    [new AssessmentMutation(
                        original.ImageId, AssessmentAxes.Rating, Rating: 5)],
                    AssessmentAxes.Rating);
            }
        };
        writer.Start();

        Assert.True(writer.TryEnqueue(original, AssessmentAxes.Rating,
            [original.FilePath], XmpSidecarNaming.FullName));
        await writer.DrainAsync();

        Assert.Equal("5", ReadRating(original.FilePath + ".xmp"));
        var current = Assert.Single(await catalog.LoadAssessmentSnapshotsAsync(
            [original.ImageId]));
        Assert.Equal(2, current.Revision);
        Assert.Equal(AssessmentAxes.None, current.PendingAxes);
        Assert.True(promotions >= 2);
    }

    [Fact]
    public async Task StopLeavesQueuedWriteMaskPending()
    {
        using var catalog = await CreateCatalogAsync();
        var active = await PendingRatingAsync(catalog, "active.cr3", 2);
        var queued = await PendingRatingAsync(catalog, "queued.cr3", 4);
        var entered = NewSignal();
        var release = NewSignal();
        await using var writer = new XmpSidecarWriter(
            catalog, ColorLabelNames.Defaults, capacity: 2);
        writer.BeforePromotionAsync = async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        writer.Start();
        Assert.True(writer.TryEnqueue(active, AssessmentAxes.Rating,
            [active.FilePath, queued.FilePath], XmpSidecarNaming.FullName));
        await entered.Task.WaitAsync(TestWaits.Condition);
        Assert.True(writer.TryEnqueue(queued, AssessmentAxes.Rating,
            [active.FilePath, queued.FilePath], XmpSidecarNaming.FullName));

        await writer.StopAsync();

        var current = Assert.Single(await catalog.LoadAssessmentSnapshotsAsync(
            [queued.ImageId]));
        Assert.Equal(AssessmentAxes.Rating, current.PendingAxes);
        Assert.False(File.Exists(queued.FilePath + ".xmp"));
    }

    [Fact]
    public async Task ReaderChecksSidecarWithoutProbingOriginal()
    {
        var original = Path.Combine(_root.Path, "online.cr3");
        var sidecar = original + ".xmp";
        WriteRating(sidecar, 4);
        var info = new FileInfo(sidecar);
        var availability = new RecordingAvailability(sidecar);
        var reader = new XmpSidecarReader(availability);

        var facts = await reader.ReadAsync(
            new XmpSidecarCandidate(
                sidecar, info.LastWriteTimeUtc, info.Length, true),
            ColorLabelNames.Defaults);

        Assert.Equal(4, facts!.Rating.Value);
        Assert.Equal([sidecar], availability.Probed);
        Assert.False(File.Exists(original));
    }

    [Fact]
    public async Task FreshSidecar_WritesCompleteRejectedTupleOnRatingChange()
    {
        using var catalog = await CreateCatalogAsync();
        var path = Path.Combine(_root.Path, "rejected.cr3");
        var id = await catalog.GetOrCreateImageAsync(path);
        await catalog.MutateAssessmentsAsync(
            [new AssessmentMutation(
                id, AssessmentAxes.Flag, Flag: ImageFlag.Rejected)],
            AssessmentAxes.None);
        var snapshot = Assert.Single(await catalog.MutateAssessmentsAsync(
            [new AssessmentMutation(id, AssessmentAxes.Rating, Rating: 4)],
            AssessmentAxes.Rating));
        await using var writer = new XmpSidecarWriter(
            catalog, ColorLabelNames.Defaults);
        writer.Start();

        Assert.True(writer.TryEnqueue(snapshot, AssessmentAxes.Rating,
            [path], XmpSidecarNaming.FullName));
        await writer.DrainAsync();

        var document = System.Xml.Linq.XDocument.Load(path + ".xmp");
        var facts = XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults);
        Assert.Equal("4", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.Xmp, "Rating"));
        Assert.Equal("-1", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.XmpDynamicMedia, "pick"));
        Assert.Equal("False", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.XmpDynamicMedia, "good"));
        Assert.Equal(string.Empty, XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.Xmp, "Label"));
        var serialized = document.ToString(
            System.Xml.Linq.SaveOptions.DisableFormatting);
        Assert.Contains("xmlns:xmpDM=", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("happyphoton", serialized,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, facts.Rating.Value);
        Assert.Equal(ImageFlag.Rejected, facts.Flag.Value);
        Assert.Equal(XmpFactKind.Empty, facts.Label.Kind);
        Assert.Equal(AssessmentAxes.None,
            Assert.Single(await catalog.LoadAssessmentSnapshotsAsync([id])).PendingAxes);
    }

    [Fact]
    public async Task ExistingLightroomReject_RatingChangeKeepsRejectAndTrueStars()
    {
        using var catalog = await CreateCatalogAsync();
        var path = Path.Combine(_root.Path, "lightroom-rejected.cr3");
        var sidecar = path + ".xmp";
        var document = XmpSidecarDocument.Create();
        var description = document.Descendants(
            XmpSidecarDocument.Rdf + "Description").Single();
        description.SetAttributeValue(XmpSidecarDocument.Xmp + "Rating", "3");
        description.SetAttributeValue(
            XmpSidecarDocument.XmpDynamicMedia + "pick", "-1");
        description.SetAttributeValue(
            XmpSidecarDocument.XmpDynamicMedia + "good", "False");
        document.Save(sidecar);
        var id = await catalog.GetOrCreateImageAsync(path);
        await catalog.MutateAssessmentsAsync(
            [new AssessmentMutation(
                id, AssessmentAxes.Flag | AssessmentAxes.Rating,
                Flag: ImageFlag.Rejected, Rating: 3)],
            AssessmentAxes.None);
        var snapshot = Assert.Single(await catalog.MutateAssessmentsAsync(
            [new AssessmentMutation(id, AssessmentAxes.Rating, Rating: 4)],
            AssessmentAxes.Rating));
        await using var writer = new XmpSidecarWriter(
            catalog, ColorLabelNames.Defaults);
        writer.Start();

        Assert.True(writer.TryEnqueue(snapshot, AssessmentAxes.Rating,
            [path], XmpSidecarNaming.FullName));
        await writer.DrainAsync();

        document = System.Xml.Linq.XDocument.Load(sidecar);
        Assert.Equal("4", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.Xmp, "Rating"));
        Assert.Equal("-1", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.XmpDynamicMedia, "pick"));
        Assert.Equal("False", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.XmpDynamicMedia, "good"));
        var facts = XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults);
        Assert.Equal(4, facts.Rating.Value);
        Assert.Equal(ImageFlag.Rejected, facts.Flag.Value);
    }

    [Fact]
    public async Task ForeignLabelReplacement_ReportsOnceAfterPromotionRetry()
    {
        using var catalog = await CreateCatalogAsync();
        var path = Path.Combine(_root.Path, "label.cr3");
        var sidecar = path + ".xmp";
        WriteLabel(sidecar, "Foreign");
        var id = await catalog.GetOrCreateImageAsync(path);
        var snapshot = Assert.Single(await catalog.MutateAssessmentsAsync(
            [new AssessmentMutation(
                id, AssessmentAxes.Label, ColorLabel: ColorLabel.Blue)],
            AssessmentAxes.Label));
        var promotions = 0;
        var reports = new List<string>();
        await using var writer = new XmpSidecarWriter(
            catalog, ColorLabelNames.Defaults);
        writer.Report = reports.Add;
        writer.BeforePromotionAsync = (_, _) =>
        {
            if (Interlocked.Increment(ref promotions) == 1)
                WriteLabel(sidecar, "A longer foreign label");
            return Task.CompletedTask;
        };
        writer.Start();

        Assert.True(writer.TryEnqueue(snapshot, AssessmentAxes.Label,
            [path], XmpSidecarNaming.FullName));
        await writer.DrainAsync();

        var replacement = Assert.Single(reports, report =>
            report.Contains("Unsupported XMP label replaced", StringComparison.Ordinal));
        Assert.Contains(path, replacement, StringComparison.Ordinal);
        Assert.Equal("Blue", XmpSidecarDocument.ReadValue(
            System.Xml.Linq.XDocument.Load(sidecar),
            XmpSidecarDocument.Xmp, "Label"));
        Assert.True(promotions >= 2);
    }

    [Fact]
    public async Task OversizedSidecar_IsUntouchedAndLeavesWritePending()
    {
        using var catalog = await CreateCatalogAsync();
        var snapshot = await PendingRatingAsync(catalog, "large.cr3", 5);
        var sidecar = snapshot.FilePath + ".xmp";
        var original = new byte[XmpSidecarReader.MaximumSidecarBytes + 1];
        Array.Fill(original, (byte)'x');
        File.WriteAllBytes(sidecar, original);
        var reports = new List<string>();
        await using var writer = new XmpSidecarWriter(
            catalog, ColorLabelNames.Defaults);
        writer.Report = reports.Add;
        writer.Start();

        Assert.True(writer.TryEnqueue(snapshot, AssessmentAxes.Rating,
            [snapshot.FilePath], XmpSidecarNaming.FullName));
        await writer.DrainAsync();

        Assert.Equal(original, File.ReadAllBytes(sidecar));
        Assert.Empty(Directory.GetFiles(
            _root.Path, $".{Path.GetFileName(sidecar)}.*.tmp"));
        Assert.Equal(AssessmentAxes.Rating,
            Assert.Single(await catalog.LoadAssessmentSnapshotsAsync(
                [snapshot.ImageId])).PendingAxes);
        var report = Assert.Single(reports);
        Assert.Contains("4 MiB", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoundingReadStream_RejectsContentPastTheSizeLimit()
    {
        var content = new byte[checked((int)XmpSidecarReader.MaximumSidecarBytes + 1)];
        await using var bounded = new XmpSidecarReadStream(
            new MemoryStream(content), "growing.xmp",
            XmpSidecarReader.MaximumSidecarBytes);

        var exception = await Assert.ThrowsAsync<XmpSidecarTooLargeException>(
            () => bounded.CopyToAsync(Stream.Null));

        Assert.Contains("4 MiB", exception.Message, StringComparison.Ordinal);
    }

    private async Task<AssessmentSnapshot> PendingRatingAsync(
        CatalogService catalog,
        string name,
        int rating)
    {
        var path = Path.Combine(_root.Path, name);
        var id = await catalog.GetOrCreateImageAsync(path);
        return Assert.Single(await catalog.MutateAssessmentsAsync(
            [new AssessmentMutation(id, AssessmentAxes.Rating, Rating: rating)],
            AssessmentAxes.Rating));
    }

    private async Task<CatalogService> CreateCatalogAsync()
    {
        var catalog = new CatalogService(Path.Combine(
            _root.Path, $"catalog-{Guid.NewGuid():N}"));
        await catalog.InitializeAsync();
        return catalog;
    }

    private static void WriteRating(string path, int rating)
    {
        var document = XmpSidecarDocument.Create();
        document.Root!.Descendants(XmpSidecarDocument.Rdf + "Description")
            .Single().SetAttributeValue(
                XmpSidecarDocument.Xmp + "Rating", rating.ToString());
        document.Save(path);
    }

    private static void WriteLabel(string path, string label)
    {
        var document = XmpSidecarDocument.Create();
        document.Root!.Descendants(XmpSidecarDocument.Rdf + "Description")
            .Single().SetAttributeValue(
                XmpSidecarDocument.Xmp + "Label", label);
        document.Save(path);
    }

    private static string? ReadRating(string path) =>
        XmpSidecarDocument.ReadValue(
            System.Xml.Linq.XDocument.Load(path),
            XmpSidecarDocument.Xmp, "Rating");

    private static TaskCompletionSource NewSignal() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose() => _root.Dispose();

    private sealed class RecordingAvailability(string expectedPath)
        : ISourceAvailabilityService
    {
        public List<string> Probed { get; } = [];

        public SourceAvailability GetAvailability(string path)
        {
            Probed.Add(path);
            return string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase)
                ? SourceAvailability.AvailableLocally
                : SourceAvailability.RequiresHydration;
        }
    }
}
