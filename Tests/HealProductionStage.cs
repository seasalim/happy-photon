using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

// Test fixture adapter only: all pixel work belongs to the production stage.
internal sealed class HealProductionStage
{
    private readonly RenderRepairs stage = new();
    internal long RetainedScratchBytes => stage.RetainedScratchBytes;

    internal BaseImage Repair(BaseImage basis, IReadOnlyList<HealSpot> spots, HealCandidate candidate, int workers = 2)
    {
        var copy = new MagickImage(basis.Pixels);
        try { Apply(copy, spots, candidate, workers); return new(copy, basis.Info); }
        catch { copy.Dispose(); throw; }
    }

    internal void Apply(MagickImage image, IReadOnlyList<HealSpot> spots, HealCandidate candidate, int workers)
    {
        Assert.Equal(default, candidate); // Only the FINAL Membrane-additive contract ships.
        stage.Apply(image, HealWorkloads.Repairs(spots), RenderExecutionOptions.Resting(CancellationToken.None, workers));
    }

    internal static double BoundaryMean(ushort[] pixels, int width, int height,
        double x, double y, double halfWidth, int channel) =>
        new RepairBoundaryRows(pixels, width, height, 3, new double[width * height * 3], null)
            .Mean(x, y, halfWidth, channel);
}
