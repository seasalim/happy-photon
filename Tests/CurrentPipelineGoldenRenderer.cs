using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal sealed class CurrentPipelineGoldenRenderer
{
    public const int LongEdge = 500;

    private readonly RenderPipeline _renderPipeline = new();
    private readonly IBaseImageLoader _baseLoader = new BaseLoaderRouter(
        new RawBaseLoader(),
        new StandardBaseLoader());

    public BaseImage LoadBase(GoldenAssetCase asset) =>
        Load(asset, preview: false);

    public BaseImage LoadPreviewBase(GoldenAssetCase asset) =>
        Load(asset, preview: true);

    private BaseImage Load(GoldenAssetCase asset, bool preview)
    {
        if (!File.Exists(asset.FilePath))
        {
            throw new FileNotFoundException(
                $"Golden test asset is missing: {asset.FilePath}", asset.FilePath);
        }

        var file = new ImageFile(asset.FilePath);
        var result = preview
            ? _baseLoader.LoadPreviewBase(
                file,
                BaseDecodeSettings.Default,
                CancellationToken.None)
            : _baseLoader.LoadFullBase(
                file,
                BaseDecodeSettings.Default,
                CancellationToken.None);
        return result ?? throw new InvalidOperationException(
            $"Could not decode golden asset: {asset.FilePath}");
    }

    public MagickImage Render(
        BaseImage baseImage,
        EditSettings settings,
        RenderIntent intent = RenderIntent.Export,
        int maxDimension = LongEdge)
    {
        using var rendered = _renderPipeline.Render(new RenderRequest(
            baseImage,
            settings,
            intent,
            maxDimension,
            new RenderOptions(false, false)));
        var output = new MagickImage(rendered.Image);
        try
        {
            output.Format = MagickFormat.Png;
            output.Strip();
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    internal static void NormalizeToSrgb(MagickImage image)
    {
        if (image.GetColorProfile() is { } sourceProfile)
        {
            image.TransformColorSpace(sourceProfile, ColorProfiles.SRGB);
        }
        else if (image.ColorSpace != ColorSpace.sRGB)
        {
            image.ColorSpace = ColorSpace.sRGB;
        }
    }
}
