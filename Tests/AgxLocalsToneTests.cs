using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AgxLocalsToneTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparedTonePreservesExtendedEvaluator(bool editedCurve)
    {
        var curve = new CurveData();
        if (editedCurve) curve.AddPointAndReturnIndex(.4, .3);
        foreach (var contrast in new[] { -100, 0, 100 })
        foreach (var shadows in new[] { -100, 0, 100 })
        foreach (var highlights in new[] { -100, 0, 100 })
        {
            var parameters = new AgxToneParameters(-1, .3, contrast, highlights,
                shadows, curve);
            var slope = AgxToneEngine.Slope(contrast);
            var toe = AgxToneEngine.ToePower(shadows);
            var shoulder = AgxToneEngine.ShoulderPower(highlights);
            var toeScale = AgxToneEngine.TailScale(AgxToneEngine.XPivot,
                AgxToneEngine.YPivot, slope, toe);
            var shoulderScale = AgxToneEngine.TailScale(1 - AgxToneEngine.XPivot,
                1 - AgxToneEngine.YPivot, slope, shoulder);
            foreach (var value in new[] { -1d, 0, .00001, .01, .18, .5, 1, 2, 16, 4294967296 })
            {
                var gain = Math.Pow(2, -.7);
                var reference = AgxToneEngine.EvaluateToneExtendedUnchecked(value,
                    parameters, gain, 1, slope, toe, shoulder);
                var prepared = AgxToneEngine.EvaluateToneExtendedUnchecked(value,
                    parameters, gain, 1, slope, toe, shoulder, null,
                    toeScale, shoulderScale, false);
                Assert.Equal(reference, prepared);
                var optimized = AgxToneEngine.EvaluateToneExtendedUnchecked(value,
                    parameters, gain, 1, slope, toe, shoulder, null,
                    toeScale, shoulderScale, !editedCurve);
                Assert.InRange(Math.Abs(reference - optimized), 0, 1e-14);
            }
        }
    }
}
