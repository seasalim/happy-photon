using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class HealGateTests
{
    [Fact]
    public async Task G2ExportParity()
    {
        OptIn(); var file = LocalFile(Raw); using var folder = new TemporaryDirectory();
        var pipeline = new RenderPipeline(); var prototype = new HealPrototype();
        foreach (var repairs in new[] { false, true })
        {
            file.EditSettings = repairs ? HealWorkloads.LH8() : new();
            var calls = 0;
            var service = new ImageExportService(pipeline, Loader(), new ExportMetadataService(),
                new DcpProfileService(new SourceAvailabilityService()), request =>
                {
                    calls++;
                    using var repaired = repairs ? prototype.Repair(request.Base, HealWorkloads.S64(), Candidate) : null;
                    var sameBase = request with { Base = repaired ?? request.Base };
                    Assert.True(sameBase.Settings.Detail.ResolveCaptureSharpen(true) > 0);
                    using var refinement = pipeline.RenderResting(sameBase,
                        RenderExecutionOptions.Resting(CancellationToken.None, 2));
                    var upstream = pipeline.RenderDisplayRec2020(sameBase);
                    try
                    {
                        using var canonical = RenderFinalizer.Finalize(upstream, null, OutputColorSpace.Srgb,
                            OutputSharpeningMode.Off, false, effects: request.Settings.Effects);
                        var a = RenderPipelineTestSupport.ReadPixels(refinement.Image);
                        var b = RenderPipelineTestSupport.ReadPixels(canonical);
                        Assert.Equal(a.Length, b.Length);
                        var differing = a.Zip(b).LongCount(pair => pair.First != pair.Second);
                        Report("G2", new { repairs, size = Size(sameBase.Base), codes = a.Length, differing,
                            captureSharpen = sameBase.Settings.Detail.ResolveCaptureSharpen(true), workers = 2, threshold = 0 });
                        Assert.Equal(0, differing);
                        return upstream;
                    }
                    catch { upstream.Dispose(); throw; }
                });
            var result = await service.ExportBatchAsync([file], new ExportSettings
            {
                OutputFolder = folder.Path, NamingPattern = repairs ? "heal" : "control", Format = ExportFormat.Jpeg,
                ExportHiRes = true, ExportWeb = false, ExportSmall = false,
                OutputColorSpace = OutputColorSpace.Srgb, OutputSharpening = OutputSharpeningMode.Off
            });
            Assert.True(result.ExportedCount == 1, string.Join(';', result.FailedTargets.Select(t => t.FailureReason)));
            Assert.Equal(1, calls);
        }
    }

    [Fact]
    public void Registration()
    {
        OptIn();
        var allPass = true;
        // Canon pins the gated frame; Fuji has a real embedded optics prescription.
        foreach (var fixture in new[] { Raw, "fujifilm-x30.raf" })
        foreach (var optics in new[] { false, true })
        {
            var decode = new BaseDecodeSettings(HlReconstructionMode.Clip, optics, optics, optics);
            var file = LocalFile(fixture); var loader = Loader();
            using var pair = loader.LoadPreviewBaseWithOutcome(file, decode, CancellationToken.None).Pair;
            using var full = loader.LoadFullBase(file, decode, CancellationToken.None);
            Assert.NotNull(pair); Assert.NotNull(pair.Large); Assert.NotNull(full);
            foreach (var basis in new[] { pair.Interactive, pair.Large })
            {
                using var reference = new MagickImage(full.Pixels);
                reference.Resize(new MagickGeometry(basis.Pixels.Width, basis.Pixels.Height) { IgnoreAspectRatio = true });
                using var preview = new MagickImage(basis.Pixels);
                reference.GaussianBlur(0, 1); preview.GaussianBlur(0, 1);
                var a = RenderPipelineTestSupport.ReadPixels(reference); var b = RenderPipelineTestSupport.ReadPixels(preview);
                var w = (int)preview.Width; var h = (int)preview.Height;
                var scaleX = full.Pixels.Width / (double)w; var scaleY = full.Pixels.Height / (double)h;
                var offsets = new List<(double X, double Y)>();
                foreach (var v in new[] { .2, .5, .8 }) foreach (var u in new[] { .2, .5, .8 })
                {
                    double best = double.PositiveInfinity, bestX = 0, bestY = 0;
                    for (var iy = -16; iy <= 16; iy++) for (var ix = -16; ix <= 16; ix++)
                        Consider(ix * .25, iy * .25);
                    var coarseX = bestX; var coarseY = bestY;
                    for (var iy = -10; iy <= 10; iy++) for (var ix = -10; ix <= 10; ix++)
                        Consider(coarseX + ix * .025, coarseY + iy * .025);
                    void Consider(double x, double y)
                    {
                        var error = RegistrationError(a, b, w, (int)(u * w), (int)(v * h), x / scaleX, y / scaleY);
                        if (error < best) { best = error; bestX = x; bestY = y; }
                    }
                    offsets.Add((bestX, bestY));
                }
                var max = offsets.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
                var threshold = fixture == "fujifilm-x30.raf" ? .75 : .5;
                var pass = max <= threshold; allPass &= pass;
                // Diagnostic correction to hand back to the owner, not a production decode fix.
                var correctionX = fixture == "fujifilm-x30.raf" ? .25 : 0;
                var correctedMax = offsets.Max(p => Math.Sqrt((p.X - correctionX) * (p.X - correctionX) + p.Y * p.Y));
                Report("registration", new { source = fixture, optics, prescription = full.Info.LensPrescriptionSummary?.Source,
                    activeOptics = optics && full.Info.LensPrescriptionSummary?.HasAny == true,
                    size = Size(basis), full = Size(full), method = "9-patch NCC, green, sigma1, 0.25px search +/-4, refined to 0.025 full px",
                    offsets = offsets.Select(p => new { p.X, p.Y }), maxFullPixelError = max, threshold, pass,
                    suggestedFullPixelCorrectionX = correctionX, correctedMax,
                    medianOffsetX = Median(offsets.Select(p => p.X)), medianOffsetY = Median(offsets.Select(p => p.Y)) });
            }
        }
        Assert.True(allPass, "Registration exceeds the owner-approved Bayer 0.5 / X-Trans 0.75 full-pixel bound");
    }
    private static double RegistrationError(ushort[] a, ushort[] b, int w, int cx, int cy, double dx, double dy)
    {
        double aa = 0, bb = 0, ab = 0, sumA = 0, sumB = 0; var count = 0;
        for (var y = cy - 24; y <= cy + 24; y += 2)
        for (var x = cx - 24; x <= cx + 24; x += 2)
        {
            var left = (int)Math.Floor(x + dx); var top = (int)Math.Floor(y + dy);
            var fx = x + dx - left; var fy = y + dy - top;
            var va = ((1 - fx) * a[(top * w + left) * 3 + 1] + fx * a[(top * w + left + 1) * 3 + 1]) * (1 - fy) +
                ((1 - fx) * a[((top + 1) * w + left) * 3 + 1] + fx * a[((top + 1) * w + left + 1) * 3 + 1]) * fy;
            var vb = b[(y * w + x) * 3 + 1];
            aa += va * va; bb += vb * (double)vb; ab += va * vb; sumA += va; sumB += vb; count++;
        }
        return 1 - (ab - sumA * sumB / count) / Math.Sqrt(
            Math.Max(1, aa - sumA * sumA / count) * Math.Max(1, bb - sumB * sumB / count));
    }

}
