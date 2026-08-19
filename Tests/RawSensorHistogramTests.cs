using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawSensorHistogramTests
{
    private const uint BayerFilters = 0xB4B4B4B4;

    [Fact]
    public void Bayer_ScansVisibleWindowWithPitchMarginsAndMergedGreens()
    {
        var frame = Frame(width: 2, height: 2, pitch: 5, top: 1, left: 1);
        frame.SetVisible([256, 1024, 2048, 4095]);

        var histogram = RawSensorHistogram.Sample(frame);

        Assert.NotNull(histogram);
        Assert.Equal(HistogramDomain.RawSensor, histogram!.Domain);
        Assert.Equal(4, Sum(histogram));
        Assert.Equal(1, histogram.Red.Sum());
        Assert.Equal(2, histogram.Green.Sum());
        Assert.Equal(1, histogram.Blue.Sum());
        Assert.Equal(4, histogram.Clipping!.TotalVisibleSamples);
        // Odd margins must not shift CFA phase (phase is visible-frame, addressing is
        // margin-offset). The distinguishable value 256 sits at visible (0,0) = Red and
        // the clipped 4095 at visible (1,1) = Blue; raw-frame phase would swap them, so
        // assert the channel assignment, not just the sums/total.
        Assert.Equal(1, histogram.Red[ExpectedBin(256, 0, 4095)]);
        Assert.Equal(1, histogram.Clipping.Blue);
        Assert.Equal(0, histogram.Clipping.Red);
        Assert.Equal(0, histogram.Clipping.Green);
    }

    [Fact]
    public void Binning_UsesChannelAndRepeatingBlackWithSrgbEncoding()
    {
        var frame = Frame(width: 2, height: 1, maximum: 4095);
        frame.Black = 100;
        frame.CBlack[0] = 20;
        frame.CBlack[1] = 40;
        frame.RepeatingRows = 1;
        frame.RepeatingColumns = 2;
        frame.CBlack[6] = 10;
        frame.CBlack[7] = 30;
        frame.SetVisible([1120, 2140]);

        var histogram = RawSensorHistogram.Sample(frame)!;

        Assert.Equal(1, histogram.Red[ExpectedBin(1120, 130, 4095)]);
        Assert.Equal(1, histogram.Green[ExpectedBin(2140, 170, 4095)]);
    }

    [Fact]
    public void EncodedWhiteBinDoesNotImplyLinearClipping()
    {
        var frame = Frame(width: 2, height: 1, maximum: 4095);
        frame.SetVisible([4080, 4095]);

        var histogram = RawSensorHistogram.Sample(frame)!;

        Assert.Equal(2, histogram.Red[255] + histogram.Green[255]);
        Assert.Equal(1, histogram.Clipping!.Red + histogram.Clipping.Green);
    }

    [Fact]
    public void XTrans_UsesAllThirtySixVisibleOriginPositions()
    {
        var xtrans = Enumerable.Range(0, 36)
            .Select(index => (sbyte)(index % 3)).ToArray();
        var frame = Frame(width: 6, height: 6, filters: 9, xtrans: xtrans);
        frame.SetVisible(Enumerable.Repeat((ushort)1000, 36).ToArray());

        var histogram = RawSensorHistogram.Sample(frame)!;

        Assert.Equal(12, histogram.Red.Sum());
        Assert.Equal(12, histogram.Green.Sum());
        Assert.Equal(12, histogram.Blue.Sum());
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(999u)]
    public void UnsupportedCfa_ReturnsNullWithoutReadingSamples(uint filters)
    {
        var frame = Frame(filters: filters);
        frame.ThrowOnSamples = true;

        Assert.Null(RawSensorHistogram.Sample(frame));
    }

    [Fact]
    public void LookupCap_UsesSplitTablesForXTrans()
    {
        Assert.True(RawSensorHistogram.UsesFusedLookup(8, 2, 0, 0));
        Assert.False(RawSensorHistogram.UsesFusedLookup(6, 6, 0, 0));
    }

    [Fact]
    public void Cancellation_IsObservedAtRowBoundary()
    {
        var frame = Frame(width: 1, height: 257);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            RawSensorHistogram.Sample(frame, cancellation.Token));
    }

    private static SyntheticFrame Frame(
        int width = 1,
        int height = 1,
        int pitch = 0,
        int top = 0,
        int left = 0,
        uint maximum = 4095,
        uint filters = BayerFilters,
        sbyte[]? xtrans = null) =>
        new(width, height, pitch == 0 ? width + left : pitch, top, left,
            maximum, filters, xtrans ?? new sbyte[36]);

    private static int Sum(HistogramData histogram) =>
        histogram.Red.Sum() + histogram.Green.Sum() + histogram.Blue.Sum();

    private static int ExpectedBin(uint value, uint black, uint white)
    {
        var n = Math.Clamp((value - black) / (double)Math.Max(1, white - black), 0, 1);
        var encoded = n <= 0.0031308
            ? 12.92 * n
            : 1.055 * Math.Pow(n, 1 / 2.4) - 0.055;
        return (int)Math.Round(encoded * 255, MidpointRounding.AwayFromZero);
    }

    private sealed class SyntheticFrame : IRawSensorFrame
    {
        private readonly ushort[] _samples;
        private readonly int _pitch;
        private readonly int _top;
        private readonly int _left;

        public SyntheticFrame(int width, int height, int pitch, int top, int left,
            uint maximum, uint filters, sbyte[] xtrans)
        {
            VisibleWidth = (uint)width;
            VisibleHeight = (uint)height;
            RawPitch = (uint)(pitch * 2);
            TopMargin = (uint)top;
            LeftMargin = (uint)left;
            Maximum = maximum;
            Filters = filters;
            XTrans = xtrans;
            _pitch = pitch;
            _top = top;
            _left = left;
            _samples = new ushort[(top + height) * pitch];
        }

        public int Colors { get; set; } = 3;
        public uint Filters { get; }
        public IReadOnlyList<sbyte> XTrans { get; }
        public uint RawPitch { get; }
        public uint VisibleWidth { get; }
        public uint VisibleHeight { get; }
        public uint TopMargin { get; }
        public uint LeftMargin { get; }
        public uint Black { get; set; }
        public uint Maximum { get; }
        public uint RepeatingRows { get; set; }
        public uint RepeatingColumns { get; set; }
        public uint[] CBlack { get; } = new uint[4104];
        IReadOnlyList<uint> IRawSensorFrame.CBlack => CBlack;
        public bool ThrowOnSamples { get; set; }
        public Span<ushort> Samples => ThrowOnSamples
            ? throw new InvalidOperationException()
            : _samples;

        public void SetVisible(IReadOnlyList<ushort> values)
        {
            var index = 0;
            for (var row = 0; row < (int)VisibleHeight; row++)
                for (var column = 0; column < (int)VisibleWidth; column++)
                    _samples[(_top + row) * _pitch + _left + column] = values[index++];
        }
    }
}
