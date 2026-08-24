using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LensIdentityResolverTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"happy-photon-lens-id-{Guid.NewGuid():N}");

    [Fact]
    public void ShippedTableResolvesSingletonAndRejectsUnknownAndAmbiguousIds()
    {
        var resolver = new LensIdentityResolver();

        Assert.Equal("AF Nikkor 50mm f/1.8D",
            resolver.Resolve("NIKON", Identity(0x7658505014147A02)));
        Assert.Null(resolver.Resolve("NIKON", Identity(ulong.MaxValue)));
        Assert.Null(resolver.Resolve("NIKON", Identity(0x000000000000F10C)));
    }

    [Fact]
    public void TableTreatsMultiNameAndDuplicateKeyEntriesAsAmbiguous()
    {
        WriteTable("nikon",
            "0000000000000001\tFirst Lens or Second Lens\n" +
            "0000000000000002\tFirst Lens\n" +
            "0000000000000002\tSecond Lens\n" +
            "0000000000000003\tOnly Lens\n" +
            "0000000000000003\tOnly Lens\n");
        var resolver = new LensIdentityResolver(_directory);

        Assert.Null(resolver.Resolve("Nikon", Identity(1)));
        Assert.Null(resolver.Resolve("Nikon", Identity(2)));
        Assert.Equal("Only Lens", resolver.Resolve("Nikon", Identity(3)));
    }

    [Fact]
    public void SelectsTableByNormalizedMakerAndMissingTableIsNoData()
    {
        WriteTable("canon", "0000000000000001\tCanon Lens\n");
        WriteTable("nikon", "0000000000000001\tNikon Lens\n");
        var resolver = new LensIdentityResolver(_directory);

        Assert.Equal("Canon Lens", resolver.Resolve("Canon", Identity(1)));
        Assert.Equal("Nikon Lens", resolver.Resolve("Nikon", Identity(1)));
        Assert.Null(resolver.Resolve("Pentax", Identity(1)));
    }

    [Fact]
    public void TransmittedNameWinsAndNoFt1AlternateKeyIsInvented()
    {
        var resolver = new LensIdentityResolver();
        var masked = 0x7658505014147A02ul;

        Assert.Equal("Transmitted Lens", resolver.Resolve(
            "Other Maker", Identity(ulong.MaxValue, " Transmitted Lens ")));
        Assert.Equal("AF Nikkor 50mm f/1.8D",
            resolver.Resolve("Nikon", Identity(masked)));
        Assert.Null(resolver.Resolve("Nikon", Identity(masked | 0x20)));
    }

    private void WriteTable(string maker, string contents)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, $"{maker}.tsv"), contents);
    }

    private static LibRawLensIdentity Identity(ulong id, string? lens = null) => new(
        id, lens, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, null, 0, null, 0, null);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
