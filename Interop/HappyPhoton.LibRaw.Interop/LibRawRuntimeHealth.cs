using System.Globalization;

namespace HappyPhoton.LibRaw.Interop;

public enum LibRawRuntimeComponent
{
    Bridge,
    LibRawCompanion,
}

public enum LibRawDeploymentStage
{
    Resolution,
    Load,
    RuntimeQuery,
}

public enum LibRawHealthRejectionReason
{
    DeploymentFailure,
    BridgeAbiMismatch,
    LibRawVersionMismatch,
    MissingJpegCapability,
    MissingZlibCapability,
}

public static class LibRawCapabilities
{
    // LibRaw::capabilities() contract from libraw_const.h.
    public const uint Zlib = 0x40;
    public const uint Jpeg = 0x80;
}

public sealed record LibRawRuntimeObservations(
    uint? BridgeAbiVersion,
    uint? LibRawVersionNumber,
    string? LibRawVersion,
    uint? Capabilities,
    LibRawRuntimeComponent? FailedComponent = null,
    LibRawDeploymentStage? FailureStage = null,
    string? FailureDetail = null);

public sealed record LibRawRuntimeHealth(
    bool IsHealthy,
    LibRawHealthRejectionReason? RejectionReason,
    LibRawRuntimeComponent? RejectedComponent,
    LibRawRuntimeObservations Observations)
{
    public string DiagnosticText => LibRawRuntimeHealthEvaluator.FormatDiagnostic(this);
}

public static class LibRawRuntimeHealthEvaluator
{
    public const uint SupportedLibRawVersion = 0x001602;

    public static LibRawRuntimeHealth Evaluate(LibRawRuntimeObservations observed)
    {
        ArgumentNullException.ThrowIfNull(observed);

        if (observed.FailedComponent is { } failedComponent)
        {
            return Rejected(
                LibRawHealthRejectionReason.DeploymentFailure,
                failedComponent,
                observed);
        }

        if (observed.BridgeAbiVersion != LibRawOutputConfiguration.Version)
        {
            return Rejected(
                LibRawHealthRejectionReason.BridgeAbiMismatch,
                LibRawRuntimeComponent.Bridge,
                observed);
        }

        if (observed.LibRawVersionNumber != SupportedLibRawVersion)
        {
            return Rejected(
                LibRawHealthRejectionReason.LibRawVersionMismatch,
                LibRawRuntimeComponent.LibRawCompanion,
                observed);
        }

        if (observed.Capabilities is not { } capabilities ||
            (capabilities & LibRawCapabilities.Jpeg) == 0)
        {
            return Rejected(
                LibRawHealthRejectionReason.MissingJpegCapability,
                LibRawRuntimeComponent.LibRawCompanion,
                observed);
        }

        if ((capabilities & LibRawCapabilities.Zlib) == 0)
        {
            return Rejected(
                LibRawHealthRejectionReason.MissingZlibCapability,
                LibRawRuntimeComponent.LibRawCompanion,
                observed);
        }

        return new(true, null, null, observed);
    }

    public static string FormatDiagnostic(LibRawRuntimeHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);
        var facts = health.Observations;
        if (health.IsHealthy)
        {
            return $"Native RAW runtime healthy: {FormatFacts(facts)}.";
        }

        var component = FormatComponent(health.RejectedComponent);
        var reason = FormatReason(health.RejectionReason);
        var stage = facts.FailureStage is { } value
            ? $", stage={value.ToString().ToLowerInvariant()}"
            : string.Empty;
        var detail = string.IsNullOrWhiteSpace(facts.FailureDetail)
            ? string.Empty
            : $" Detail: {facts.FailureDetail}";
        return $"Native RAW runtime rejected: component={component}, reason={reason}{stage}; " +
               $"{FormatFacts(facts)}.{detail}" +
               " Reinstall Happy Photon to restore RAW support; " +
               "RAW decoding is unavailable until the runtime is repaired.";
    }

    private static LibRawRuntimeHealth Rejected(
        LibRawHealthRejectionReason reason,
        LibRawRuntimeComponent component,
        LibRawRuntimeObservations observed) =>
        new(false, reason, component, observed);

    private static string FormatFacts(LibRawRuntimeObservations facts) =>
        $"observed bridge ABI={FormatDecimal(facts.BridgeAbiVersion)}, " +
        $"LibRaw version={FormatHex(facts.LibRawVersionNumber)}, " +
        $"LibRaw version string={FormatText(facts.LibRawVersion)}, " +
        $"capability mask={FormatMask(facts.Capabilities)}";

    private static string FormatComponent(LibRawRuntimeComponent? component) =>
        component switch
        {
            LibRawRuntimeComponent.Bridge => "bridge",
            LibRawRuntimeComponent.LibRawCompanion => "LibRaw companion",
            _ => "not observed",
        };

    private static string FormatReason(LibRawHealthRejectionReason? reason) =>
        reason switch
        {
            LibRawHealthRejectionReason.DeploymentFailure => "deployment failure",
            LibRawHealthRejectionReason.BridgeAbiMismatch => "bridge ABI mismatch",
            LibRawHealthRejectionReason.LibRawVersionMismatch => "LibRaw version mismatch",
            LibRawHealthRejectionReason.MissingJpegCapability => "JPEG capability missing",
            LibRawHealthRejectionReason.MissingZlibCapability => "zlib capability missing",
            _ => "not observed",
        };

    private static string FormatDecimal(uint? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "not observed";

    private static string FormatHex(uint? value) =>
        value is { } observed ? $"0x{observed:X6}" : "not observed";

    private static string FormatMask(uint? value) =>
        value is { } observed ? $"0x{observed:X8}" : "not observed";

    private static string FormatText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "not observed" : value;
}
