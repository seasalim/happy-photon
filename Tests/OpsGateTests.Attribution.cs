using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class OpsGateTests
{
    [Fact]
    public void CostAttribution()
    {
        OptIn(); var loader = Loader(); var file = LocalFile();
        using var pair = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, CancellationToken.None).Pair;
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(pair); Assert.NotNull(full);
        foreach (var basis in new[] { pair.Interactive, full })
        {
            var wb = RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, new());
            OpsDehaze? haze = null;
            var buildMs = Time(() => haze = OpsDehaze.Build(basis.Pixels, wb, Workers));
            using var image = new MagickImage(basis.Pixels);
            using var pixels = image.GetPixels();
            ushort[] rgb = [];
            var readMs = Time(() => rgb = pixels.GetArea(0, 0, image.Width, image.Height)!);
            var writeMs = Time(() => pixels.SetArea(0, 0, image.Width, image.Height, rgb));
            var w = (int)image.Width; var h = (int)image.Height;
            var layout = RenderKernelSupport.GetLayout(pixels);
            var sums = new double[h];
            // Separate report-only scans, no clocks inside pixels. WB, traversal and
            // checksum are common to all three; no tone, Q16 clamp or image writeback.
            double Scan(double amount, bool refine, bool correct) => Time(() =>
            {
                Parallel.For(0, h, new ParallelOptions { MaxDegreeOfParallelism = Workers }, y =>
                {
                    double sum = 0;
                    for (var x = 0; x < w; x++)
                    {
                        var i = (y * w + x) * layout.Channels;
                        var r = rgb[i + layout.Red] / 65535d;
                        var g = rgb[i + layout.Green] / 65535d;
                        var b = rgb[i + layout.Blue] / 65535d;
                        var cr = wb[0, 0] * r + wb[0, 1] * g + wb[0, 2] * b;
                        var cg = wb[1, 0] * r + wb[1, 1] * g + wb[1, 2] * b;
                        var cb = wb[2, 0] * r + wb[2, 1] * g + wb[2, 2] * b;
                        if (correct) haze!.Correct(ref cr, ref cg, ref cb, (x + .5) / w, (y + .5) / h, amount, refine);
                        sum += cr + cg + cb;
                    }
                    sums[y] = sum;
                });
            });
            Scan(50, false, false); Scan(50, false, true); Scan(50, true, true);
            foreach (var arm in OpsArm.All)
            {
                var scanMs = arm.Dehaze == 0 ? 0 : Scan(arm.Dehaze, false, false);
                var latticeMs = arm.Dehaze == 0 ? 0 : Scan(arm.Dehaze, false, true);
                var refinedMs = arm.Dehaze == 0 ? 0 : Scan(arm.Dehaze, true, true);
                var presence = arm.Texture != 0 || arm.Clarity != 0;
                var fused = arm.Dehaze != 0;
                var cached = ReferenceEquals(basis, pair.Interactive);
                var reads = (fused ? 1 : 0) + (presence ? 1 : 0);
                var writes = reads;
                Assert.All(sums, value => Assert.True(double.IsFinite(value)));
                // The uncached build includes one additional GetArea. Its time is
                // already inside buildMs: expose it but never add/subtract twice.
                Report("cost-attribution", new { arm = arm.Name, size = Size(basis), gate = cached ? "G3" : "G2",
                    reportOnly = true, latticeCached = cached, latticeBuildMs = fused ? buildMs : 0,
                    latticeBuildChargedToGate = fused && !cached,
                    buildMaterializationEstimateMs = fused ? readMs : 0,
                    renderMaterializations = reads, renderWritebacks = writes,
                    renderCopyEstimateMs = reads * readMs + writes * writeMs,
                    // The fused RGB pair replaces the production tone pair; it
                    // cannot honestly be counted as entirely removable overhead.
                    replacesProductionTonePairs = fused ? 1 : 0,
                    additionalRgbMaterializations = (presence ? 1 : 0) + (fused && !cached ? 1 : 0),
                    materializationBytes = (long)rgb.Length * sizeof(ushort), readMs, writeMs,
                    traversalMs = scanMs, latticeCorrectionScanMs = latticeMs, refinedCorrectionScanMs = refinedMs,
                    correctionMs = latticeMs - scanMs, refinementMs = Refine ? refinedMs - latticeMs : 0,
                    checksum = sums.Sum(),
                    method = "isolated report-only scans and copy estimates; non-additive; no subtraction from gate total" });
            }
        }
    }
}
