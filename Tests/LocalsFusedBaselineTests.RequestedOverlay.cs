using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;
using static HappyPhoton.Tests.LocalsRangeOracle;

namespace HappyPhoton.Tests;

public sealed partial class LocalsFusedBaselineTests
{
    [Fact]
    public void QualifiedRangeRequestedOverlay()
    {
        OptIn();
        using var loadedBase = Load(false);
        var settings = RadialSettings(loadedBase);
        var selected = settings.Locals![0];
        var window = new LightWindow(.47, 1, .1);
        // The control retains a resting preview with Show Mask off. Neither its pixels
        // nor its geometry/WB preparation are reused by the overlay request.
        using var resting = new RenderPipeline().Render(new(loadedBase, settings,
            RenderIntent.Preview, 1600, new(false, false) { PreparePreviewPixels = true }));
        var realBitmap = CanCreateRangeBitmap(out var fallback);
        using var restingSurface = UploadRangeBitmap(resting.PreviewPixels!,
            (int)resting.Image.Width, (int)resting.Image.Height, realBitmap);
        BaseImage AcquireMatchingLoadedBase() => loadedBase;
        RangeOverlaySurface? drawable = null;
        void Request()
        {
            var basis = AcquireMatchingLoadedBase();
            using var geometry = RenderGeometry.Apply(basis.Pixels, settings, out _);
            // The loaded base is linear Rec.2020: resize directly in linear light.
            if (Math.Max(geometry.Width, geometry.Height) > 1600)
                geometry.Resize(new MagickGeometry(1600, 1600) { Greater = true });
            DcpHueSatRenderer.Apply(geometry, basis.Info.DcpProfile?.HueSatMap);
            var source = RenderPipelineTestSupport.ReadPixels(geometry);
            var width = (int)geometry.Width; var height = (int)geometry.Height;
            var edge = Math.Max(width, height); var pixels = width * height;
            var wb = new DcpRenderMatrix(RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, settings));
            var tint = HappyPhotonColors.MixerMagentaColor;
            var bgra = new byte[pixels * 4];
            var workers = Math.Min(Environment.ProcessorCount, Math.Max(1, (pixels + 32767) / 32768));
            Parallel.For(0, workers, worker =>
            {
                for (var p = pixels * worker / workers; p < pixels * (worker + 1) / workers; p++)
                {
                    var weight = RadialWeight(selected, (p % width + .5) / edge,
                        (p / width + .5) / edge, width / (double)edge, height / (double)edge);
                    if (weight == 0) continue;
                    var o = p * 3;
                    var r = source[o] / 65535d; var g = source[o + 1] / 65535d; var b = source[o + 2] / 65535d;
                    var l = ClassifyLightness(wb.Row0(r, g, b), wb.Row1(r, g, b), wb.Row2(r, g, b));
                    var alpha = (byte)Math.Round(weight * window.Weight(l) * 128);
                    bgra[p * 4] = (byte)(tint.B * alpha / 255);
                    bgra[p * 4 + 1] = (byte)(tint.G * alpha / 255);
                    bgra[p * 4 + 2] = (byte)(tint.R * alpha / 255);
                    bgra[p * 4 + 3] = alpha;
                }
            });
            drawable = UploadRangeBitmap(bgra, width, height, realBitmap);
        }
        Request(); drawable!.Dispose(); drawable = null;
        var times = new double[Samples]; var memory = new double[Samples];
        var allocations = new double[Samples]; var controls = new double[Samples];
        for (var i = 0; i < Samples; i++)
        {
            var control = Measure(() => GC.KeepAlive(restingSurface));
            try
            {
                var result = Measure(Request);
                times[i] = result.Ms; memory[i] = result.Peak - control.Peak;
                controls[i] = control.Peak; allocations[i] = result.Allocated;
                Assert.NotNull(drawable);
            }
            finally { drawable?.Dispose(); drawable = null; }
        }
        Print(_radialCoverage, $"requested_overlay end_to_end=True samples={Samples} " +
            $"size={resting.Image.Width}x{resting.Image.Height} base={loadedBase.Pixels.Width}x{loadedBase.Pixels.Height} " +
            $"median_ms={Median(times):F4} private_increment_bytes={Median(memory):F0} " +
            $"caller_allocation={Median(allocations):F0} upload={(realBitmap ? "WriteableBitmap" : "pinned-buffer-copy")} " +
            $"fallback={fallback} dcp_map={loadedBase.Info.DcpProfile?.HueSatMap != null} " +
            $"selected=R1 angle={selected.Angle} outside={selected.Outside} L=.47..1 softness=.1 " +
            $"times=[{string.Join(',', times)}] private=[{string.Join(',', memory)}] " +
            $"control_private=[{string.Join(',', controls)}] caller_alloc=[{string.Join(',', allocations)}]");
        GC.KeepAlive(restingSurface);
    }

    private static bool CanCreateRangeBitmap(out string reason)
    {
        try
        {
            using var probe = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var frame = probe.Lock();
            reason = "none"; return true;
        }
        catch (InvalidOperationException ex)
        {
            reason = ex.Message.Replace(' ', '_'); return false;
        }
    }

    private static RangeOverlaySurface UploadRangeBitmap(byte[] pixels, int width, int height, bool real)
    {
        if (!real)
        {
            var pinned = GC.AllocateArray<byte>(pixels.Length, pinned: true);
            Buffer.BlockCopy(pixels, 0, pinned, 0, pixels.Length);
            return new(null, pinned);
        }
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        try
        {
            using var frame = bitmap.Lock();
            for (var y = 0; y < height; y++)
                Marshal.Copy(pixels, y * width * 4, frame.Address + y * frame.RowBytes, width * 4);
            return new(bitmap, null);
        }
        catch { bitmap.Dispose(); throw; }
    }

    private sealed class RangeOverlaySurface(WriteableBitmap? bitmap, byte[]? pinned) : IDisposable
    {
        public void Dispose() { bitmap?.Dispose(); GC.KeepAlive(pinned); }
    }

    [Fact]
    public void RangeLightnessMatchesExactOracle()
    {
        foreach (var r in new[] { -.2, 0, .18, 1, 2 })
        foreach (var g in new[] { -.1, 0, .3, 1, 4 })
        foreach (var b in new[] { -.3, 0, .5, 1, 3 })
            Assert.Equal(Classify(r, g, b).L, ClassifyLightness(r, g, b));
    }
}
