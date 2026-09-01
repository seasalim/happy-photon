namespace HappyPhoton.Tests;

internal static class PerfEnvironment
{
    // Budgets are calibrated on the full-width host; the default test host pins
    // DOTNET_PROCESSOR_COUNT=2 (HappyPhoton.runsettings) and inflates parallel
    // kernels 2-4x, so a capped measurement must fail rather than mislead.
    internal static void AssertFullCpu()
    {
        if (Environment.ProcessorCount <= 2)
        {
            throw new InvalidOperationException(
                "Performance budgets require the full-width test host: set " +
                "HAPPY_PHOTON_FULL_CPU=1 (the default host caps " +
                "DOTNET_PROCESSOR_COUNT at 2).");
        }
    }
}
