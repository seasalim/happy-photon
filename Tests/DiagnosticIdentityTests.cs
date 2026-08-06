using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DiagnosticIdentityTests
{
    [Fact]
    public void ReadDiagnosticFlags_HappyPhotonVariablesEnableDiagnostics()
    {
        var variables = new Dictionary<string, string?>
        {
            ["HAPPY_PHOTON_PERF"] = "1",
            ["HAPPY_PHOTON_DEBUG"] = "true"
        };

        var flags = ImageServiceHelpers.ReadDiagnosticFlags(
            name => variables.GetValueOrDefault(name));

        Assert.True(flags.Perf);
        Assert.True(flags.Debug);
    }

    [Fact]
    public void ReadDiagnosticFlags_PhotoEditVariablesHaveNoEffect()
    {
        var variables = new Dictionary<string, string?>
        {
            ["PHOTOEDIT_PERF"] = "1",
            ["PHOTOEDIT_DEBUG"] = "true"
        };

        var flags = ImageServiceHelpers.ReadDiagnosticFlags(
            name => variables.GetValueOrDefault(name));

        Assert.False(flags.Perf);
        Assert.False(flags.Debug);
    }
}
