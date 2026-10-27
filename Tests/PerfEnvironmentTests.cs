using Xunit;

namespace HappyPhoton.Tests;

public sealed class PerfEnvironmentTests(ITestOutputHelper output)
{
    [Fact]
    public void CappedHostIsNotFullCpu()
    {
        var processorCap = Environment.GetEnvironmentVariable("DOTNET_PROCESSOR_COUNT");
        output.WriteLine($"Environment.ProcessorCount={Environment.ProcessorCount}; " +
            $"DOTNET_PROCESSOR_COUNT={processorCap ?? "<unset>"}; IsFullCpu={PerfEnvironment.IsFullCpu}");

        if (!string.IsNullOrEmpty(processorCap))
        {
            Assert.False(PerfEnvironment.IsFullCpu);
        }
    }
}
