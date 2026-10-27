namespace HappyPhoton.Tests;

internal static class PerfEnvironment
{
    internal const int OrdinaryProcessorCap = 8;

    internal static bool IsFullCpu =>
        Environment.GetEnvironmentVariable("HAPPY_PHOTON_FULL_CPU") == "1" &&
        Environment.ProcessorCount > OrdinaryProcessorCap;

    // Budgets require an explicitly selected full-width host; capped measurements mislead.
    internal static void AssertFullCpu()
    {
        if (!IsFullCpu)
        {
            throw new InvalidOperationException(
                "Performance budgets require the full-width test host: set " +
                "HAPPY_PHOTON_FULL_CPU=1 (the default host caps " +
                $"DOTNET_PROCESSOR_COUNT at {OrdinaryProcessorCap}).");
        }
    }
}
