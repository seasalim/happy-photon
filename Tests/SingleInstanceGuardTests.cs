using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public class SingleInstanceGuardTests
{
    [Fact]
    public void TryAcquire_DefaultNameAllowsOnlyOneHappyPhotonOwner()
    {
        using var first = SingleInstanceGuard.TryAcquire();
        using var second = SingleInstanceGuard.TryAcquire();

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void TryAcquire_PhotoEditMutexDoesNotBlockHappyPhoton()
    {
        using var legacyMutex = new Mutex(
            initiallyOwned: false,
            "PhotoEdit.Application.SingleInstance");
        using var happyPhoton = SingleInstanceGuard.TryAcquire();

        Assert.NotNull(happyPhoton);
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
