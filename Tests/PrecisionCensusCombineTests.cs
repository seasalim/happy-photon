using Xunit;

namespace HappyPhoton.Tests;

public sealed class PrecisionCensusCombineTests
{
    [Fact]
    public void CombineTwoDeclaredRunArtifacts_WhenRequested()
    {
        var gate = Environment.GetEnvironmentVariable(
            "HAPPY_PHOTON_PRECISION_CENSUS_COMBINE");
        if (string.IsNullOrEmpty(gate))
        {
            return;
        }
        Assert.Equal("1", gate);
        var firstPath = RequirePath("HAPPY_PHOTON_PRECISION_CENSUS_RUN_1");
        var secondPath = RequirePath("HAPPY_PHOTON_PRECISION_CENSUS_RUN_2");
        var manifest = PrecisionCensusManifest.Load();
        var result = PrecisionCensusCombiner.Combine(
            File.ReadAllBytes(firstPath),
            File.ReadAllBytes(secondPath),
            manifest.ExpectedCases);
        var outputPath = Environment.GetEnvironmentVariable(
            "HAPPY_PHOTON_PRECISION_CENSUS_TERMINAL");
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var fullPath = Path.GetFullPath(
                outputPath,
                GoldenTestPaths.RepositoryRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, result.Statement + "\n");
        }
        TestContext.Current.TestOutputHelper?.WriteLine(result.Statement);
    }

    private static string RequirePath(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Set {name}.");
        var fullPath = Path.GetFullPath(value!, GoldenTestPaths.RepositoryRoot);
        Assert.True(File.Exists(fullPath), $"Artifact does not exist: {fullPath}");
        return fullPath;
    }
}
