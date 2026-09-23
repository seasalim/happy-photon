using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderStageProbeTests
{
    [Fact]
    public void WithoutListener_MarksAreEmptyAndNothingIsAllocated()
    {
        using var image = new MagickImage(MagickColors.Gray, 4, 4);
        RenderStageProbe.End(RenderStageProbe.Begin(), "warm-up", image);

        var nonEmpty = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++)
        {
            var mark = RenderStageProbe.Begin();
            if (mark.Timestamp != 0 || mark.AllocatedBytes != 0) nonEmpty++;
            RenderStageProbe.End(mark, "stage", image);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, nonEmpty);
        Assert.Equal(0, allocated);
    }
}
