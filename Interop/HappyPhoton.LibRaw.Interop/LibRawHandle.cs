using Microsoft.Win32.SafeHandles;

namespace HappyPhoton.LibRaw.Interop;

internal sealed class LibRawHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal LibRawHandle(ulong token) : base(true)
    {
        SetHandle(unchecked((nint)token));
    }

    internal ulong Token => unchecked((ulong)(nuint)DangerousGetHandle());

    protected override bool ReleaseHandle()
    {
        NativeApi.CloseNoThrow(unchecked((ulong)(nuint)handle));
        return true;
    }
}
