namespace HappyPhoton.LibRaw.Interop;

public sealed class LibRawDecodeException : Exception
{
    public LibRawDecodeException(int nativeCode, string nativeText)
        : base($"LibRaw failed ({nativeCode}): {nativeText}")
    {
        NativeCode = nativeCode;
        NativeText = nativeText;
    }

    public int NativeCode { get; }
    public string NativeText { get; }
}

public sealed class LibRawDeploymentException : Exception
{
    internal LibRawDeploymentException(string message) : base(message) { }
    internal LibRawDeploymentException(string message, Exception inner) : base(message, inner) { }
}

public sealed class LibRawProgrammingException : InvalidOperationException
{
    internal LibRawProgrammingException(string message) : base(message) { }
}

public sealed class LibRawBridgeException : Exception
{
    internal LibRawBridgeException(string message) : base(message) { }
}
