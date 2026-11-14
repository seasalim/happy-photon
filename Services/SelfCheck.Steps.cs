using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

// Each step asserts an observable result: several services report failure through
// defaults, null bases or failed-target lists rather than exceptions.
internal static partial class SelfCheck
{
    private const int RenderMaxDimension = 1024;

    private static EditSettings SampleEdits() => new()
    {
        Exposure = 0.5,
        HlReconstruction = HlReconstructionMode.Blend,
        Wb = new WhiteBalanceSettings { Mode = WbMode.Custom, Kelvin = 5200, Tint = 8 },
        Locals =
        [
            new LocalAdjustment
            {
                Type = "brush",
                Exposure = 0.3,
                Strokes = [new LocalBrushStroke { Points = [new(4000, 4000), new(9000, 7000)] }]
            }
        ]
    };

    private static void CheckEditSettingsJson(EditSettings edits)
    {
        var json = EditSettingsJson.Serialize(edits);
        var restored = EditSettingsJson.Deserialize(json, out var wasClamped);

        Require(!wasClamped, "sample edits were clamped");
        Require(restored.HasSameEdits(edits), "edit settings changed in a JSON round trip");
        Require(restored.Locals?[0].Strokes?[0].Points.Count == 2, "brush points were lost");
    }

    private static async Task<long> CheckCatalogAsync(CatalogService catalog, string raw, EditSettings edits)
    {
        var id = await catalog.GetOrCreateImageAsync(raw);
        await catalog.SaveEditSettingsAsync(id, edits);
        var states = await catalog.LoadImageStatesAsync([raw]);

        Require(states.TryGetValue(raw, out var rows) && rows.Count == 1, "image row did not read back");
        Require(rows![0].EditSettings.HasSameEdits(edits), "edit settings changed in the catalog");

        return id;
    }

    private static async Task CheckAppSettingsAsync(CatalogService catalog)
    {
        var service = new AppSettingsService(catalog);
        var settings = await service.LoadAsync();
        settings.BrowseThumbnailSize = BrowseThumbnailSize.Large;
        settings.AppTheme = AppTheme.MidGray;

        // The group preferences are the JSON-stored settings; a failed read returns empty.
        settings.DevelopGroups["Light"] = false;
        settings.PasteGroups["Adjustments"] = false;
        await service.SaveAsync(settings);
        var reloaded = await new AppSettingsService(catalog).LoadAsync();

        Require(reloaded.BrowseThumbnailSize == BrowseThumbnailSize.Large &&
                reloaded.AppTheme == AppTheme.MidGray, "app settings did not read back");
        Require(reloaded.DevelopGroups.GetValueOrDefault("Light", true) == false &&
                reloaded.PasteGroups.GetValueOrDefault("Adjustments", true) == false,
            "JSON-stored group settings did not read back");
    }

    private static void CheckMetadata(string path)
    {
        var metadata = MetadataService.ExtractMetadata(new ImageFile(path), new LibRawProcessingService());

        Require(!string.IsNullOrWhiteSpace(metadata.CameraMake) &&
                !string.IsNullOrWhiteSpace(metadata.CameraModel) &&
                metadata.PixelWidth > 0 && metadata.PixelHeight > 0,
            $"metadata incomplete for {Path.GetFileName(path)}");
    }

    private static BaseImage Decode(IBaseImageLoader loader, string path, EditSettings edits)
    {
        var decoded = loader.LoadFullBase(new ImageFile(path), BaseDecodeSettings.From(edits), CancellationToken.None);
        Require(decoded is { Pixels.Width: > 0, Pixels.Height: > 0 }, $"{Path.GetFileName(path)} did not decode");

        return decoded!;
    }

    private static void CheckRender(BaseImage source, EditSettings edits)
    {
        using var result = new RenderPipeline().Render(new RenderRequest(
            source, edits, RenderIntent.Export, RenderMaxDimension, new RenderOptions()));
        var longest = Math.Max(result.Image.Width, result.Image.Height);

        Require(longest is > 0 and <= RenderMaxDimension, $"render produced {result.Image.Width}x{result.Image.Height}");
    }

    // The export folder is new and under the temporary root, so no original can collide;
    // the image keeps the built-in profile, so the result does not depend on the machine.
    private static async Task CheckExportAsync(string root, string raw, EditSettings edits)
    {
        var folder = Path.Combine(root, "export");
        Directory.CreateDirectory(folder);
        var service = new ImageExportService(
            new RenderPipeline(),
            new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()),
            new ExportMetadataService());
        var result = await service.ExportBatchAsync(
            [new ImageFile(raw) { EditSettings = edits.Clone() }],
            new ExportSettings { OutputFolder = folder, Format = ExportFormat.Jpeg, Quality = 85 });
        var output = Directory.GetFiles(folder, "*.jpg");
        var failure = result.FailedTargets.FirstOrDefault()?.FailureReason;

        Require(result.ExportedCount == 1 && failure == null && output.Length == 1,
            $"export did not write one JPEG{(failure == null ? "" : $": {failure}")}");

        // A header read accepts a JPEG whose pixel data cannot decode; decode it fully.
        using var exported = new MagickImage(output[0]);
        Require(exported.Width > 0 && exported.Height > 0, "exported JPEG does not decode");
    }

    private static async Task CheckXmpAsync(CatalogService catalog, long id, string raw)
    {
        Step("xmp-write");
        const AssessmentAxes axes = AssessmentAxes.Rating | AssessmentAxes.Label;
        var snapshot = (await catalog.MutateAssessmentsAsync(
            [new AssessmentMutation(id, axes, Rating: 4, ColorLabel: ColorLabel.Green, PendingAxes: axes)])).Single();
        var reports = new List<string>();

        await using (var writer = new XmpSidecarWriter(catalog, ColorLabelNames.Defaults))
        {
            writer.Report = reports.Add;
            writer.Start();
            Require(writer.TryEnqueue(snapshot, axes, [raw], XmpSidecarNaming.FullName), "sidecar write was not queued");
            await writer.DrainAsync();
        }

        Require(reports.Count == 0, $"sidecar write reported: {string.Join("; ", reports)}");

        Step("xmp-read");
        var winner = XmpSidecarPaths.Resolve(raw, [raw], XmpSidecarNaming.FullName).Winner;
        Require(winner != null, "sidecar was not written");
        var facts = await new XmpSidecarReader().ReadAsync(winner!, ColorLabelNames.Defaults);

        Require(facts is { Rating.Value: 4, Label.Value: ColorLabel.Green }, "sidecar did not read back");
    }

    private static async Task CheckBackupAsync(CatalogService catalog)
    {
        Step("backup");
        var backup = new CatalogBackupService(catalog);
        await backup.BackupAsync();
        var archives = backup.List();

        // BackupAsync records a failure in this setting instead of throwing.
        var outcome = await catalog.GetAppSettingAsync(CatalogBackupService.OutcomeKey);
        Require(archives.Count == 1, $"backup was not written: {outcome}");

        Step("backup-verify");
        // List() returns the archive stem; the archive itself is <stem>.zip.
        CatalogBackupService.VerifyArchive(archives[0].Path + ".zip");
    }
}
