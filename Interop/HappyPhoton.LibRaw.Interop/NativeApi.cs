using System.Runtime.CompilerServices;
using System.Text;

namespace HappyPhoton.LibRaw.Interop;

internal static unsafe class NativeApi
{
    private const uint Abi = 2;
    private const int ErrorCapacity = 512;

    internal static LibRawRuntime Runtime()
    {
        NativeRuntimeInfo value = Init<NativeRuntimeInfo>();
        Span<byte> text = stackalloc byte[ErrorCapacity];
        fixed (byte* pointer = text)
        {
            var error = Error(pointer);
            var status = NativeMethods.Runtime(ref value, ref error);
            Check(status, error, text);
        }
        byte* version = value.VersionString;
        return new(Abi, value.LibRawVersionNumber,
            Encoding.UTF8.GetString(version, checked((int)value.VersionStringLength)),
            value.Capabilities, value.ThreadSafeVariant != 0);
    }

    internal static ulong Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\0')) throw new ArgumentException("Path contains NUL.", nameof(path));
        var utf8 = new UTF8Encoding(false, true).GetBytes(path);
        Span<byte> text = stackalloc byte[ErrorCapacity];
        fixed (byte* pathPointer = utf8)
        fixed (byte* textPointer = text)
        {
            var error = Error(textPointer);
            var status = NativeMethods.Open(pathPointer, checked((uint)utf8.Length),
                out var handle, ref error);
            Check(status, error, text);
            return handle;
        }
    }

    internal static NativeDimensions Dimensions(ulong handle)
    {
        NativeDimensions value = Init<NativeDimensions>();
        Invoke((NativeError* error) => NativeMethods.Dimensions(handle, ref value, ref *error));
        return value;
    }

    internal static NativeSensorIdentity Sensor(ulong handle)
    {
        NativeSensorIdentity value = Init<NativeSensorIdentity>();
        Invoke((NativeError* error) => NativeMethods.Sensor(handle, ref value, ref *error));
        return value;
    }

    internal static NativeMetadata Metadata(ulong handle)
    {
        NativeMetadata value = Init<NativeMetadata>();
        Invoke((NativeError* error) => NativeMethods.Metadata(handle, ref value, ref *error));
        return value;
    }

    internal static (NativeStatus Status, NativeCameraFacts Value) CameraFacts(ulong handle)
    {
        NativeCameraFacts value = Init<NativeCameraFacts>();
        var status = Invoke((NativeError* error) => NativeMethods.CameraFacts(handle,
            ref value, ref *error));
        return ((NativeStatus)status, value);
    }

    internal static (NativeStatus Status, NativeFujiFacts Value) FujiFacts(ulong handle)
    {
        NativeFujiFacts value = Init<NativeFujiFacts>();
        var status = Invoke((NativeError* error) => NativeMethods.FujiFacts(handle,
            ref value, ref *error));
        return ((NativeStatus)status, value);
    }

    internal static NativeMosaicDescriptor BorrowMosaic(ulong handle, out NativeStatus outcome)
    {
        NativeMosaicDescriptor value = Init<NativeMosaicDescriptor>();
        var status = Invoke((NativeError* error) => NativeMethods.BorrowMosaic(handle,
            ref value, ref *error));
        outcome = (NativeStatus)status;
        return value;
    }

    internal static void Configure(ulong handle, LibRawOutputConfiguration configuration)
    {
        configuration.Validate();
        var value = ToNative(configuration);
        Invoke((NativeError* error) => NativeMethods.Configure(handle, ref value, ref *error));
    }

    internal static void Unpack(ulong handle) =>
        Invoke(error => NativeMethods.Unpack(handle, ref *error));
    internal static void Recycle(ulong handle) =>
        Invoke(error => NativeMethods.Recycle(handle, ref *error));
    internal static void Process(ulong handle) =>
        Invoke(error => NativeMethods.Process(handle, ref *error));
    internal static void UnpackThumbnail(ulong handle) =>
        Invoke(error => NativeMethods.UnpackThumbnail(handle, ref *error));

    internal static NativeImageDescriptor MakeThumbnail(ulong handle)
    {
        NativeImageDescriptor value = Init<NativeImageDescriptor>();
        Invoke((NativeError* error) => NativeMethods.MakeThumbnail(handle, ref value, ref *error));
        return value;
    }

    internal static NativeImageDescriptor MakeProcessed(ulong handle)
    {
        NativeImageDescriptor value = Init<NativeImageDescriptor>();
        Invoke((NativeError* error) => NativeMethods.MakeProcessed(handle, ref value, ref *error));
        return value;
    }

    internal static void ReleaseMosaic(ulong lease) =>
        Invoke(error => NativeMethods.ReleaseMosaic(lease, ref *error));
    internal static void FreeImage(ulong allocation) =>
        Invoke(error => NativeMethods.FreeImage(allocation, ref *error));

    internal static void CloseNoThrow(ulong handle)
    {
        Span<byte> text = stackalloc byte[ErrorCapacity];
        fixed (byte* pointer = text)
        {
            var error = Error(pointer);
            _ = NativeMethods.Close(handle, ref error);
        }
    }

    private unsafe delegate int NativeAction(NativeError* error);

    private static int Invoke(NativeAction action)
    {
        Span<byte> text = stackalloc byte[ErrorCapacity];
        fixed (byte* pointer = text)
        {
            var error = Error(pointer);
            var status = action(&error);
            Check(status, error, text);
            return status;
        }
    }

    private static NativeOutputConfig ToNative(LibRawOutputConfiguration value)
    {
        NativeOutputConfig native = Init<NativeOutputConfig>();
        native.OutputBits = value.OutputBits;
        native.OutputColor = value.OutputColor;
        native.GammaPower = value.GammaPower;
        native.GammaSlope = value.GammaSlope;
        native.NoAutoBright = value.NoAutoBright ? 1 : 0;
        native.HalfSize = value.HalfSize ? 1 : 0;
        native.HighlightMode = value.HighlightMode;
        native.FbddNoiseReduction = value.FbddNoiseReduction;
        native.UseCameraWhiteBalance = value.UseCameraWhiteBalance ? 1 : 0;
        native.UseAutoWhiteBalance = value.UseAutoWhiteBalance ? 1 : 0;
        native.UserMultipliers[0] = value.UserMultiplier0;
        native.UserMultipliers[1] = value.UserMultiplier1;
        native.UserMultipliers[2] = value.UserMultiplier2;
        native.UserMultipliers[3] = value.UserMultiplier3;
        native.UseCameraMatrix = value.UseCameraMatrix ? 1 : 0;
        return native;
    }

    private static T Init<T>() where T : unmanaged
    {
        var value = default(T);
        var words = (uint*)&value;
        words[0] = Abi;
        words[1] = (uint)Unsafe.SizeOf<T>();
        return value;
    }

    private static NativeError Error(byte* text) => new()
    {
        AbiVersion = Abi,
        StructSize = (uint)Unsafe.SizeOf<NativeError>(),
        Text = text,
        TextCapacity = ErrorCapacity
    };

    private static void Check(int status, NativeError error, Span<byte> buffer)
    {
        var length = checked((int)Math.Min(error.TextLength, (uint)buffer.Length));
        NativeErrorMapper.Throw(status, (NativeErrorClass)error.ErrorClass,
            error.NativeCode, buffer[..length]);
    }
}
