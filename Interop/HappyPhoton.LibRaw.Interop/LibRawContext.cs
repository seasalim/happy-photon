using System.Text;

namespace HappyPhoton.LibRaw.Interop;

public sealed unsafe class LibRawContext : IDisposable
{
    private readonly LibRawHandle _handle;
    private readonly INativeCallObserver? _observer;

    private LibRawContext(ulong token, INativeCallObserver? observer)
    {
        _handle = new LibRawHandle(token);
        _observer = observer;
    }

    public static LibRawRuntime Runtime => AbiHandshake.Ensure();

    public static LibRawRuntimeHealth RuntimeHealth => AbiHandshake.Probe();

    public static LibRawContext Open(string path, CancellationToken cancellationToken = default)
        => Open(path, cancellationToken, null);

    internal static LibRawContext Open(string path, CancellationToken cancellationToken,
        INativeCallObserver? observer)
    {
        _ = AbiHandshake.Ensure();
        NativeCallCoordinator.Before(cancellationToken, observer);
        return new LibRawContext(NativeApi.Open(path), observer);
    }

    public LibRawDimensions GetDimensions(CancellationToken cancellationToken = default)
        => Call(value => Convert(NativeApi.Dimensions(value)), cancellationToken);

    public LibRawSensorIdentity GetSensorIdentity(CancellationToken cancellationToken = default)
        => Call(value => Convert(NativeApi.Sensor(value)), cancellationToken);

    public LibRawMetadata GetMetadata(CancellationToken cancellationToken = default)
        => Call(value => Convert(NativeApi.Metadata(value)), cancellationToken);

    public LibRawCameraFacts? GetCameraFacts(CancellationToken cancellationToken = default)
        => Call(value => ConvertCameraFacts(NativeApi.CameraFacts(value)), cancellationToken);

    public LibRawFujiFacts? GetFujiFacts(CancellationToken cancellationToken = default)
        => Call(value => Convert(NativeApi.FujiFacts(value)), cancellationToken);

    public LibRawLensIdentity? GetLensIdentity(CancellationToken cancellationToken = default)
        => Call(value => ConvertLensIdentity(NativeApi.LensIdentity(value)), cancellationToken);

    public void Unpack(CancellationToken cancellationToken = default)
        => Call(value => NativeApi.Unpack(value), cancellationToken);

    public void ConfigureOutput(LibRawOutputConfiguration configuration,
        CancellationToken cancellationToken = default)
        => Call(value => NativeApi.Configure(value, configuration), cancellationToken);

    public void Process(CancellationToken cancellationToken = default)
        => Call(value => NativeApi.Process(value), cancellationToken);

    public void Recycle(CancellationToken cancellationToken = default)
        => Call(value => NativeApi.Recycle(value), cancellationToken);

    public LibRawImage? ExtractThumbnail(CancellationToken cancellationToken = default)
    {
        try
        {
            Call(value => NativeApi.UnpackThumbnail(value), cancellationToken);
            return Call(value => LibRawImage.FromNative(NativeApi.MakeThumbnail(value), false),
                cancellationToken);
        }
        catch (LibRawDecodeException) { return null; }
    }

    public LibRawImage MakeProcessedImage(CancellationToken cancellationToken = default)
        => Call(value => LibRawImage.FromNative(NativeApi.MakeProcessed(value), true),
            cancellationToken);

    public LibRawMosaicLease? BorrowMosaic(CancellationToken cancellationToken = default)
    {
        NativeCallCoordinator.Before(cancellationToken, _observer);
        var added = false;
        _handle.DangerousAddRef(ref added);
        try
        {
            var native = NativeApi.BorrowMosaic(_handle.Token, out var outcome);
            if (outcome == NativeStatus.Unavailable)
            {
                _handle.DangerousRelease();
                added = false;
                return null;
            }
            try
            {
                return new LibRawMosaicLease(_handle, native);
            }
            catch
            {
                ReleaseFailedMosaic(native, NativeApi.ReleaseMosaic);
                throw;
            }
        }
        catch
        {
            if (added) _handle.DangerousRelease();
            throw;
        }
    }

    public void Dispose() => _handle.Dispose();

    internal static void ReleaseFailedMosaic(
        NativeMosaicDescriptor native,
        Action<ulong> release)
    {
        if (native.Lease == 0) return;
        try { release(native.Lease); }
        catch { }
    }

    private T Call<T>(Func<ulong, T> action, CancellationToken cancellationToken)
    {
        NativeCallCoordinator.Before(cancellationToken, _observer);
        var added = false;
        _handle.DangerousAddRef(ref added);
        try { return action(_handle.Token); }
        finally { if (added) _handle.DangerousRelease(); }
    }

    private void Call(Action<ulong> action, CancellationToken cancellationToken) =>
        Call(value => { action(value); return true; }, cancellationToken);

    private static LibRawDimensions Convert(NativeDimensions value) => new(
        value.RawWidth, value.RawHeight, value.VisibleWidth, value.VisibleHeight,
        value.OutputWidth, value.OutputHeight, value.Orientation);

    private static LibRawSensorIdentity Convert(NativeSensorIdentity value)
    {
        var xtrans = new sbyte[checked((int)value.XtransCount)];
        if (xtrans.Length != 36) throw new InvalidDataException("X-Trans shape is invalid.");
        sbyte* source = value.Xtrans;
        new ReadOnlySpan<sbyte>(source, xtrans.Length).CopyTo(xtrans);
        byte* cdesc = value.Cdesc;
        return new(value.Colors, value.Filters, value.DngVersion, xtrans,
            Decode(cdesc, value.CdescLength, 5) ?? string.Empty);
    }

    private static LibRawMetadata Convert(NativeMetadata value)
    {
        byte* make = value.Make;
        byte* model = value.Model;
        byte* normalizedMake = value.NormalizedMake;
        byte* normalizedModel = value.NormalizedModel;
        byte* lens = value.Lens;
        return new(Decode(make, value.MakeLength), Decode(model, value.ModelLength),
            Decode(normalizedMake, value.NormalizedMakeLength),
            Decode(normalizedModel, value.NormalizedModelLength),
            Decode(lens, value.LensLength), Optional(value.IsoPresent, value.Iso),
            Optional(value.ShutterPresent, value.Shutter),
            Optional(value.AperturePresent, value.Aperture),
            Optional(value.FocalLengthPresent, value.FocalLength),
            Optional(value.FocalLength35mmPresent, value.FocalLength35mm),
            value.TimestampPresent != 0 ? value.Timestamp : null, value.Orientation,
            new(value.Gps.Parsed != 0,
                value.Gps.CoordinatePresent != 0 ? value.Gps.Latitude : null,
                value.Gps.CoordinatePresent != 0 ? value.Gps.Longitude : null,
                value.Gps.AltitudePresent != 0 ? value.Gps.Altitude : null));
    }

    internal static LibRawCameraFacts? ConvertCameraFacts(
        (NativeStatus Status, NativeCameraFacts Value) result)
    {
        if (result.Status == NativeStatus.Absent) return null;
        var value = result.Value;
        if (value.MultiplierCount is < 3 or > 4 || value.MatrixRows != 3 ||
            value.MatrixColumns != value.MultiplierCount) throw new InvalidDataException(
                "Camera fact shape is invalid.");
        var multipliers = new float[value.MultiplierCount];
        var matrix = new float[value.MatrixRows, value.MatrixColumns];
        float* source = value.Multipliers;
        float* matrixSource = value.CameraToSrgb;
        new ReadOnlySpan<float>(source, multipliers.Length).CopyTo(multipliers);
        for (var row = 0; row < matrix.GetLength(0); row++)
            for (var column = 0; column < matrix.GetLength(1); column++)
                matrix[row, column] = matrixSource[row * 4 + column];

        var preMultipliers = CopyOptionalFloats(
            value.PreMultipliers,
            value.PreMultiplierCount,
            value.MultiplierCount,
            "Pre-multiplier shape is invalid.");
        float[,]? cameraFromXyz = null;
        if (value.CameraFromXyzRows != 0 || value.CameraFromXyzColumns != 0)
        {
            if (value.CameraFromXyzRows != value.MultiplierCount ||
                value.CameraFromXyzColumns != 3)
            {
                throw new InvalidDataException("Camera-from-XYZ shape is invalid.");
            }

            cameraFromXyz = new float[value.CameraFromXyzRows, value.CameraFromXyzColumns];
            float* cameraFromXyzSource = value.CameraFromXyz;
            for (var row = 0; row < cameraFromXyz.GetLength(0); row++)
                for (var column = 0; column < cameraFromXyz.GetLength(1); column++)
                    cameraFromXyz[row, column] = cameraFromXyzSource[row * 3 + column];
        }

        var linearMax = CopyOptionalUInts(
            value.LinearMax,
            value.LinearMaxCount,
            value.MultiplierCount,
            "Linear-maximum shape is invalid.");
        return new(multipliers, matrix, preMultipliers, cameraFromXyz, linearMax);
    }

    private static float[]? CopyOptionalFloats(
        float* source,
        uint count,
        uint expectedCount,
        string error)
    {
        if (count == 0) return null;
        if (count != expectedCount) throw new InvalidDataException(error);
        var values = new float[count];
        new ReadOnlySpan<float>(source, values.Length).CopyTo(values);
        return values;
    }

    private static uint[]? CopyOptionalUInts(
        uint* source,
        uint count,
        uint expectedCount,
        string error)
    {
        if (count == 0) return null;
        if (count != expectedCount) throw new InvalidDataException(error);
        var values = new uint[count];
        new ReadOnlySpan<uint>(source, values.Length).CopyTo(values);
        return values;
    }

    private static LibRawFujiFacts? Convert((NativeStatus Status, NativeFujiFacts Value) result)
        => result.Status == NativeStatus.Absent ? null : new(
            result.Value.ExposureMidpointShift, result.Value.DynamicRange,
            result.Value.DynamicRangeSetting, result.Value.DevelopmentDynamicRange,
            result.Value.AutoDynamicRange);

    internal static LibRawLensIdentity? ConvertLensIdentity(
        (NativeStatus Status, NativeLensIdentity Value) result)
    {
        if (result.Status == NativeStatus.Absent) return null;
        var value = result.Value;
        if (value.Present == 0)
            throw new InvalidDataException("Native lens identity is not present.");
        byte* lens = value.Lens;
        byte* teleconverter = value.Teleconverter;
        byte* adapter = value.Adapter;
        byte* attachment = value.Attachment;
        return new(
            value.LensId, Decode(lens, value.LensLength),
            value.LensFormat, value.LensMount,
            value.CameraId, value.CameraFormat, value.CameraMount,
            value.FocalType, value.FocalUnits, value.MinFocal, value.MaxFocal,
            value.MaxApertureAtMinFocal, value.MaxApertureAtMaxFocal,
            value.MinApertureAtMinFocal, value.MinApertureAtMaxFocal,
            value.MaxAperture, value.MinAperture,
            value.CurrentFocal, value.CurrentAperture,
            value.MaxApertureAtCurrentFocal, value.MinApertureAtCurrentFocal,
            value.MinFocusDistance, value.FocusRangeIndex,
            value.LensFStops, value.FocalLength35mm,
            value.TeleconverterId, Decode(teleconverter, value.TeleconverterLength),
            value.AdapterId, Decode(adapter, value.AdapterLength),
            value.AttachmentId, Decode(attachment, value.AttachmentLength));
    }

    private static float? Optional(uint present, float value) => present != 0 ? value : null;
    private static string? Decode(byte* value, uint length, uint capacity = 128)
    {
        if (length == 0) return null;
        if (length > capacity) throw new InvalidDataException("Native text length is invalid.");
        return Encoding.UTF8.GetString(value, checked((int)length));
    }
}
