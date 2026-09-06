using Xunit;

namespace HappyPhoton.Tests;

internal static class ConstructionBaselineAssert
{
    internal static string Root => GoldenTestPaths.RepositoryRoot;
    internal static void Match(string name, IEnumerable<string> observations)
    {
        var actual = string.Join("\n", observations) + "\n";
        var expected = name switch
        {
            "render" => ConstructionBaselineGoldens.render,
            "migration" => ConstructionBaselineGoldens.migration,
            "warnings" => ConstructionBaselineGoldens.warnings,
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        Assert.Equal(expected.Replace("\r\n", "\n") + "\n", actual);
    }
}
