using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HappyPhoton.Services;

public enum DisplaySourceColorSpace
{
    Srgb,
    DisplayP3,
}

public enum DisplayProfileSupport
{
    Absent,
    Srgb,
    MatrixTrc,
    LutBased,
    Mhc2,
    Invalid,
    AcmManaged,
    AcmQueryFailed,
}

public sealed class DisplayTransformSnapshot
{
    private readonly PixelTransform _srgb;
    private readonly PixelTransform _displayP3;

    internal DisplayTransformSnapshot(
        string identity,
        string profileName,
        DisplayProfileSupport support,
        PixelTransform srgb,
        PixelTransform displayP3,
        string diagnosticText)
    {
        Identity = identity;
        ProfileName = profileName;
        Support = support;
        _srgb = srgb;
        _displayP3 = displayP3;
        DiagnosticText = diagnosticText;
    }

    public static DisplayTransformSnapshot None { get; } = CreateTreatedSrgb(
        "none", "none", DisplayProfileSupport.Absent,
        "Display profile · none (sRGB)");

    public string Identity { get; }
    public string ProfileName { get; }
    public DisplayProfileSupport Support { get; }
    public string DiagnosticText { get; }

    public bool IsIdentity(DisplaySourceColorSpace sourceColorSpace) =>
        TransformFor(sourceColorSpace).IsIdentity;

    public Bitmap Derive(Bitmap canonical, DisplaySourceColorSpace sourceColorSpace)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        var transform = TransformFor(sourceColorSpace);
        return transform.IsIdentity ? canonical : transform.Apply(canonical);
    }

    internal static DisplayTransformSnapshot CreateTreatedSrgb(
        string identity,
        string profileName,
        DisplayProfileSupport support,
        string diagnosticText) =>
        new(
            identity,
            profileName,
            support,
            PixelTransform.Identity,
            PixelTransform.Create(
                Matrix3.DisplayP3ToXyzD50,
                Matrix3.SrgbToXyzD50,
                ToneCurve.Srgb,
                ToneCurve.Srgb,
                ToneCurve.Srgb),
            diagnosticText);

    internal static DisplayTransformSnapshot CreateManaged(
        string identity,
        string profileName,
        IccDisplayProfile profile,
        string acmDiagnostic) =>
        new(
            identity,
            profileName,
            DisplayProfileSupport.MatrixTrc,
            PixelTransform.Create(
                Matrix3.SrgbToXyzD50,
                profile.Colorants,
                profile.RedCurve,
                profile.GreenCurve,
                profile.BlueCurve),
            PixelTransform.Create(
                Matrix3.DisplayP3ToXyzD50,
                profile.Colorants,
                profile.RedCurve,
                profile.GreenCurve,
                profile.BlueCurve),
            $"Display profile · {profileName} · matrix/TRC active · {acmDiagnostic}");

    private PixelTransform TransformFor(DisplaySourceColorSpace sourceColorSpace) =>
        sourceColorSpace == DisplaySourceColorSpace.Srgb ? _srgb : _displayP3;
}

internal sealed class PixelTransform
{
    private const int EncodeLutSize = 4096;
    private const int EncodeLowEntries = 256;
    private const float EncodeLowLimit = 1f / 4096;
    private readonly Matrix3 _matrix;
    private readonly float[] _decode;
    private readonly byte[] _encodeRed;
    private readonly byte[] _encodeGreen;
    private readonly byte[] _encodeBlue;

    private PixelTransform(
        bool isIdentity,
        Matrix3 matrix,
        float[] decode,
        byte[] encodeRed,
        byte[] encodeGreen,
        byte[] encodeBlue)
    {
        IsIdentity = isIdentity;
        _matrix = matrix;
        _decode = decode;
        _encodeRed = encodeRed;
        _encodeGreen = encodeGreen;
        _encodeBlue = encodeBlue;
    }

    internal static PixelTransform Identity { get; } = new(
        true, Matrix3.Identity, [], [], [], []);

    internal bool IsIdentity { get; }

    internal static PixelTransform Create(
        Matrix3 sourceColorants,
        Matrix3 destinationColorants,
        ToneCurve red,
        ToneCurve green,
        ToneCurve blue)
    {
        var decode = new float[256];
        for (var index = 0; index < decode.Length; index++)
            decode[index] = (float)ToneCurve.Srgb.Evaluate(index / 255.0);
        return new(
            false,
            destinationColorants.Inverse() * sourceColorants,
            decode,
            CreateEncodeLut(red),
            CreateEncodeLut(green),
            CreateEncodeLut(blue));
    }

    internal WriteableBitmap Apply(Bitmap source)
    {
        var output = new WriteableBitmap(
            source.PixelSize,
            source.Dpi,
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        try
        {
            using var framebuffer = output.Lock();
            source.CopyPixels(framebuffer);
            Transform(framebuffer.Address, framebuffer.RowBytes,
                source.PixelSize.Width, source.PixelSize.Height);
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private unsafe void Transform(IntPtr address, int rowBytes, int width, int height)
    {
        var m11 = (float)_matrix.M11;
        var m12 = (float)_matrix.M12;
        var m13 = (float)_matrix.M13;
        var m21 = (float)_matrix.M21;
        var m22 = (float)_matrix.M22;
        var m23 = (float)_matrix.M23;
        var m31 = (float)_matrix.M31;
        var m32 = (float)_matrix.M32;
        var m33 = (float)_matrix.M33;
        var decode = _decode;
        var encodeRed = _encodeRed;
        var encodeGreen = _encodeGreen;
        var encodeBlue = _encodeBlue;
        var workers = Math.Min(Environment.ProcessorCount, height);
        Parallel.For(0, workers, worker =>
        {
            var firstRow = height * worker / workers;
            var lastRow = height * (worker + 1) / workers;
            for (var y = firstRow; y < lastRow; y++)
            {
                var pixel = (byte*)address + y * rowBytes;
                for (var x = 0; x < width; x++, pixel += 4)
                {
                    var red = decode[pixel[2]];
                    var green = decode[pixel[1]];
                    var blue = decode[pixel[0]];
                    var outputRed = m11 * red + m12 * green + m13 * blue;
                    var outputGreen = m21 * red + m22 * green + m23 * blue;
                    var outputBlue = m31 * red + m32 * green + m33 * blue;
                    pixel[2] = Encode(outputRed, encodeRed);
                    pixel[1] = Encode(outputGreen, encodeGreen);
                    pixel[0] = Encode(outputBlue, encodeBlue);
                }
            }
        });
    }

    private static byte Encode(float value, byte[] lut)
    {
        if (value <= 0) return 0;
        if (value >= 1) return 255;
        var index = value < EncodeLowLimit
            ? (int)(value / EncodeLowLimit * (EncodeLowEntries - 1) + 0.5f)
            : EncodeLowEntries + (int)((value - EncodeLowLimit) /
                (1 - EncodeLowLimit) * (EncodeLutSize - EncodeLowEntries - 1) + 0.5f);
        return lut[index];
    }

    private static byte[] CreateEncodeLut(ToneCurve curve)
    {
        var result = new byte[EncodeLutSize];
        for (var index = 0; index < result.Length; index++)
        {
            var target = index < EncodeLowEntries
                ? index / (double)(EncodeLowEntries - 1) * EncodeLowLimit
                : EncodeLowLimit + (index - EncodeLowEntries) /
                    (double)(EncodeLutSize - EncodeLowEntries - 1) *
                    (1 - EncodeLowLimit);
            var low = 0.0;
            var high = 1.0;
            for (var iteration = 0; iteration < 18; iteration++)
            {
                var middle = (low + high) * 0.5;
                if (curve.Evaluate(middle) < target) low = middle;
                else high = middle;
            }
            result[index] = checked((byte)Math.Round((low + high) * 0.5 * 255));
        }
        return result;
    }
}
