using System.Runtime.InteropServices;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal sealed record PrecisionFixturePopulation(
    string Id,
    string Kind,
    string RowSemantics,
    string Intensity);

internal sealed class PrecisionFixture : IDisposable
{
    private PrecisionFixture(
        string name,
        int width,
        int height,
        ushort[] sourceCodes,
        double[] expectedLinear,
        double[] expectedLinearRgb,
        double[] sweepParameters,
        IReadOnlyList<string> rowNames,
        BaseImage baseImage,
        bool loadedFromTiff,
        PrecisionFixturePopulation? population = null)
    {
        Name = name;
        Width = width;
        Height = height;
        SourceCodes = sourceCodes;
        ExpectedLinear = expectedLinear;
        ExpectedLinearRgb = expectedLinearRgb;
        SweepParameters = sweepParameters;
        RowNames = rowNames;
        Base = baseImage;
        LoadedFromTiff = loadedFromTiff;
        Population = population ?? new PrecisionFixturePopulation(
            "synthetic-neutral-ramp",
            "synthetic-neutral-ramp",
            "horizontal-grayscale-sweeps",
            "fixture-declared-range");
        ValidateBaseContract(baseImage, width, height);
    }

    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public ushort[] SourceCodes { get; }
    public double[] ExpectedLinear { get; }
    public double[] ExpectedLinearRgb { get; }
    public double[] SweepParameters { get; }
    public IReadOnlyList<string> RowNames { get; }
    public BaseImage Base { get; }
    public bool LoadedFromTiff { get; }
    public PrecisionFixturePopulation Population { get; }

    public static IReadOnlyList<PrecisionFixture> CreateAll(string temporaryRoot) =>
    [
        CreateLinearSweep(),
        CreateLinearDeepShadow(),
        CreateSrgbTiff(temporaryRoot)
    ];

    public static PrecisionFixture CreateChromaticAdaptationSweep(
        double asShotKelvin,
        PrecisionFixturePopulation? population = null)
    {
        const int width = 257;
        string[] rowNames =
        [
            "red", "green", "blue", "cyan", "magenta", "yellow",
            "ring-rg-up", "ring-rg-down", "ring-gb-up",
            "ring-gb-down", "ring-br-up", "ring-br-down"
        ];
        var height = rowNames.Length;
        var expected = new double[checked(width * height * 3)];
        var sweep = new double[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var t = x / (double)(width - 1);
                var rgb = CreateSweepColor(y, t);
                var pixel = y * width + x;
                var offset = pixel * 3;
                expected[offset] = rgb.Red;
                expected[offset + 1] = rgb.Green;
                expected[offset + 2] = rgb.Blue;
                sweep[pixel] = t;
            }
        }

        var samples = expected.Select(ToQuantum).ToArray();
        var image = ImportRgb(samples, width, height, ColorSpace.RGB);
        image.Depth = 16;
        return new PrecisionFixture(
            $"adaptation-sweep-anchor-{asShotKelvin:0}",
            width,
            height,
            [],
            [],
            expected,
            sweep,
            rowNames,
            Wrap(image, width, height, asShotKelvin),
            loadedFromTiff: false,
            population: population ?? new PrecisionFixturePopulation(
                "synthetic-saturation-extreme",
                "synthetic-saturation-extreme",
                "six-full-intensity-primary-secondary-sweeps-and-six-near-gamut-ring-traversals",
                "primary-secondary-0-to-1,ring-low-0.04,ring-high-0.90"));
    }

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
            ExpandGrayscale(expected, width, height),
            ExpandSweep(width, height),
            Enumerable.Range(0, height).Select(row => $"row-{row}").ToArray(),
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
            ExpandGrayscale(expected, width, height),
            ExpandSweep(width, height),
            Enumerable.Range(0, height).Select(row => $"row-{row}").ToArray(),
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

    private static MagickImage ImportRgb(
        ushort[] samples,
        int width,
        int height,
        ColorSpace colorSpace)
    {
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

    private static (double Red, double Green, double Blue) CreateSweepColor(
        int row,
        double t)
    {
        const double high = 0.90;
        const double low = 0.04;
        return row switch
        {
            0 => (t, 0, 0),
            1 => (0, t, 0),
            2 => (0, 0, t),
            3 => (0, t, t),
            4 => (t, 0, t),
            5 => (t, t, 0),
            6 => (high, low + (high - low) * t, low),
            7 => (high, high - (high - low) * t, low),
            8 => (low, high, low + (high - low) * t),
            9 => (low, high, high - (high - low) * t),
            10 => (low + (high - low) * t, low, high),
            11 => (high - (high - low) * t, low, high),
            _ => throw new ArgumentOutOfRangeException(nameof(row))
        };
    }

    private static ushort ToQuantum(double value) =>
        (ushort)Math.Round(
            Math.Clamp(value, 0, 1) * ushort.MaxValue,
            MidpointRounding.AwayFromZero);

    private static double[] ExpandGrayscale(
        double[] columns,
        int width,
        int height)
    {
        var result = new double[checked(width * height * 3)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 3;
                result[offset] = columns[x];
                result[offset + 1] = columns[x];
                result[offset + 2] = columns[x];
            }
        }
        return result;
    }

    private static double[] ExpandSweep(int width, int height)
    {
        var result = new double[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                result[y * width + x] = x / (double)(width - 1);
            }
        }
        return result;
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

    private static BaseImage Wrap(
        MagickImage image,
        int width,
        int height,
        double asShotKelvin = 6504) =>
        new(image, new BaseImageInfo(
            BaseSourceKind.Standard,
            IsRawSource: false,
            BaseDecodeSettings.Default,
            CamMul: null,
            CamToSrgb: null,
            AsShotKelvin: asShotKelvin,
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
