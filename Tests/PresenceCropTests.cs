using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PresenceCropTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(768, 512, false)]
    [InlineData(779, 521, false)]
    [InlineData(779, 521, true)]
    [InlineData(192, 128, false)]
    public void CropMatchesUncroppedInterior(int width, int height, bool raw)
    {
        using var basis = CreateScene(width, height, raw);
        var settings = new EditSettings { Clarity = 100, Detail = new() { CaptureSharpen = 0 } };
        var pipeline = new RenderPipeline();
        using var full = pipeline.RenderDisplayRec2020(new(basis, settings, RenderIntent.Export, null, new(false)));
        settings.Crop = new() { Left = .173, Top = .191, Right = .827, Bottom = .859 };
        var (left, top, cropWidth, cropHeight) = settings.Crop.ToPixels(width, height);
        using var cropped = pipeline.RenderDisplayRec2020(new(basis, settings, RenderIntent.Export, null, new(false)));
        var expected = RenderPipelineTestSupport.ReadPixels(full);
        var actual = RenderPipelineTestSupport.ReadPixels(cropped);
        // Two radius-five grid blurs plus interpolation: exclude twelve base-grid cells.
        var margin = (int)Math.Ceiling(12 * Math.Max(1, Math.Max(width, height) / 256d));
        var maxError = 0;

        for (var y = margin; y < cropHeight - margin; y++)
        for (var x = margin; x < cropWidth - margin; x++)
        for (var c = 0; c < 3; c++)
        {
            maxError = Math.Max(maxError, Math.Abs(actual[(y * cropWidth + x) * 3 + c] -
                expected[((y + top) * width + x + left) * 3 + c]));
        }

        output.WriteLine($"crop {width}x{height} raw={raw}: max Q16 error={maxError}, margin={margin}");
        Assert.Equal(0, maxError);
    }

    [Theory]
    [InlineData(47, 31, 0, 100, "32F9FA97AF301232D12B763D925359B23639AEE5EC72C48B8F89875D1F30E10F")]
    [InlineData(389, 259, 0, -100, "9D2D34E14DD81D6D6E8B6F1D243D68071A63F0BF45C09BE525A21DE045E650F8")]
    [InlineData(779, 521, 40, 40, "FE945CB87C515DCF10AB48B84B6D34FC23BF11F99BB32B4A3A66599D0CF7B58C")]
    public void UncroppedCodesStayFrozen(int width, int height, int texture, int clarity, string expectedHash)
    {
        using var basis = CreateScene(width, height, false);
        using var actual = new RenderPipeline().RenderDisplayRec2020(new(basis,
            new() { Texture = texture, Clarity = clarity, Detail = new() { CaptureSharpen = 0 } },
            RenderIntent.Export, null, new(false)));
        var hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(
            RenderPipelineTestSupport.ReadPixels(actual).AsSpan())));
        output.WriteLine($"frozen {width}x{height} TX={texture} CL={clarity}: {hash}");
        Assert.Equal(expectedHash, hash);
    }

    [Theory]
    [InlineData(0, 1, 0, 0)]
    [InlineData(90, 1, 0, 0)]
    [InlineData(270, 2, 0, 0)]
    [InlineData(0, 1, 100, 0)]
    [InlineData(90, 1, 0, 12)]
    [InlineData(270, 2, 100, 12)]
    public void PreparedRestingFramePreservesExtentAndPhase(
        int rotation, int preparationScale, int vertical, double straighten)
    {
        using var basis = CreateScene(768, 512, false);
        var settings = new EditSettings
        {
            Clarity = 100, Rotation = rotation,
            Geometry = new() { Vertical = vertical }, HorizonRotation = straighten,
            Crop = new() { Left = .25, Top = .125, Right = .875, Bottom = .875 },
            Detail = new() { CaptureSharpen = 0 }
        };
        var pipeline = new RenderPipeline();
        using var expected = pipeline.Render(new(basis, settings, RenderIntent.Export, null, new(false)));
        using var prepared = new BaseImage(RenderGeometry.Apply(basis.Pixels, settings, out _), basis.Info);
        // A resting resize preserves normalized crop coordinates even when the frame was larger.
        var frame = RenderGeometry.CalculateLocalsFrame(768, 512, settings);
        frame = frame with
        {
            Width = frame.Width * preparationScale, Height = frame.Height * preparationScale,
            BaseLongEdge = frame.BaseLongEdge * preparationScale
        };
        var preparedSettings = settings.Clone();
        preparedSettings.Crop = null;
        preparedSettings.Rotation = 0;
        preparedSettings.Geometry = null;
        preparedSettings.HorizonRotation = 0;
        var request = new RenderRequest(prepared, preparedSettings, RenderIntent.Preview, null, new(false))
        {
            LocalsFrameOverride = frame
        };
        var codes = RenderPipelineTestSupport.ReadPixels(expected.Image);

        foreach (var workers in new[] { 1, 2 })
        {
            using var resting = pipeline.RenderResting(request, RenderExecutionOptions.Resting(default, workers));
            Assert.Equal(codes, RenderPipelineTestSupport.ReadPixels(resting.Image));
        }

        using var export = pipeline.Render(request with { Intent = RenderIntent.Export });
        Assert.Equal(codes, RenderPipelineTestSupport.ReadPixels(export.Image));
    }

    private static BaseImage CreateScene(int width, int height, bool raw)
    {
        var values = new ushort[width * height * 3];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        for (var c = 0; c < 3; c++)
        {
            var light = .3 + .08 * Math.Sin(x * .08) + .06 * Math.Cos(y * .09) +
                .04 * Math.Sin((x + y) * .13) + c * .015;
            values[(y * width + x) * 3 + c] = (ushort)Math.Round(light * 65535);
        }

        return RenderPipelineTestSupport.CreateBase(values, raw, height);
    }
}
