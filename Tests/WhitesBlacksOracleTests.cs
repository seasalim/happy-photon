using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WhitesBlacksOracleTests(ITestOutputHelper output)
{
    // Independent analytic definition of the frozen ramps, not the table construction.
    internal static double LogGain(double y, int whites, int blacks, bool raw)
    {
        var ev = Math.Log2(y / .18);
        var span = ev > 0 ? raw ? 6.5 : Math.Log2(1 / .18) : raw ? 10 : 5.5;
        var t = Math.Min(1, Math.Abs(ev) / span);
        double ramp;
        if (raw) ramp = 3 * t * t - 2 * t * t * t;
        else if (t < .02) ramp = t * t / .0392;
        else if (t > .98) ramp = 1 - (1 - t) * (1 - t) / .0392;
        else ramp = (t - .01) / .98;
        return (ev > 0 ? whites : blacks) * (raw ? .04 : .024) * ramp;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExponentMantissaNodesAndInteriorsMatchFrozenOracle(bool raw)
    {
        var worst = 0d;
        foreach (var whites in new[] { -100, -60, 0, 60, 100 })
        foreach (var blacks in new[] { -100, -60, 0, 60, 100 })
        {
            var op = new WhitesBlacksOperator(whites, blacks, raw);
            var previous = 0d;
            for (var i = 0; i <= 17 * 2048; i++)
            foreach (var fraction in new[] { 0d, .25, .5, .75 })
            {
                var y = .18 * BitConverter.Int64BitsToDouble(((1023L - 10) << 52) +
                    ((long)i << 41) + (long)(fraction * (1L << 41)));
                var actual = y * op.Gain(WhitesBlacksOperator.Position(y));
                var expected = y * Math.Pow(2, LogGain(y, whites, blacks, raw));
                var error = Math.Abs(actual - expected) * 65535;
                worst = Math.Max(worst, error);
                Assert.InRange(error, 0, 1);
                Assert.True(actual > previous);
                previous = actual;
                if (whites == 0 && y >= .18 || blacks == 0 && y <= .18)
                    Assert.Equal(1, op.Gain(WhitesBlacksOperator.Position(y)));
            }
            Assert.Equal(1, op.Gain(WhitesBlacksOperator.Position(.18)));
            Assert.Equal(op.Gain(0), op.Gain(WhitesBlacksOperator.Position(double.Epsilon)));
            Assert.Equal(op.Gain(17 * 2048), op.Gain(WhitesBlacksOperator.Position(double.MaxValue)));
        }
        output.WriteLine($"raw={raw}, maximum unbounded scene-output Q16 error={worst:F6}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WeightedStackMatchesAnalyticLogSum(bool raw)
    {
        var global = new WhitesBlacksOperator(60, -60, raw);
        var local = new WhitesBlacksOperator(30, -20, raw);
        foreach (var weight in new[] { 0d, .01, .2, .5, .99, 1 })
        for (var i = 0; i < 8192; i++)
        {
            var y = .18 * Math.Pow(2, -12 + i * 20d / 8191);
            var position = WhitesBlacksOperator.Position(y);
            var actual = y * double.Exp2(global.LogGain(position) + 8 * weight * local.LogGain(position));
            var expected = y * Math.Pow(2, LogGain(y, 60, -60, raw) + 8 * weight * LogGain(y, 30, -20, raw));
            // Relative gain accuracy also covers stacked headroom far above the Q16 buffer domain.
            Assert.InRange(Math.Abs(actual / expected - 1), 0, 1e-5);
            double Display(double value) => raw ? ToneLut.SrgbEncode(AgxToneEngine.EvaluateToneExtendedUnchecked(
                value, new(0, 0, 0, 0, 0, new()), 1, 0, 2, 3, 3.25)) : ToneLut.SrgbEncode(Math.Min(1, value));
            Assert.InRange(Math.Abs(Display(actual) - Display(expected)) * 65535, 0, 1);
        }
    }
}
