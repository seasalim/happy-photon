using HappyPhoton.MlSpike;
using Xunit;

namespace MlSpike.Tests;

public sealed class LocalFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mlspike-test-" + Guid.NewGuid().ToString("N"));

    public LocalFilesTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void OutputCollisionDoesNotModifyExistingBytes()
    {
        var path = Path.Combine(_root, "original.png");
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Throws<IOException>(() => LocalFiles.Create(path));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void ManifestTraversalAndAbsolutePathsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => LocalFiles.Resolve(_root, "../outside.png"));
        Assert.Throws<InvalidDataException>(() => LocalFiles.Resolve(_root, Path.Combine(_root, "image.png")));
    }

    [Fact]
    public void HashUsesSourceBytes()
    {
        var path = Path.Combine(_root, "sample");
        File.WriteAllText(path, "abc");
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            LocalFiles.Hash(path));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
