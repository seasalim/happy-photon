using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BuiltInLookMonochromeTests
{
    [Fact]
    public void EveryShippedLookKeepsTrueMonochromeRawExactlyNeutral()
    {
        var path = FinishingFollowOn.PhotoPaths().FirstOrDefault(path =>
            Path.GetFileName(path).Equals("m2462362.DNG", StringComparison.OrdinalIgnoreCase));
        Assert.SkipWhen(path == null, "Set HAPPY_PHOTON_LOOKS_MONOCHROME_FIXTURE to the reviewed local RAW fixture.");
        using var basis = FinishingGateSupport.Loader().LoadPreviewBase(
            FinishingGateSupport.LocalFile(path!), BaseDecodeSettings.Default, default);
        Assert.NotNull(basis);
        Assert.True(basis.Info.IsMonochrome);
        Assert.True(basis.Info.IsRawSource);

        foreach (var look in new PresetService().BuiltInPresets)
        {
            var settings = new EditSettings();
            EditSettingsLook.Apply(look.Settings, settings);
            using var render = new RenderPipeline().Render(new(basis, settings,
                RenderIntent.Preview, 1600, new(false, false)));
            var pixels = RenderPipelineTestSupport.ReadPixels(render.Image);

            for (var index = 0; index < pixels.Length; index += 3)
            {
                Assert.Equal(pixels[index], pixels[index + 1]);
                Assert.Equal(pixels[index], pixels[index + 2]);
            }
        }
    }
}
