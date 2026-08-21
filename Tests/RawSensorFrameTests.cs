using System.Text.Json;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawSensorFrameTests
{
    public static TheoryData<string> FixtureNames => new()
    {
        "canon-eos-350d.cr2",
        "canon-eos-6d-iso-6400.cr2",
        "fujifilm-x30.raf",
        "nikon-d70-burst-1.nef",
        "nikon-d70-burst-2.nef",
        "pentax-k-r.dng"
    };

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void ShippedRuntime_FrameMatchesRecordedOracle(string fixtureName)
    {
        using var context = LibRawContext.Open(GoldenTestPaths.Asset(fixtureName));
        context.Unpack();
        using var frame = RawSensorFrame.TryCreate(context);
        Assert.NotNull(frame);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            GoldenTestPaths.RepositoryRoot, "native", "libraw", "oracle",
            "facts", fixtureName + ".json")));
        var expected = document.RootElement;
        var dimensions = expected.GetProperty("dimensions");
        var sensor = expected.GetProperty("sensor");
        var cblack = expected.GetProperty("cblack");

        Assert.Equal(expected.GetProperty("raw_pitch").GetUInt32(), frame!.RawPitch);
        Assert.Equal(dimensions.GetProperty("raw_width").GetUInt32(), frame.RawWidth);
        Assert.Equal(dimensions.GetProperty("raw_height").GetUInt32(), frame.RawHeight);
        Assert.Equal(dimensions.GetProperty("width").GetUInt32(), frame.VisibleWidth);
        Assert.Equal(dimensions.GetProperty("height").GetUInt32(), frame.VisibleHeight);
        Assert.Equal(dimensions.GetProperty("top_margin").GetUInt32(), frame.TopMargin);
        Assert.Equal(dimensions.GetProperty("left_margin").GetUInt32(), frame.LeftMargin);
        Assert.Equal(sensor.GetProperty("colors").GetInt32(), frame.Colors);
        Assert.Equal(sensor.GetProperty("filters").GetUInt32(), frame.Filters);
        Assert.Equal(sensor.GetProperty("xtrans").EnumerateArray()
            .Select(value => value.GetSByte()), frame.XTrans);
        Assert.Equal(expected.GetProperty("black").GetUInt32(), frame.Black);
        Assert.Equal(expected.GetProperty("maximum").GetUInt32(), frame.Maximum);
        Assert.Equal(cblack.GetProperty("block_rows").GetUInt32(), frame.RepeatingRows);
        Assert.Equal(cblack.GetProperty("block_columns").GetUInt32(), frame.RepeatingColumns);
        Assert.Equal(cblack.GetProperty("values").EnumerateArray()
            .Select(value => value.GetUInt32()),
            frame.CBlack.Take(cblack.GetProperty("values").GetArrayLength()));
    }

    [Fact]
    public void Lease_BlocksProcessUntilFrameIsDisposed()
    {
        using var context = LibRawContext.Open(GoldenTestPaths.Asset("canon-eos-350d.cr2"));
        context.Unpack();
        var frame = RawSensorFrame.TryCreate(context);
        Assert.NotNull(frame);

        Assert.Throws<LibRawProgrammingException>(() => context.Process());
        Assert.Throws<LibRawProgrammingException>(() => context.Recycle());

        frame!.Dispose();
        context.ConfigureOutput(LibRawOutputConfiguration.Linear(
            LibRawHighlightMode.Clip, LibRawFbddMode.Off, true));
        context.Process();
    }

    [Fact]
    public void ContextDispose_DefersNativeCloseWhileFrameOwnsLease()
    {
        var context = LibRawContext.Open(GoldenTestPaths.Asset("canon-eos-350d.cr2"));
        context.Unpack();
        using var frame = RawSensorFrame.TryCreate(context);

        context.Dispose();

        Assert.False(frame!.Samples.IsEmpty);
    }
}
