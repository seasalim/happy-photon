using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

/// <summary>
/// Regression: a profile-active decode+render of an X-Trans file must fill
/// the whole frame (user-reported half-black output on a Fujifilm RAF).
/// </summary>
public sealed class XTransProfileRenderRegressionTests
{
    [Fact]
    public void ProfileActiveXTransPreview_FillsTheWholeFrame()
    {
        var fixture = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "fujifilm-x30.raf");
        using var directory = new TemporaryDirectory();
        var hueSat = BuildNeutralHueSatTable(hue: 6, saturation: 4, value: 2);
        var path = SyntheticDcpFactory.WriteTemporary(
            directory.Path,
            new SyntheticDcpOptions
            {
                Name = "XTrans regression",
                EmbedPolicy = 3,
                HueSatDimensions = [6, 4, 2],
                HueSatTable1 = hueSat
            });
        var reader = new DcpProfileReader();
        var snapshot = reader.ReadExternalSnapshot(path);
        var parsed = reader.ParseExternal(snapshot, "xtrans-regression");
        var selection = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = path,
            ContentHash = snapshot.ContentHash
        };
        var decode = BaseDecodeSettings.From(new EditSettings
        {
            RawProfile = selection
        }).WithProfileResolution(
            DcpProfileResolution.Success(selection, parsed));

        using var baseImage = new RawBaseLoader().LoadPreviewBase(
            new ImageFile(fixture),
            decode,
            CancellationToken.None);
        Assert.NotNull(baseImage);
        Assert.Equal(
            DcpProfileErrorCode.None,
            baseImage!.Info.Decode.ProfileResolution!.Status);

        AssertBandsLit(baseImage.Pixels, "base");

        using var rendered = new RenderPipeline().Render(new RenderRequest(
            baseImage,
            new EditSettings { RawProfile = selection },
            RenderIntent.Preview,
            MaxDimension: null,
            new RenderOptions(ComputeStats: false)));
        AssertBandsLit(rendered.Image, "render");
    }

    private static void AssertBandsLit(MagickImage image, string stage)
    {
        using var pixels = image.GetPixels();
        var height = (int)image.Height;
        var width = (int)image.Width;
        foreach (var fraction in new[] { 0.1, 0.5, 0.75, 0.95 })
        {
            var y = Math.Min(height - 1, (int)(height * fraction));
            var row = pixels.GetArea(0, y, (uint)width, 1);
            Assert.NotNull(row);
            double sum = 0;
            foreach (var value in row!) sum += value;
            Assert.True(
                sum > 0,
                $"{stage}: row at {fraction:P0} of the frame is entirely black.");
        }
    }

    private static float[] BuildNeutralHueSatTable(
        int hue,
        int saturation,
        int value)
    {
        var table = new float[hue * saturation * value * 3];
        for (var index = 0; index < table.Length; index += 3)
        {
            table[index] = 0f;
            table[index + 1] = 1f;
            table[index + 2] = 1f;
        }
        return table;
    }
}
