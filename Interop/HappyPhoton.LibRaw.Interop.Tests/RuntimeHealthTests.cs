using Xunit;

namespace HappyPhoton.LibRaw.Interop.Tests;

public sealed class RuntimeHealthTests
{
    [Fact]
    public void Evaluate_AcceptsOnlyPinnedAbiVersionAndCapabilities()
    {
        var observed = HealthyObservations(versionText: "diagnostic-only");

        var health = LibRawRuntimeHealthEvaluator.Evaluate(observed);

        Assert.True(health.IsHealthy);
        Assert.Null(health.RejectionReason);
        Assert.Same(observed, health.Observations);
    }

    [Theory]
    [MemberData(nameof(Rejections))]
    public void Evaluate_RejectsEachPolicyFailureWithObservedFacts(
        LibRawRuntimeObservations observed,
        LibRawHealthRejectionReason reason,
        LibRawRuntimeComponent component)
    {
        var health = LibRawRuntimeHealthEvaluator.Evaluate(observed);

        Assert.False(health.IsHealthy);
        Assert.Equal(reason, health.RejectionReason);
        Assert.Equal(component, health.RejectedComponent);
        Assert.Same(observed, health.Observations);
    }

    [Fact]
    public void Probe_AbiMismatchDoesNotReadVersionedRuntimeStructure()
    {
        var native = new StubNativeObservations(
            bridgeAbi: LibRawOutputConfiguration.Version + 1,
            HealthyRuntime());

        var health = AbiHandshake.Probe(native);

        Assert.Equal(
            LibRawHealthRejectionReason.BridgeAbiMismatch,
            health.RejectionReason);
        Assert.Equal(0, native.RuntimeCalls);
        Assert.Null(health.Observations.LibRawVersionNumber);
        Assert.Null(health.Observations.Capabilities);
        Assert.Contains("LibRaw version=not observed", health.DiagnosticText);
        Assert.Contains("capability mask=not observed", health.DiagnosticText);
    }

    [Theory]
    [MemberData(nameof(RuntimeRejections))]
    public void Probe_PropagatesRuntimeFactsThroughPolicyAndDiagnostic(
        LibRawRuntime runtime,
        LibRawHealthRejectionReason expectedReason)
    {
        var native = new StubNativeObservations(
            LibRawOutputConfiguration.Version,
            runtime);

        var health = AbiHandshake.Probe(native);

        Assert.Equal(expectedReason, health.RejectionReason);
        Assert.Equal(1, native.RuntimeCalls);
        Assert.Equal(runtime.LibRawVersionNumber, health.Observations.LibRawVersionNumber);
        Assert.Equal(runtime.Capabilities, health.Observations.Capabilities);
        Assert.Contains($"0x{runtime.LibRawVersionNumber:X6}", health.DiagnosticText);
        Assert.Contains($"0x{runtime.Capabilities:X8}", health.DiagnosticText);
    }

    public static TheoryData<
        LibRawRuntimeObservations,
        LibRawHealthRejectionReason,
        LibRawRuntimeComponent> Rejections() => new()
    {
        {
            HealthyObservations() with { BridgeAbiVersion = 2 },
            LibRawHealthRejectionReason.BridgeAbiMismatch,
            LibRawRuntimeComponent.Bridge
        },
        {
            HealthyObservations() with { LibRawVersionNumber = 0x001601 },
            LibRawHealthRejectionReason.LibRawVersionMismatch,
            LibRawRuntimeComponent.LibRawCompanion
        },
        {
            HealthyObservations() with { Capabilities = LibRawCapabilities.Zlib },
            LibRawHealthRejectionReason.MissingJpegCapability,
            LibRawRuntimeComponent.LibRawCompanion
        },
        {
            HealthyObservations() with { Capabilities = LibRawCapabilities.Jpeg },
            LibRawHealthRejectionReason.MissingZlibCapability,
            LibRawRuntimeComponent.LibRawCompanion
        },
        {
            new(null, null, null, null,
                LibRawRuntimeComponent.Bridge,
                LibRawDeploymentStage.Load,
                "bad image"),
            LibRawHealthRejectionReason.DeploymentFailure,
            LibRawRuntimeComponent.Bridge
        },
    };

    public static TheoryData<LibRawRuntime, LibRawHealthRejectionReason>
        RuntimeRejections() => new()
        {
            {
                HealthyRuntime() with { LibRawVersionNumber = 0x001601 },
                LibRawHealthRejectionReason.LibRawVersionMismatch
            },
            {
                HealthyRuntime() with { Capabilities = LibRawCapabilities.Zlib },
                LibRawHealthRejectionReason.MissingJpegCapability
            },
            {
                HealthyRuntime() with { Capabilities = LibRawCapabilities.Jpeg },
                LibRawHealthRejectionReason.MissingZlibCapability
            },
        };

    private static LibRawRuntimeObservations HealthyObservations(
        string versionText = "0.22.2-Release") => new(
        LibRawOutputConfiguration.Version,
        LibRawRuntimeHealthEvaluator.SupportedLibRawVersion,
        versionText,
        LibRawCapabilities.Jpeg | LibRawCapabilities.Zlib);

    private static LibRawRuntime HealthyRuntime() => new(
        LibRawOutputConfiguration.Version,
        LibRawRuntimeHealthEvaluator.SupportedLibRawVersion,
        "0.22.2-Release",
        LibRawCapabilities.Jpeg | LibRawCapabilities.Zlib,
        true);

    private sealed class StubNativeObservations(
        uint bridgeAbi,
        LibRawRuntime runtime) : ILibRawNativeObservations
    {
        public int RuntimeCalls { get; private set; }

        public uint ReadBridgeAbiVersion() => bridgeAbi;

        public LibRawRuntime ReadRuntime()
        {
            RuntimeCalls++;
            return runtime;
        }
    }
}
