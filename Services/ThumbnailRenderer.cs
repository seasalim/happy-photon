using Avalonia.Media.Imaging;
using ImageMagick;
using HappyPhoton.Models;
using static HappyPhoton.Services.BitmapConversionService;

namespace HappyPhoton.Services;

internal sealed class ThumbnailRenderer
{
    private readonly RenderPipeline _renderPipeline;

    public ThumbnailRenderer(RenderPipeline renderPipeline) =>
        _renderPipeline = renderPipeline;

    public Bitmap RenderRawGeometry(
        Bitmap source,
        EditSettings settings,
        int generationDimension)
    {
        using var image = ConvertToMagickImage(source);
        RenderGeometry.Apply(image, settings);
        if (image.Width > generationDimension || image.Height > generationDimension)
        {
            ApplyThumbnailSize(image, generationDimension);
        }
        return ConvertToBitmap(image)!;
    }

    public Bitmap RenderStandardEdits(
        Bitmap source,
        EditSettings settings,
        int generationDimension)
    {
        MagickImage? image = ConvertToMagickImage(source);
        try
        {
            image.ColorSpace = ColorSpace.RGB;
            image.Depth = 16;
            image.Strip();
            using var baseImage = new BaseImage(
                image,
                new BaseImageInfo(
                    Kind: BaseSourceKind.Standard,
                    IsRawSource: false,
                    Decode: BaseDecodeSettings.Default,
                    CamMul: null,
                    CamToSrgb: null,
                    AsShotKelvin: 6504,
                    AsShotTint: 0,
                    HadIccProfile: false,
                    IccDescription: null,
                    ExifOrientationApplied: 1,
                    FullWidth: source.PixelSize.Width,
                    FullHeight: source.PixelSize.Height));
            image = null;
            using var rendered = _renderPipeline.Render(new RenderRequest(
                baseImage,
                settings,
                RenderIntent.Preview,
                generationDimension,
                new RenderOptions(false, false)));
            return ConvertToBitmap(rendered.Image)!;
        }
        finally
        {
            image?.Dispose();
        }
    }
}
