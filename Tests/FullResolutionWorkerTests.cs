using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class FullResolutionWorkerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtendedToneLutHonorsBudgetAndPreservesCodes(bool raw)
    {
        var caps = new List<int>();
        var stages = new List<string>();
        var execution = RenderExecutionOptions.Resting(CancellationToken.None, Environment.ProcessorCount, stages.Add)
            with { WorkerBudget = () => 2, WorkersSelected = caps.Add };
        var parameters = new ToneParams(.317, 1, 0, 17, 12, -9, false, new());
        var agx = new AgxToneParameters(.317, 0, 17, -9, 12, new());
        var actual = raw ? ExtendedToneLut.ComposeRaw(agx, 1, execution)
            : ExtendedToneLut.ComposeStandard(parameters, execution);
        var expected = raw ? ExtendedToneLut.ComposeRaw(agx, 1)
            : ExtendedToneLut.ComposeStandard(parameters);
        Assert.Empty(stages);
        Assert.NotEmpty(caps);
        Assert.All(caps, count => Assert.InRange(count, 1, 2));
        foreach (var value in new[] { 0, .0001, .1, .8, 1, 2, 128 })
            Assert.Equal(expected.Red.Evaluate(value), actual.Red.Evaluate(value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UncachedToneLutHonorsBudgetWithoutReportingStages(bool raw)
    {
        var caps = new List<int>();
        var stages = new List<string>();
        var execution = RenderExecutionOptions.Resting(CancellationToken.None, Environment.ProcessorCount, stages.Add)
            with { WorkerBudget = () => 2, WorkersSelected = caps.Add };
        var parameters = new ToneParams(.317, 1, 0, 17, 12, -9, false, new());
        var agx = new AgxToneParameters(.317, 0, 17, -9, 12, new());
        var actual = raw ? AgxToneLut.Compose(agx, 1, execution) : ToneLut.Compose(parameters, execution);
        var expected = raw ? AgxToneLut.Compose(agx, 1) : ToneLut.Compose(parameters);

        Assert.Empty(stages);
        Assert.NotEmpty(caps);
        Assert.All(caps, count => Assert.InRange(count, 1, 2));
        Assert.Equal(expected.Red, actual.Red);
    }

    [Fact]
    public void CancelledExtendedLutDoesNotPoisonCache()
    {
        var parameters = new ToneParams(.734, 1, 0, 0, 0, 0, false, new());
        var key = ToneLut.Compose(parameters);
        var execution = RenderExecutionOptions.Resting(new CancellationToken(true));
        Assert.ThrowsAny<OperationCanceledException>(() => ExtendedToneLut.ForStandard(key, parameters, execution));
        Assert.NotNull(ExtendedToneLut.ForStandard(key, parameters));
    }

    [Fact]
    public void DcpLookupAndApplicationHonorBudgetAndPreserveCodes()
    {
        var map = new DcpHueSatMap(6, 3, 2, true,
            DcpProfileReaderTests.CreateTable(6, 3, 2, 8, 1.1f, .92f), null, 0);
        var caps = new List<int>();
        var stages = new List<string>();
        var execution = RenderExecutionOptions.Resting(CancellationToken.None, Environment.ProcessorCount, stages.Add)
            with { WorkerBudget = () => 2, WorkersSelected = caps.Add };
        var actual = Enumerable.Range(0, 180000).Select(i => (ushort)(i % 65536)).ToArray();
        var expected = (ushort[])actual.Clone();
        DcpHueSatRenderer.ApplyValues(actual, actual.Length / 3, 3, 0, 1, 2, map, execution);
        DcpHueSatRenderer.ApplyValues(expected, expected.Length / 3, 3, 0, 1, 2, map);
        Assert.Empty(stages);
        Assert.NotEmpty(caps);
        Assert.All(caps, count => Assert.InRange(count, 1, 2));
        Assert.Equal(expected, actual);
    }
}
