using Xunit;

namespace HappyPhoton.Tests;

public sealed class DataLocationStartupOrderTests
{
    [Fact]
    public void StartupWiring_ExecutesMoveBeforeResolveAndCatalogBeforePresets()
    {
        var source = File.ReadAllText(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "Views",
            "MainWindow.DataContext.cs"));

        AssertOrdered(
            source,
            "_locationMigrator.ExecutePendingAsync()",
            "_dataLocationService.ResolveAsync()",
            "_startupCatalogService.InitializeAsync(_startupLocations)",
            "vm.InitializeAsync(_startupLocations)");
    }

    [Fact]
    public void CatalogSchema_RunsMigrationsBeforeValidation()
    {
        var source = File.ReadAllText(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "Services",
            "CatalogSchema.cs"));

        AssertOrdered(
            source,
            "CreateTablesAsync(connection)",
            "CatalogMigrations.RunAsync(connection)",
            "ValidateImageSchemaAsync(connection)",
            "ValidateAssessmentSchemaAsync(connection)");
    }

    [Fact]
    public void Architecture_PinsFullInitializationOrder()
    {
        var architecture = File.ReadAllText(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "docs",
            "ARCHITECTURE.md"));

        AssertOrdered(
            architecture,
            "pending journaled move",
            "open the shared catalog connection",
            "create tables",
            "run ordered catalog migrations",
            "validate the resulting schema",
            "cache/catalog pairing stamp");
    }

    private static void AssertOrdered(string text, params string[] values)
    {
        var last = -1;
        foreach (var value in values)
        {
            var next = text.IndexOf(value, StringComparison.Ordinal);
            Assert.True(next > last, $"Expected '{value}' after offset {last}.");
            last = next;
        }
    }
}
