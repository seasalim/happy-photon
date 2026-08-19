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
            ["HAPPY_PHOTON_DEBUG"] = "true",
            ["HAPPY_PHOTON_DISPLAY_TRACE"] = "1"
        };

        var flags = ImageServiceHelpers.ReadDiagnosticFlags(
            name => variables.GetValueOrDefault(name));

        Assert.True(flags.Perf);
        Assert.True(flags.Debug);
        Assert.True(flags.DisplayTrace);
    }

    [Fact]
    public void ReadDiagnosticFlags_PhotoEditVariablesHaveNoEffect()
    {
        var variables = new Dictionary<string, string?>
        {
            ["PHOTOEDIT_PERF"] = "1",
            ["PHOTOEDIT_DEBUG"] = "true",
            ["PHOTOEDIT_DISPLAY_TRACE"] = "1"
        };

        var flags = ImageServiceHelpers.ReadDiagnosticFlags(
            name => variables.GetValueOrDefault(name));

        Assert.False(flags.Perf);
        Assert.False(flags.Debug);
        Assert.False(flags.DisplayTrace);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("true")]
    [InlineData("0")]
    public void ReadDiagnosticFlags_DisplayTraceRequiresExactOne(string? value)
    {
        var flags = ImageServiceHelpers.ReadDiagnosticFlags(
            name => name == "HAPPY_PHOTON_DISPLAY_TRACE" ? value : null);

        Assert.False(flags.DisplayTrace);
    }
}
