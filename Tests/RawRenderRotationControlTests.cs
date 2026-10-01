using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;
using static HappyPhoton.Tests.RawOrientationMeasureSupport;

namespace HappyPhoton.Tests;

public sealed class RawRenderRotationControlTests(ITestOutputHelper output)
{
    [Fact]
    public void RotatedBaseControl_IsolatesCaptureSharpening()
    {
        Require();
        using var directory = new TemporaryDirectory();
        var original = SyncSpotGateSupport.Fixture();
        var one = SyncSpotRotatedFixture.Create(original, directory.Path, 1);
        var three = SyncSpotRotatedFixture.Create(original, directory.Path, 3);
        var rows = new List<object>();
        var unsharpened = new EditSettings { Detail = new() { CaptureSharpen = 0 } };

        foreach (var preview in new[] { true, false })
        {
            using var source = Load(one, preview);
            using var actual = Load(three, preview);
            // Preserve all orientation-1 facts; only independently permute its pixels.
            using var control = new BaseImage(Transform(source.Pixels, 3), source.Info);
            Assert.Equal(0, Difference(control.Pixels, actual.Pixels).Max);

            foreach (var intent in preview ? new[] { RenderIntent.Preview }
                : new[] { RenderIntent.Preview, RenderIntent.Export })
            {
                using var a = Render(source, intent);
                using var b = Render(actual, intent);
                using var c = Render(control, intent);
                using var expected = Transform(a.Image, 3);
                var actualError = Difference(expected, b.Image).Max;
                var controlError = Difference(expected, c.Image).Max;
                Assert.Equal(0, Difference(b.Image, c.Image).Max);
                Assert.Equal(actualError, controlError);

                using var withoutA = Render(source, intent, unsharpened);
                using var withoutC = Render(control, intent, unsharpened);
                using var withoutExpected = Transform(withoutA.Image, 3);
                var withoutSharpening = Difference(withoutExpected, withoutC.Image).Max;
                Assert.Equal(0, withoutSharpening);
                rows.Add(new
                {
                    surface = preview ? "Develop" : intent == RenderIntent.Preview ? "1:1" : "export",
                    orientation3Q16 = actualError,
                    rotatedBaseControlQ16 = controlError,
                    actualVsControlQ16 = Difference(b.Image, c.Image).Max,
                    captureSharpenDisabledQ16 = withoutSharpening,
                    stage = "RenderSharpening.ApplyCapture"
                });
            }
        }

        Record("O1-control", rows, output);
    }
}
