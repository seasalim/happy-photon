using System.Runtime.InteropServices;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal sealed class PrecisionFixture : IDisposable
{
    private PrecisionFixture(
        string name,
        int width,
        int height,
        ushort[] sourceCodes,
        double[] expectedLinear,
        BaseImage baseImage,
        bool loadedFromTiff)
    {
        Name = name;
        Width = width;
        Height = height;
        SourceCodes = sourceCodes;
        ExpectedLinear = expectedLinear;
        Base = baseImage;
        LoadedFromTiff = loadedFromTiff;
        ValidateBaseContract(baseImage, width, height);
    }

    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public ushort[] SourceCodes { get; }
    public double[] ExpectedLinear { get; }
    public BaseImage Base { get; }
    public bool LoadedFromTiff { get; }

    public static IReadOnlyList<PrecisionFixture> CreateAll(string temporaryRoot) =>
    [
        CreateLinearSweep(),
        CreateLinearDeepShadow(),
        CreateSrgbTiff(temporaryRoot)
    ];

    private static PrecisionFixture CreateLinearSweep()
    {
        const int width = ushort.MaxValue + 1;
        const int height = 8;
        var codes = new ushort[width];
        for (var x = 0; x < width; x++)
        {
            codes[x] = (ushort)x;
        }

        return CreateLinear("linear-q16-sweep", width, height, codes);
    }

    private static PrecisionFixture CreateLinearDeepShadow()
    {
        const int width = 1600;
        const int height = 64;
        var codes = CreateRampCodes(width, 4095);
        return CreateLinear("linear-deep-shadow", width, height, codes);
    }

    private static PrecisionFixture CreateLinear(
        string name,
        int width,
        int height,
        ushort[] codes)
    {
        var image = ImportRows(codes, width, height, ColorSpace.RGB);
        image.Depth = 16;
        var expected = codes
            .Select(code => code / (double)ushort.MaxValue)
            .ToArray();
        return new PrecisionFixture(
            name,
            width,
            height,
            codes,
            expected,
            Wrap(image, width, height),
            loadedFromTiff: false);
    }

    private static PrecisionFixture CreateSrgbTiff(string temporaryRoot)
    {
        const int width = 1600;
        const int height = 64;
        var codes = CreateRampCodes(width, 8191);
        var path = Path.Combine(temporaryRoot, "srgb16-deep-shadow.tiff");
        using (var source = ImportRows(codes, width, height, ColorSpace.sRGB))
        {
            source.Depth = 16;
            source.SetProfile(ColorProfiles.SRGB);
            source.Format = MagickFormat.Tiff;
            source.Settings.Compression = CompressionMethod.NoCompression;
            source.Write(path);
        }

        VerifyTiffSource(path, codes, width, height);
        var loaded = new StandardBaseLoader().LoadFullBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None) ??
            throw new InvalidOperationException(
                "The generated RGB16 TIFF did not load through StandardBaseLoader.");
        var expected = codes
            .Select(code => ToneLut.SrgbDecode(
                code / (double)ushort.MaxValue))
            .ToArray();
        return new PrecisionFixture(
            "srgb16-deep-shadow-tiff",
            width,
            height,
            codes,
            expected,
            loaded,
            loadedFromTiff: true);
    }

    private static ushort[] CreateRampCodes(int width, int maximum)
    {
        var codes = new ushort[width];
        for (var x = 0; x < width; x++)
        {
            codes[x] = (ushort)Math.Round(
                x / (double)(width - 1) * maximum);
        }
        return codes;
    }

    private static MagickImage ImportRows(
        ushort[] codes,
        int width,
        int height,
        ColorSpace colorSpace)
    {
        var samples = new ushort[checked(width * height * 3)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 3;
                samples[offset] = codes[x];
                samples[offset + 1] = codes[x];
                samples[offset + 2] = codes[x];
            }
        }

        var image = new MagickImage(MagickColors.Black, (uint)width, (uint)height);
        image.ColorSpace = colorSpace;
        image.ImportPixels(
            MemoryMarshal.AsBytes(samples.AsSpan()),
            new PixelImportSettings(
                (uint)width,
                (uint)height,
                StorageType.Short,
                PixelMapping.RGB));
        return image;
    }

    private static void VerifyTiffSource(
        string path,
        ushort[] codes,
        int width,
        int height)
    {
        using var reopened = new MagickImage(path);
        if (reopened.Width != width || reopened.Height != height || reopened.Depth != 16 ||
            reopened.Compression != CompressionMethod.NoCompression)
        {
            throw new InvalidOperationException(
                "The generated TIFF did not reopen as the specified RGB16 fixture.");
        }

        var actual = reopened.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
            throw new InvalidOperationException("Unable to read the reopened TIFF pixels.");
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 3;
                var expected = codes[x];
                if (actual[offset] != expected ||
                    actual[offset + 1] != expected ||
                    actual[offset + 2] != expected)
                {
                    throw new InvalidOperationException(
                        $"TIFF source-code gate failed at ({x},{y}): " +
                        $"expected {expected}, observed " +
                        $"{actual[offset]}/{actual[offset + 1]}/{actual[offset + 2]}.");
                }
            }
        }
    }

    private static BaseImage Wrap(MagickImage image, int width, int height) =>
        new(image, new BaseImageInfo(
            BaseSourceKind.Standard,
            IsRawSource: false,
            BaseDecodeSettings.Default,
            CamMul: null,
            CamToSrgb: null,
            AsShotKelvin: 6504,
            AsShotTint: 0,
            HadIccProfile: false,
            IccDescription: null,
            ExifOrientationApplied: 1,
            FullWidth: width,
            FullHeight: height,
            SourceExposureBiasEv: 0));

    private static void ValidateBaseContract(
        BaseImage baseImage,
        int width,
        int height)
    {
        var info = baseImage.Info;
        if (info.IsRawSource || info.SourceExposureBiasEv != 0 ||
            info.CamMul != null || info.CamToSrgb != null ||
            info.FullWidth != width || info.FullHeight != height)
        {
            throw new InvalidOperationException(
                "The precision fixture does not satisfy the non-RAW base contract.");
        }
    }

    public void Dispose() => Base.Dispose();
}
