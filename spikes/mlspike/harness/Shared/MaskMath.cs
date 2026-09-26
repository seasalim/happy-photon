namespace HappyPhoton.MlSpike;

public static class MaskMath
{
    public static float[] Preprocess(PreviewInput image, ModelConfig config)
    {
        config.Validate();
        ValidateSize(image.Bgra.Length / 4, image.Width, image.Height);
        if (image.Bgra.Length != checked(image.Width * image.Height * 4))
            throw new ArgumentException("Invalid BGRA buffer.");
        var output = new float[checked(config.Width * config.Height * 3)];
        for (var y = 0; y < config.Height; y++)
        for (var x = 0; x < config.Width; x++)
        {
            var (x0, x1, dx) = Coordinate(x, config.Width, image.Width);
            var (y0, y1, dy) = Coordinate(y, config.Height, image.Height);
            for (var c = 0; c < 3; c++)
            {
                var channel = 2 - c;
                float At(int xx, int yy) => image.Bgra[(yy * image.Width + xx) * 4 + channel];
                var value = Lerp(Lerp(At(x0, y0), At(x1, y0), dx),
                    Lerp(At(x0, y1), At(x1, y1), dx), dy) / 255f;
                var index = config.InputLayout == "NCHW"
                    ? c * config.Width * config.Height + y * config.Width + x
                    : (y * config.Width + x) * 3 + c;
                output[index] = (value - config.Mean[c]) / config.Std[c];
            }
        }
        return output;
    }

    public static float[] Probabilities(float[] values, int[] shape, ModelConfig config)
    {
        if (shape.Length != 4 || shape[0] != 1 || shape.Any(v => v <= 0) ||
            values.Length != shape.Aggregate(1, (a, b) => checked(a * b)))
            throw new InvalidDataException("Expected a batch-one 4D output tensor.");
        var nchw = config.OutputLayout == "NCHW";
        int channels = shape[nchw ? 1 : 3], height = shape[nchw ? 2 : 1], width = shape[nchw ? 3 : 2];
        if (config.ClassIndex >= channels || values.Any(v => !float.IsFinite(v)))
            throw new InvalidDataException("Invalid class index or non-finite model output.");
        var result = new float[width * height];
        for (var i = 0; i < result.Length; i++)
        {
            float At(int c) => values[nchw ? c * result.Length + i : i * channels + c];
            var selected = At(config.ClassIndex);
            if (config.Activation == "softmax")
            {
                var max = Enumerable.Range(0, channels).Max(At);
                double sum = 0;
                for (var c = 0; c < channels; c++) sum += Math.Exp(At(c) - max);
                result[i] = (float)(Math.Exp(selected - max) / sum);
            }
            else if (config.Activation == "sigmoid")
                result[i] = (float)(1 / (1 + Math.Exp(-selected)));
            else if (selected is < 0 or > 1)
                throw new InvalidDataException("Probability output is outside [0,1].");
            else result[i] = selected;
        }
        return result;
    }

    public static byte[] ResizeMask(float[] probabilities, int width, int height, int targetWidth, int targetHeight)
    {
        ValidateSize(probabilities.Length, width, height);
        if (targetWidth <= 0 || targetHeight <= 0 ||
            probabilities.Any(p => !float.IsFinite(p) || p < 0 || p > 1))
            throw new ArgumentException("Invalid probabilities or target dimensions.");
        var output = new byte[checked(targetWidth * targetHeight)];
        for (var y = 0; y < targetHeight; y++)
        for (var x = 0; x < targetWidth; x++)
        {
            var (x0, x1, dx) = Coordinate(x, targetWidth, width);
            var (y0, y1, dy) = Coordinate(y, targetHeight, height);
            var value = Lerp(Lerp(probabilities[y0 * width + x0], probabilities[y0 * width + x1], dx),
                Lerp(probabilities[y1 * width + x0], probabilities[y1 * width + x1], dx), dy);
            output[y * targetWidth + x] = value >= 0.5f ? (byte)255 : (byte)0;
        }
        return output;
    }

    public static byte[] ResizeLabels(byte[] labels, int width, int height, int targetWidth, int targetHeight)
    {
        ValidateSize(labels.Length, width, height);
        if (targetWidth <= 0 || targetHeight <= 0 || labels.Any(v => v is not (0 or 255)))
            throw new ArgumentException("Expected binary labels and positive dimensions.");
        var output = new byte[checked(targetWidth * targetHeight)];
        for (var y = 0; y < targetHeight; y++)
        for (var x = 0; x < targetWidth; x++)
            output[y * targetWidth + x] = labels[
                Math.Min(height - 1, (int)((y + 0.5) * height / targetHeight)) * width +
                Math.Min(width - 1, (int)((x + 0.5) * width / targetWidth))];
        return output;
    }

    public static double IoU(byte[] actual, byte[] expected)
    {
        ValidatePair(actual, expected);
        long intersection = 0, union = 0;
        for (var i = 0; i < actual.Length; i++)
        {
            if (actual[i] != 0 && expected[i] != 0) intersection++;
            if (actual[i] != 0 || expected[i] != 0) union++;
        }
        return union == 0 ? 1 : (double)intersection / union;
    }

    public static double Difference(byte[] actual, byte[] expected)
    {
        ValidatePair(actual, expected);
        return actual.Zip(expected).Count(p => p.First != p.Second) / (double)actual.Length;
    }

    private static void ValidatePair(byte[] a, byte[] b)
    {
        if (a.Length == 0 || a.Length != b.Length || a.Concat(b).Any(v => v is not (0 or 255)))
            throw new ArgumentException("Masks must be equally sized, nonempty, and binary.");
    }

    private static void ValidateSize(int length, int width, int height)
    {
        if (width <= 0 || height <= 0 || length != checked(width * height))
            throw new ArgumentException("Invalid image dimensions.");
    }

    private static (int Low, int High, float Weight) Coordinate(int value, int target, int source)
    {
        var position = Math.Clamp((value + 0.5f) * source / target - 0.5f, 0, source - 1);
        var low = (int)position;
        return (low, Math.Min(low + 1, source - 1), position - low);
    }

    private static float Lerp(float a, float b, float amount) => a + (b - a) * amount;
}
