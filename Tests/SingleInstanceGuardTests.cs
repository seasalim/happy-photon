using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public class SingleInstanceGuardTests
{
    // The default name is machine-global, so no test may acquire it: a running
    // app or a concurrent test run would turn the test into a race. Exclusivity
    // is proven with unique names below; these pin the name itself.
    [Fact]
    public void DefaultMutexName_IsStableAcrossReleases()
    {
        Assert.Equal(
            "HappyPhoton.Application.SingleInstance",
            SingleInstanceGuard.ApplicationMutexName);
    }

    [Fact]
    public void DefaultMutexName_DoesNotCollideWithLegacyPhotoEdit()
    {
        Assert.NotEqual(
            "PhotoEdit.Application.SingleInstance",
            SingleInstanceGuard.ApplicationMutexName);
    }

    [Fact]
    public void TryAcquire_AllowsOnlyOneOwnerAtATime()
    {
        var mutexName = $"HappyPhoton.Tests.{Guid.NewGuid():N}";

        using var first = SingleInstanceGuard.TryAcquire(mutexName);
        using var second = SingleInstanceGuard.TryAcquire(mutexName);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void TryAcquire_AllowsAnotherOwnerAfterRelease()
    {
        var mutexName = $"HappyPhoton.Tests.{Guid.NewGuid():N}";

        using (var first = SingleInstanceGuard.TryAcquire(mutexName))
        {
            Assert.NotNull(first);
        }

        using var next = SingleInstanceGuard.TryAcquire(mutexName);

        Assert.NotNull(next);
    }
}
