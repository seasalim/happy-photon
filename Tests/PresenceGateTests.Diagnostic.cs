using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class PresenceGateTests
{
    [Fact]
    public void SchedulingDiagnostic()
    {
        OptIn();
        using var pair = Loader().LoadPreviewBaseWithOutcome(LocalFile(), BaseDecodeSettings.Default, default).Pair;
        Assert.NotNull(pair);
        var settings = new EditSettings { Detail = new() { CaptureSharpen = 0 } };
        using var source = new RenderPipeline().RenderDisplayRec2020(
            new(pair.Interactive, settings, RenderIntent.Preview, null, new(false, false)));
        var prototype = new List<double>();
        var production = new List<double>();

        for (var round = -10; round < 9; round++)
        foreach (var port in round % 2 == 0 ? new[] { false, true } : new[] { true, false })
        {
            using var image = new MagickImage(source);
            var elapsed = Time(() =>
            {
                if (port)
                {
                    RenderPresence.Apply(image, pair.Interactive.Info, new() { Texture = 60 });
                }
                else
                {
                    OpsPresencePrototype.Apply(image, pair.Interactive.Info, new("TX+", 60),
                        OpsClarity.Guided, workers: Environment.ProcessorCount);
                }
            });

            if (round >= 0) (port ? production : prototype).Add(elapsed);
        }

        Report("scheduling-diagnostic", new { prototype = Median(prototype), production = Median(production) });
    }
}
