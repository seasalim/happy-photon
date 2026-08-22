using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class McpExportToolTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("mcp-export");

    [Fact]
    public async Task ExportTool_DescribesSixteenBitLosslessProfiledTiff()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var viewModel = _fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask);
        await using var imageService = new ImageService(catalog);
        var service = new AgentToolService(viewModel, imageService, catalog);
        var createTools = typeof(McpServerHost).GetMethod(
            "CreateTools",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic);
        var tools = Assert.IsAssignableFrom<
            ModelContextProtocol.Server.McpServerTool[]>(
            createTools!.Invoke(null, [service]));

        var description = tools.Single(tool =>
            tool.ProtocolTool.Name == "export_images").ProtocolTool.Description!;
        Assert.Contains("16-bit", description);
        Assert.Contains("lossless", description);
        Assert.Contains("ICC", description);
    }

    public void Dispose() => _fixture.Dispose();
}
