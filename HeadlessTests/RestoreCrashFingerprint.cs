using Xunit;

namespace HappyPhoton.Tests;

public sealed class RestoreCrashFingerprintTests
{
    [Theory]
    [InlineData("thumbs")]
    [InlineData("previews")]
    [InlineData("rendered-thumbs")]
    [InlineData("tmp")]
    public void CacheTierBytes_AffectFingerprint(string tier)
    {
        using var first = new TemporaryDirectory();
        using var second = new TemporaryDirectory();

        foreach (var root in new[] { first.Path, second.Path })
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, "cache", "assets", tier));
            File.WriteAllText(Path.Combine(directory.FullName, "old"), "same");
        }

        Assert.True(KillPointFingerprint.Read(first.Path).SameAs(KillPointFingerprint.Read(second.Path)));
        File.WriteAllText(Path.Combine(second.Path, "cache", "assets", tier, "old"), "diff");
        Assert.False(KillPointFingerprint.Read(first.Path).SameAs(KillPointFingerprint.Read(second.Path)));
    }

    [Fact]
    public void EmptyDirectory_AffectsFingerprint()
    {
        using var first = new TemporaryDirectory();
        using var second = new TemporaryDirectory();
        Assert.True(KillPointFingerprint.Read(first.Path).SameAs(KillPointFingerprint.Read(second.Path)));
        Directory.CreateDirectory(Path.Combine(second.Path, "empty"));
        Assert.False(KillPointFingerprint.Read(first.Path).SameAs(KillPointFingerprint.Read(second.Path)));
    }
}
