using System.Runtime.CompilerServices;
using Xunit;

namespace HappyPhoton.Tests;

internal static class ConstructionBaselineAssert
{
    internal static string Root => Path.GetDirectoryName(Path.GetDirectoryName(Source()))!;
    private static string Source([CallerFilePath] string path = "") => path;
    internal static void Match(string name, IEnumerable<string> observations)
    {
        var actual = string.Join("\n", observations) + "\n";
        var expected = name switch
        {
            "render" => ConstructionBaselineGoldens.render,
            "transfer" => ConstructionBaselineGoldens.transfer,
            "migration" => ConstructionBaselineGoldens.migration,
            "warnings" => ConstructionBaselineGoldens.warnings,
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        Assert.Equal(expected.Replace("\r\n", "\n") + "\n", actual);
    }
}
