using System.Runtime.CompilerServices;
using ImageMagick;

namespace HappyPhoton.Services;

internal enum CameraRgbCharacterizationOutcome
{
    Usable,
    Derived,
    UncharacterizedPassthrough
}

internal sealed class CameraRgbCharacterization
{
    private readonly bool _applyMatrix;

    private CameraRgbCharacterization(
        CameraRgbCharacterizationOutcome outcome,
        double[,] cameraToRec2020,
        bool applyMatrix)
    {
        Outcome = outcome;
        CameraToRec2020 = cameraToRec2020;
        _applyMatrix = applyMatrix;
    }

    internal static CameraRgbCharacterization Passthrough { get; } = new(
        CameraRgbCharacterizationOutcome.UncharacterizedPassthrough,
        ChromaticAdaptation.Identity(),
        applyMatrix: false);

    internal CameraRgbCharacterizationOutcome Outcome { get; }

    internal double[,] CameraToRec2020 { get; }

    internal static CameraRgbCharacterization Create(
        RawCameraFactSnapshot facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.CamToSrgb is { } cameraToSrgb)
        {
            RequireThreeChannels(cameraToSrgb);
            return new CameraRgbCharacterization(
                CameraRgbCharacterizationOutcome.Usable,
                ComposeWorkingMatrix(cameraToSrgb),
                applyMatrix: true);
        }

        if (facts.IsIdentityCameraTransform &&
            TryDeriveCameraToSrgb(facts.CameraFromXyz, out cameraToSrgb))
        {
            return new CameraRgbCharacterization(
                CameraRgbCharacterizationOutcome.Derived,
                ComposeWorkingMatrix(cameraToSrgb),
                applyMatrix: true);
        }

        return Passthrough;
    }

    internal MagickImage ImportRgb16(
        ReadOnlySpan<byte> data,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(data, width, height, sizeof(ushort));
        if (!_applyMatrix)
        {
            return ImportDirect(
                data,
                width,
                height,
                StorageType.Short,
                PixelMapping.RGB);
        }

        return ImportCharacterized(data, width, height, cancellationToken);
    }

    internal static MagickImage ImportRgb8(
        ReadOnlySpan<byte> data,
        int width,
        int height)
    {
        ValidateInput(data, width, height, sizeof(byte));
        return ImportDirect(
            data,
            width,
            height,
            StorageType.Char,
            PixelMapping.RGB);
    }

    private static double[,] ComposeWorkingMatrix(double[,] cameraToSrgb) =>
        ChromaticAdaptation.Multiply(
            RgbColorSpaceMatrices.LinearSrgbToLinearRec2020DerivedExact,
            cameraToSrgb);

    private static bool TryDeriveCameraToSrgb(
        double[,]? cameraFromXyz,
        out double[,] cameraToSrgb)
    {
        cameraToSrgb = ChromaticAdaptation.Identity();
        if (cameraFromXyz == null)
        {
            return false;
        }

        RequireThreeChannels(cameraFromXyz);
        var cameraFromSrgb = ChromaticAdaptation.Multiply(
            cameraFromXyz,
            RgbColorSpaceMatrices.LinearSrgbToXyzD65PublishedRounded);
        for (var row = 0; row < 3; row++)
        {
            var sum = cameraFromSrgb[row, 0] +
                cameraFromSrgb[row, 1] + cameraFromSrgb[row, 2];
            if (!double.IsFinite(sum) || Math.Abs(sum) < 1e-12)
            {
                return false;
            }

            for (var column = 0; column < 3; column++)
            {
                cameraFromSrgb[row, column] /= sum;
            }
        }

        return TryInvert(cameraFromSrgb, out cameraToSrgb);
    }

    private static bool TryInvert(double[,] value, out double[,] inverse)
    {
        var a = value[0, 0];
        var b = value[0, 1];
        var c = value[0, 2];
        var d = value[1, 0];
        var e = value[1, 1];
        var f = value[1, 2];
        var g = value[2, 0];
        var h = value[2, 1];
        var i = value[2, 2];
        var determinant = a * (e * i - f * h) -
            b * (d * i - f * g) + c * (d * h - e * g);
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-12)
        {
            inverse = ChromaticAdaptation.Identity();
            return false;
        }

        var scale = 1 / determinant;
        inverse = new[,]
        {
            { (e * i - f * h) * scale, (c * h - b * i) * scale, (b * f - c * e) * scale },
            { (f * g - d * i) * scale, (a * i - c * g) * scale, (c * d - a * f) * scale },
            { (d * h - e * g) * scale, (b * g - a * h) * scale, (a * e - b * d) * scale }
        };
        return inverse.Cast<double>().All(double.IsFinite);
    }

    private unsafe MagickImage ImportCharacterized(
        ReadOnlySpan<byte> data,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        var image = CreateDestination(width, height);
        try
        {
            RenderKernelSupport.PixelLayout layout;
            using (var pixels = image.GetPixels())
            {
                layout = RenderKernelSupport.GetLayout(pixels);
            }

            using var destinationPixels = image.GetPixelsUnsafe();
            var destinationAddress = destinationPixels.GetAreaPointer(
                0,
                0,
                (uint)width,
                (uint)height);
            if (destinationAddress == 0)
            {
                throw new InvalidOperationException(
                    "Unable to access the Q16 destination pixels.");
            }

            fixed (byte* source = data)
            {
                TransformPixels(
                    (nint)source,
                    destinationAddress,
                    checked(width * height),
                    layout,
                    cancellationToken);
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private unsafe void TransformPixels(
        nint sourceAddress,
        nint destinationAddress,
        int pixelCount,
        RenderKernelSupport.PixelLayout layout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workers = Math.Min(
            Environment.ProcessorCount,
            Math.Max(1, (pixelCount + 32_767) / 32_768));
        Parallel.For(0, workers, new ParallelOptions
        {
            CancellationToken = cancellationToken
        }, worker =>
        {
            var start = pixelCount * worker / workers;
            var end = pixelCount * (worker + 1) / workers;
            var source = new ReadOnlySpan<ushort>(
                (void*)(sourceAddress + checked(start * 3 * sizeof(ushort))),
                checked((end - start) * 3));
            var destination = new Span<ushort>(
                (void*)(destinationAddress + checked(
                    start * layout.Channels * sizeof(ushort))),
                checked((end - start) * layout.Channels));
            var output = 0;
            for (var pixel = 0; pixel < end - start; pixel++)
            {
                var input = pixel * 3;
                var red = source[input];
                var green = source[input + 1];
                var blue = source[input + 2];
                destination[output + layout.Red] = EncodeQ16(
                    Transform(0, red, green, blue));
                destination[output + layout.Green] = EncodeQ16(
                    Transform(1, red, green, blue));
                destination[output + layout.Blue] = EncodeQ16(
                    Transform(2, red, green, blue));
                output += layout.Channels;
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double Transform(
        int row,
        ushort red,
        ushort green,
        ushort blue) =>
        CameraToRec2020[row, 0] * red +
        CameraToRec2020[row, 1] * green +
        CameraToRec2020[row, 2] * blue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort EncodeQ16(double value)
    {
        if (value <= ushort.MinValue) return ushort.MinValue;
        if (value >= ushort.MaxValue) return ushort.MaxValue;
        return (ushort)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static MagickImage ImportDirect(
        ReadOnlySpan<byte> data,
        int width,
        int height,
        StorageType storageType,
        PixelMapping mapping)
    {
        var image = CreateDestination(width, height);
        try
        {
            image.ImportPixels(
                data,
                new PixelImportSettings(
                    (uint)width,
                    (uint)height,
                    storageType,
                    mapping));
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static MagickImage CreateDestination(int width, int height)
    {
        var image = new MagickImage(
            MagickColors.Black,
            (uint)width,
            (uint)height);
        image.ColorSpace = ColorSpace.RGB;
        return image;
    }

    private static void ValidateInput(
        ReadOnlySpan<byte> data,
        int width,
        int height,
        int bytesPerSample)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "RGB dimensions must be positive.");
        }

        var expectedLength = checked(width * height * 3 * bytesPerSample);
        if (data.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Expected {expectedLength} bytes for a {width}x{height} RGB image.",
                nameof(data));
        }
    }

    private static void RequireThreeChannels(double[,] matrix)
    {
        if (matrix.GetLength(0) != 3 || matrix.GetLength(1) != 3 ||
            matrix.Cast<double>().Any(value => !double.IsFinite(value)))
        {
            throw new NotSupportedException(
                "Camera characterization requires three finite RGB channels.");
        }
    }
}
