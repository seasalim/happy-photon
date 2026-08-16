namespace HappyPhoton.LibRaw.Interop;

public sealed record LibRawRuntime(
    uint BridgeAbiVersion,
    uint LibRawVersionNumber,
    string LibRawVersion,
    uint Capabilities,
    bool IsThreadSafeVariant);

internal static class AbiHandshake
{
    private static readonly Lazy<LibRawRuntime> Runtime = new(Load,
        LazyThreadSafetyMode.ExecutionAndPublication);

    internal static LibRawRuntime Ensure() => Runtime.Value;

    private static LibRawRuntime Load()
    {
        try
        {
            var abi = NativeMethods.AbiVersion();
            if (abi != LibRawOutputConfiguration.Version)
                throw new LibRawDeploymentException(
                    $"Bridge ABI {abi} is incompatible with ABI {LibRawOutputConfiguration.Version}.");
            var runtime = NativeApi.Runtime();
            if (runtime.BridgeAbiVersion != abi || runtime.LibRawVersionNumber != 0x001602)
                throw new LibRawDeploymentException(
                    $"Expected bridge ABI 1 with LibRaw 0.22.2, observed ABI " +
                    $"{runtime.BridgeAbiVersion} and LibRaw 0x{runtime.LibRawVersionNumber:x6}.");
            return runtime;
        }
        catch (LibRawDeploymentException) { throw; }
        catch (Exception exception) when (exception is DllNotFoundException or
            EntryPointNotFoundException or BadImageFormatException)
        {
            throw new LibRawDeploymentException(
                "The Happy Photon LibRaw bridge could not be loaded.", exception);
        }
    }
}
