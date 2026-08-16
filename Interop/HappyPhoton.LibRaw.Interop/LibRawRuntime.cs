namespace HappyPhoton.LibRaw.Interop;

public sealed record LibRawRuntime(
    uint BridgeAbiVersion,
    uint LibRawVersionNumber,
    string LibRawVersion,
    uint Capabilities,
    bool IsThreadSafeVariant);

internal interface ILibRawNativeObservations
{
    uint ReadBridgeAbiVersion();
    LibRawRuntime ReadRuntime();
}

internal static class AbiHandshake
{
    private sealed record HandshakeState(
        LibRawRuntimeHealth Health,
        LibRawRuntime? Runtime);

    private sealed class NativeObservations : ILibRawNativeObservations
    {
        public uint ReadBridgeAbiVersion() => NativeMethods.AbiVersion();
        public LibRawRuntime ReadRuntime() => NativeApi.Runtime();
    }

    private static readonly Lazy<HandshakeState> State = new(
        () => Observe(new NativeObservations()),
        LazyThreadSafetyMode.ExecutionAndPublication);

    internal static LibRawRuntime Ensure()
    {
        var state = State.Value;
        return state.Runtime ?? throw new LibRawDeploymentException(state.Health);
    }

    internal static LibRawRuntimeHealth Probe() => State.Value.Health;

    internal static LibRawRuntimeHealth Probe(ILibRawNativeObservations native) =>
        Observe(native).Health;

    private static HandshakeState Observe(ILibRawNativeObservations native)
    {
        ArgumentNullException.ThrowIfNull(native);
        uint? abi = null;
        try
        {
            abi = native.ReadBridgeAbiVersion();
            var abiHealth = LibRawRuntimeHealthEvaluator.Evaluate(new(
                abi, null, null, null));
            if (!abiHealth.IsHealthy &&
                abiHealth.RejectionReason ==
                LibRawHealthRejectionReason.BridgeAbiMismatch)
            {
                return new(abiHealth, null);
            }

            var runtime = native.ReadRuntime();
            var health = LibRawRuntimeHealthEvaluator.Evaluate(new(
                abi,
                runtime.LibRawVersionNumber,
                runtime.LibRawVersion,
                runtime.Capabilities));
            return new(health, health.IsHealthy ? runtime : null);
        }
        catch (LibRawDeploymentException exception)
        {
            return DeploymentFailure(abi, exception);
        }
        catch (TypeInitializationException exception)
        {
            if (exception.InnerException is LibRawDeploymentException deployment)
            {
                return DeploymentFailure(abi, deployment);
            }

            var wrapped = new LibRawDeploymentException(
                LibRawRuntimeComponent.Bridge,
                LibRawDeploymentStage.Load,
                exception.InnerException?.Message ?? exception.Message,
                exception);
            return DeploymentFailure(abi, wrapped);
        }
        catch (Exception exception) when (exception is DllNotFoundException or
            EntryPointNotFoundException or BadImageFormatException or
            LibRawBridgeException or LibRawProgrammingException)
        {
            var wrapped = new LibRawDeploymentException(
                LibRawRuntimeComponent.Bridge,
                abi == null ? LibRawDeploymentStage.Load : LibRawDeploymentStage.RuntimeQuery,
                exception.Message,
                exception);
            return DeploymentFailure(abi, wrapped);
        }
    }

    private static HandshakeState DeploymentFailure(
        uint? abi,
        LibRawDeploymentException exception)
    {
        var observations = new LibRawRuntimeObservations(
            abi,
            null,
            null,
            null,
            exception.Component ?? LibRawRuntimeComponent.Bridge,
            exception.Stage ?? (abi == null
                ? LibRawDeploymentStage.Load
                : LibRawDeploymentStage.RuntimeQuery),
            exception.Message);
        return new(LibRawRuntimeHealthEvaluator.Evaluate(observations), null);
    }
}
