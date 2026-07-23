using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AgentTokenTests
{
    [Fact]
    public void Generate_Produces32UrlSafeChars()
    {
        var token = AgentAccessToken.Generate();

        Assert.Equal(32, token.Length);
        Assert.Matches("^[A-Za-z0-9]+$", token);
    }

    [Fact]
    public void Generate_ProducesUniqueValues()
    {
        Assert.NotEqual(AgentAccessToken.Generate(), AgentAccessToken.Generate());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("short", false)]
    [InlineData("1234567890123456789012345678901/", false)]
    [InlineData("12345678901234567890123456789012", true)]
    public void IsValid_AcceptsOnly32UrlSafeCharacters(string? token, bool expected)
    {
        Assert.Equal(expected, AgentAccessToken.IsValid(token));
    }
}
