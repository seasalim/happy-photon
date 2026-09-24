using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WatermarkExportTests
{
    [Theory]
    [InlineData(OutputColorSpace.Srgb, ExportFormat.Png)]
    [InlineData(OutputColorSpace.DisplayP3, ExportFormat.Png)]
    [InlineData(OutputColorSpace.Srgb, ExportFormat.Tiff)]
    [InlineData(OutputColorSpace.DisplayP3, ExportFormat.Tiff)]
    public async Task MultiSizeFilesApplyFrozenMarkOncePerSize(OutputColorSpace color, ExportFormat format)
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "source.jpg");
        using (var fixture = new MagickImage("gradient:#182a48-#edce91",
                   new MagickReadSettings { Width = 600, Height = 400 }))
            fixture.Write(source, MagickFormat.Jpeg);
        var originalBytes = File.ReadAllBytes(source);
        var file = new ImageFile(source);
        var settings = new ExportSettings
        {
            OutputFolder = Path.Combine(root.Path, "finished"), Format = format,
            OutputColorSpace = color, ExportWeb = true, ExportSmall = true,
            WebMaxSize = 300, SmallMaxSize = 150
        };
        settings.Watermark.Restore(new WatermarkSpec("© Jane Doe", Size: 10,
            Edge: WatermarkEdge.Right), enabled: true);
        var job = settings.CreateJob([file]);
        settings.Watermark.Text = "Changed after snapshot";
        settings.Watermark.Enabled = false;
        var loader = new StandardBaseLoader();
        var pipeline = new RenderPipeline();
        var service = new ImageExportService(pipeline, loader, new ExportMetadataService());
        var result = await service.ExportBatchAsync(job);
        Assert.Equal(3, result.SuccessfulTargetCount);
        Assert.Equal(originalBytes, File.ReadAllBytes(source));
        using var sourceBase = loader.LoadFullBase(file, BaseDecodeSettings.From(file.EditSettings), CancellationToken.None);
        Assert.NotNull(sourceBase);
        using var upstream = pipeline.RenderDisplayRec2020(new RenderRequest(sourceBase,
            file.EditSettings, RenderIntent.Export, null, new RenderOptions(false, false)));
        foreach (var target in job.Targets)
        {
            if (target.Recipe.MaxDimension is { } cap) RenderColorEncoding.ResizeInLinearLight(upstream, cap);
            using var plain = RenderFinalizer.Finalize(upstream, null, color,
                OutputSharpeningMode.Screen, target.Recipe.MaxDimension.HasValue);
            using var expected = RenderFinalizer.Finalize(upstream, null, color,
                OutputSharpeningMode.Screen, target.Recipe.MaxDimension.HasValue, watermark: job.Output.Watermark);
            using var proofMark = new MagickImage(plain);
            WatermarkRenderer.Apply(proofMark, job.Output.Watermark!);
            Assert.Equal(Rgb(expected), Rgb(proofMark));
            using var actual = new MagickImage(target.ResolvedPath);
            Assert.Equal(upstream.Width, actual.Width);
            Assert.Equal(upstream.Height, actual.Height);
            Assert.Equal(format == ExportFormat.Tiff ? 16U : 8U, actual.Depth);
            var tolerance = format == ExportFormat.Tiff ? 0 : 129;
            Assert.All(Rgb(expected).Zip(Rgb(actual)), pair =>
                Assert.InRange(Math.Abs(pair.First - pair.Second), 0, tolerance));
            Assert.NotEqual(Rgb(plain), Rgb(actual));
        }
    }

    private static ushort[] Rgb(MagickImage image)
    {
        using var pixels = image.GetPixelsUnsafe();
        return pixels.ToShortArray(PixelMapping.RGB)!;
    }
}
