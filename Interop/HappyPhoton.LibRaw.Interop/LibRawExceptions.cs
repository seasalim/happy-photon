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

    internal LibRawDeploymentException(
        LibRawRuntimeComponent component,
        LibRawDeploymentStage stage,
        string message,
        Exception? inner = null) : base(message, inner)
    {
        Component = component;
        Stage = stage;
    }

    internal LibRawDeploymentException(LibRawRuntimeHealth health)
        : base(health.DiagnosticText)
    {
        Component = health.RejectedComponent;
        Stage = health.Observations.FailureStage;
        Health = health;
    }

    public LibRawRuntimeComponent? Component { get; }
    public LibRawDeploymentStage? Stage { get; }
    public LibRawRuntimeHealth? Health { get; }
}

public sealed class LibRawProgrammingException : InvalidOperationException
{
    internal LibRawProgrammingException(string message) : base(message) { }
}

public sealed class LibRawBridgeException : Exception
{
    internal LibRawBridgeException(string message) : base(message) { }
}
