using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class HealGateTests
{
    [Fact]
    public void RepairStageProbeDiagnostic()
    {
        OptIn();
        using var pair = Loader().LoadPreviewBaseWithOutcome(
            LocalFile(), BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair);
        var basis = pair.Interactive;
        var pipeline = new RenderPipeline();

        foreach (var workload in new[] { "S64", "SArea64" })
        {
            var settings = HealWorkloads.LH8();
            settings.Repairs = HealWorkloads.Repairs(workload == "S64" ? HealWorkloads.S64()
                : HealWorkloads.SArea64((int)basis.Pixels.Width, (int)basis.Pixels.Height));
            var samples = new List<double>();
            using var listener = RenderStageProbe.Listen(sample =>
            {
                if (sample.Stage == "repairs") samples.Add(sample.Elapsed.TotalMilliseconds);
            });

            for (var i = 0; i < 17; i++)
            {
                using var render = pipeline.Render(new(basis, settings, RenderIntent.Preview,
                    1600, new(false, false)));
            }

            Assert.Equal(17, samples.Count);
            var measured = samples.Skip(10).ToArray();
            Report("repair-stage-probe", new { diagnostic = true, workload, size = Size(basis),
                tickSize = 1600, warmup = 10, samples = measured, medianMs = Median(measured) });
        }
    }
}
