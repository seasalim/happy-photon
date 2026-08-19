using Xunit;

namespace HappyPhoton.LibRaw.Interop.Tests;

public sealed class LibRawContextLeaseTests
{
    [Fact]
    public void FailedLeaseConstruction_ReleasesNativeLeaseToken()
    {
        ulong released = 0;
        var descriptor = new NativeMosaicDescriptor { Lease = 42 };

        LibRawContext.ReleaseFailedMosaic(
            descriptor,
            token => released = token);

        Assert.Equal(42ul, released);
    }

    [Fact]
    public void FailedLeaseConstruction_WithoutTokenDoesNotRelease()
    {
        var calls = 0;

        LibRawContext.ReleaseFailedMosaic(
            new NativeMosaicDescriptor(),
            _ => calls++);

        Assert.Equal(0, calls);
    }
}
