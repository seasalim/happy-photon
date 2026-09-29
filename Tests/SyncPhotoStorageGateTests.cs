using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncPhotoStorageGateTests(ITestOutputHelper output)
{
    [Fact]
    public async Task G2_BCap()
    {
        SyncPhotoGateSupport.RequirePerformance();
        SyncPhotoBrushFixture.Create();
        // The gate names the storage script: 96 strokes, exactly 4,000 points,
        // seed 271100. This is distinct from the 3,936-point render BCap.
        await new LocalsBrushCatalogGrowthTests(output).BrushHistoryAtCap(false);
    }
}

