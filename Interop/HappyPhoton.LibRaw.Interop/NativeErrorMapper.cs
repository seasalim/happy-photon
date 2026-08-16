using System.Text;

namespace HappyPhoton.LibRaw.Interop;

internal static class NativeErrorMapper
{
    internal static void Throw(int status, NativeErrorClass errorClass, int nativeCode,
        ReadOnlySpan<byte> text)
    {
        if (status >= 0) return;
        var message = text.IsEmpty ? $"Native bridge failed with status {status}."
            : Encoding.UTF8.GetString(text);
        throw errorClass switch
        {
            NativeErrorClass.LibRaw => new LibRawDecodeException(nativeCode, message),
            NativeErrorClass.Abi => new LibRawDeploymentException(message),
            NativeErrorClass.Programming => new LibRawProgrammingException(message),
            NativeErrorClass.Bridge => new LibRawBridgeException(message),
            _ => new LibRawBridgeException($"Unclassified native failure {status}: {message}")
        };
    }
}
