using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.MlSpike;

public sealed partial record PreviewInput
{
    public static BaseImage LoadBase(string path)
    {
        LocalFiles.Check(path);
        var loader = new GatedBaseImageLoader(
            new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()),
            new SourceAvailabilityService());
        return loader.LoadPreviewBase(new ImageFile(path), BaseDecodeSettings.Default, default)
            ?? throw new InvalidDataException($"App preview decode failed: {path}");
    }

    public static PreviewInput Render(BaseImage source, double exposure = 0)
    {
        using var result = new RenderPipeline().Render(new RenderRequest(source,
            new EditSettings { Exposure = exposure }, RenderIntent.Preview, 1600,
            new RenderOptions(ComputeStats: false, PreparePreviewPixels: true)));
        return new PreviewInput(result.PreviewPixels
            ?? throw new InvalidDataException("Render did not produce BGRA8."),
            (int)result.Image.Width, (int)result.Image.Height);
    }

    public static PreviewInput Load(string path)
    {
        using var source = LoadBase(path);
        return Render(source);
    }

    public MagickImage ToImage() => new(Bgra,
        new PixelReadSettings((uint)Width, (uint)Height, StorageType.Char, PixelMapping.BGRA));
}
