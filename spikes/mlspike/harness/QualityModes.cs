using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using HappyPhoton.MlSpike;
using ImageMagick;

namespace MlSpike.Harness;

internal static class QualityModes
{
    internal static object Quality(RunContext context)
    {
        var samples = context.Samples.Where(s => context.Config.Capability == "subject"
            ? s.Category.StartsWith("subject-") : s.Category is "sky" or "sky-negative").ToArray();
        if (samples.Length != (context.Config.Capability == "subject" ? 24 : 20))
            throw new InvalidDataException("Labeled workload count differs.");
        using var session = context.Session();
        var results = new List<object>();
        var positiveIoUs = new List<double>();
        var negatives = new List<double>();
        foreach (var sample in samples)
        {
            var input = context.Load(sample);
            var start = Stopwatch.GetTimestamp();
            var mask = session.Infer(input);
            var seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
            SaveMask(context, sample, input, mask);
            var truth = MaskFiles.Read(LocalFiles.Resolve(context.SampleRoot, sample.MaskFile!));
            var resized = MaskMath.ResizeLabels(truth.Pixels, truth.Width, truth.Height, input.Width, input.Height);
            var iou = MaskMath.IoU(mask, resized);
            var area = mask.Count(v => v != 0) / (double)mask.Length;
            if (sample.Category == "sky-negative") negatives.Add(area);
            else positiveIoUs.Add(iou);
            results.Add(new { sample.Id, sample.Category, input.Width, input.Height, Seconds = seconds, IoU = iou, Area = area });
        }
        return new
        {
            Images = results, MeanIoU = positiveIoUs.Average(),
            Below070 = positiveIoUs.Count(v => v < 0.70), NegativeAreas = negatives,
            TimingBoundary = "BGRA8 -> preprocess -> CPU inference -> resized binary mask; excludes decode and disk"
        };
    }

    internal static object Repeat(RunContext context)
    {
        var selected = context.Required("repeat-ids").Split(',');
        if (selected.Length != 6 || selected.Distinct().Count() != 6)
            throw new ArgumentException("--repeat-ids must pin six distinct edge IDs.");
        var samples = selected.Select(id => context.Edges().Single(s => s.Id == id)).ToArray();
        using var session = context.Session();
        return samples.Select(sample =>
        {
            var image = context.Load(sample);
            var first = session.Infer(image);
            var second = session.Infer(image);
            var third = session.Infer(image);
            SaveMask(context, sample, image, first);
            return new { sample.Id, DifferenceRun2 = MaskMath.Difference(first, second),
                DifferenceRun3 = MaskMath.Difference(first, third) };
        }).ToArray();
    }

    internal static object ContactSheet(RunContext context)
    {
        var cropPath = context.Required("crops");
        var crops = JsonSerializer.Deserialize<Dictionary<string, int[]>>(LocalFiles.Read(cropPath))
            ?? throw new InvalidDataException("Missing hardest-edge crops.");
        string? desktopRoot = null;
        var desktopHashes = new Dictionary<string, string>();
        if (context.Args.TryGetValue("desktop-result", out var desktop))
        {
            using var record = JsonDocument.Parse(LocalFiles.Read(desktop));
            var identity = record.RootElement.GetProperty("identity");
            if (identity.GetProperty("manifest_sha256").GetString() != RunContext.FrozenManifest ||
                identity.GetProperty("model_sha256").GetString() != context.Config.ModelSha256.ToLowerInvariant() ||
                identity.GetProperty("config_sha256").GetString() != LocalFiles.Hash(context.Required("config")) ||
                record.RootElement.GetProperty("status").GetString() != "measured" ||
                record.RootElement.GetProperty("mode").GetString() != "contact-sheet")
                throw new InvalidDataException("Desktop masks have different provenance.");
            foreach (var image in record.RootElement.GetProperty("result").GetProperty("images").EnumerateArray())
                desktopHashes.Add(image.GetProperty("id").GetString()!, image.GetProperty("mask_sha256").GetString()!);
            desktopRoot = Path.GetDirectoryName(Path.GetFullPath(desktop));
        }
        using var session = context.Session();
        var html = new StringBuilder("<!doctype html><meta charset=\"utf-8\"><title>MLSPIKE edge ratings</title>");
        html.Append("<h1>Frozen edge set</h1><p>Rate each applicable mask: usable / light brushing / unusable.</p>");
        var results = new List<object>();
        var ratingRows = new StringBuilder("id,capability,rating,notes\n");
        foreach (var sample in context.Edges())
        {
            var input = context.Load(sample);
            var mask = session.Infer(input);
            SaveMask(context, sample, input, mask);
            var applicable = context.Config.Capability == "sky" ? sample.Category == "landscape" : sample.Category != "landscape";
            double? difference = null;
            if (desktopRoot != null)
            {
                var ratedPath = LocalFiles.Resolve(desktopRoot, sample.Id + ".png");
                if (!desktopHashes.TryGetValue(sample.Id, out var hash) || LocalFiles.Hash(ratedPath) != hash)
                    throw new InvalidDataException("Desktop mask bytes differ from its result record.");
                var rated = MaskFiles.Read(ratedPath);
                if (rated.Width != input.Width || rated.Height != input.Height)
                    throw new InvalidDataException("Desktop mask dimensions differ.");
                difference = MaskMath.Difference(mask, rated.Pixels);
            }
            results.Add(new { sample.Id, MaskSha256 = LocalFiles.Hash(Path.Combine(context.Output, sample.Id + ".png")),
                ApplicableToRating = applicable, DifferenceFromDesktop = difference,
                RequiresRerating = difference > 0.005 });
            if (!applicable && difference is not > 0.005) continue;
            if (!crops.TryGetValue(sample.Id, out var crop) || crop.Length != 4 ||
                crop[0] < 0 || crop[1] < 0 || crop[2] < 1 || crop[3] < 1 ||
                crop[0] + crop[2] > input.Width || crop[1] + crop[3] > input.Height)
                throw new InvalidDataException($"Pin a valid hardest-edge crop at the 1600 base for {sample.Id}.");
            var masked = (byte[])input.Bgra.Clone();
            for (var i = 0; i < mask.Length; i++)
                if (mask[i] == 0)
                    for (var c = 0; c < 3; c++) masked[i * 4 + c] = (byte)(masked[i * 4 + c] / 4);
            using var original = input.ToImage();
            using var overlay = new PreviewInput(masked, input.Width, input.Height).ToImage();
            WriteViews(original, "source");
            WriteViews(overlay, "mask");
            html.Append($"<section><h2>{WebUtility.HtmlEncode(sample.Id)}</h2>");
            foreach (var view in new[] { "fit-source", "fit-mask", "crop-source", "crop-mask" })
                html.Append($"<img src=\"{sample.Id}-{view}.png\" alt=\"{view}\">");
            html.Append("<p>Rating: __________ Notes: ____________________</p></section>");
            if (applicable) ratingRows.Append($"{sample.Id},{context.Config.Capability},,\n");

            void WriteViews(MagickImage source, string name)
            {
                using var fitted = source.Clone();
                fitted.Resize(new MagickGeometry(480, 480));
                using (var stream = LocalFiles.Create(Path.Combine(context.Output, $"{sample.Id}-fit-{name}.png")))
                    fitted.Write(stream, MagickFormat.Png);
                using var detail = source.Clone();
                detail.Crop(new MagickGeometry(crop[0], crop[1], (uint)crop[2], (uint)crop[3]));
                detail.ResetPage();
                using var croppedStream = LocalFiles.Create(Path.Combine(context.Output, $"{sample.Id}-crop-{name}.png"));
                detail.Write(croppedStream, MagickFormat.Png);
            }
        }
        using (var writer = new StreamWriter(LocalFiles.Create(Path.Combine(context.Output, "contact-sheet.html"))))
            writer.Write(html.ToString());
        using (var writer = new StreamWriter(LocalFiles.Create(Path.Combine(context.Output, "ratings.csv"))))
            writer.Write(ratingRows.ToString());
        return new { Images = results, CropsSha256 = LocalFiles.Hash(cropPath),
            DesktopResultSha256 = desktopRoot == null ? null : LocalFiles.Hash(context.Required("desktop-result")),
            OwnerRating = "pending; never inferred from numeric metrics" };
    }

    private static void SaveMask(RunContext context, Sample sample, PreviewInput image, byte[] mask) =>
        MaskFiles.Write(Path.Combine(context.Output, sample.Id + ".png"), mask, image.Width, image.Height);
}
