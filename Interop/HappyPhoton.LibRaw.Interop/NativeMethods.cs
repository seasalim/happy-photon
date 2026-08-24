using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HappyPhoton.LibRaw.Interop;

internal static unsafe partial class NativeMethods
{
    internal const string LibraryName = "happyphoton_libraw_bridge";

    [LibraryImport(LibraryName, EntryPoint = "hplr_abi_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint AbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "hplr_runtime")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Runtime(ref NativeRuntimeInfo value, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_open_utf8")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Open(byte* path, uint length, out ulong handle, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_close")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Close(ulong handle, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_unpack")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Unpack(ulong handle, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_recycle")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Recycle(ulong handle, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_get_dimensions")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Dimensions(ulong handle, ref NativeDimensions value, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_get_sensor_identity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Sensor(ulong handle, ref NativeSensorIdentity value, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_get_metadata")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Metadata(ulong handle, ref NativeMetadata value, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_get_camera_facts")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int CameraFacts(ulong handle, ref NativeCameraFacts value, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_get_fuji_facts")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int FujiFacts(ulong handle, ref NativeFujiFacts value, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_get_lens_identity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int LensIdentity(ulong handle, ref NativeLensIdentity value, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_borrow_mosaic")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int BorrowMosaic(ulong handle, ref NativeMosaicDescriptor value, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_release_mosaic")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReleaseMosaic(ulong lease, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_unpack_thumbnail")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int UnpackThumbnail(ulong handle, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_make_thumbnail")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int MakeThumbnail(ulong handle, ref NativeImageDescriptor value, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_configure_output")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Configure(ulong handle, ref NativeOutputConfig value, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_process")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Process(ulong handle, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_make_processed_image")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int MakeProcessed(ulong handle, ref NativeImageDescriptor value, ref NativeError error);

    [LibraryImport(LibraryName, EntryPoint = "hplr_free_image")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int FreeImage(ulong allocation, ref NativeError error);
}
