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
        Assert.Equal(
            ["Transmitted Lens", "AF Nikkor 50mm f/1.8D"],
            resolver.ResolveCandidates("Nikon", Identity(masked, " Transmitted Lens ")));
        Assert.Equal("AF Nikkor 50mm f/1.8D",
            resolver.Resolve("Nikon", Identity(masked)));
        Assert.Null(resolver.Resolve("Nikon", Identity(masked | 0x20)));
    }

    [Fact]
    public void CompositeTableRequiresKnownNikonFMount()
    {
        WriteTable("nikon", "0000000000000001\tKnown Lens\n");
        var resolver = new LensIdentityResolver(_directory);

        Assert.Equal("Known Lens", resolver.Resolve(
            "Nikon", Identity(1, mount: LibRawLensMounts.NikonF)));
        Assert.Null(resolver.Resolve("Nikon", Identity(1, mount: 1)));
        Assert.Null(resolver.Resolve("Nikon", Identity(1, mount: 0)));
    }

    [Theory]
    [InlineData("AF Nikkor 20mm f/2.8", "Nikon AF Nikkor 20mm f/2.8D")]
    [InlineData("Nikon AF Nikkor 20mm f/2.8", "Nikon AF Nikkor 20mm f/2.8D")]
    [InlineData("Rokinon 20mm f/1.8 ED AS UMC", "Samyang 20mm f/1.8 ED AS UMC")]
    [InlineData("AF Nikkor 28mm f/2.8", null)]
    public void ShippedAliasesAreLimitedToCuratedOpticalPairs(string name, string? expected)
        => Assert.Equal(expected, new LensIdentityResolver().ResolveAlias(name, "Nikon"));

    [Theory]
    [InlineData("# comment\n\nSelf\tSelf\nSource\tTarget\n", "Target")]
    [InlineData("Source\tTarget\nMalformed\n", null)]
    [InlineData("Source\tTarget\tExtra\n", null)]
    [InlineData("Source\t \n", null)]
    [InlineData("Source\tTarget\nSource\tOther\n", null)]
    public void AliasTableRejectsMalformedRowsAndIgnoresSelfAliases(string data, string? expected)
    {
        WriteTable("same-optics", data);
        var resolver = new LensIdentityResolver(_directory);
        Assert.Equal(expected, resolver.ResolveAlias("Source"));
        Assert.Null(resolver.ResolveAlias("Self"));
    }

    [Theory]
    [InlineData("Canon", " CANON: Source Lens ")]
    [InlineData("Nikon", "NIKON\tSource-Lens")]
    [InlineData("Other Maker", "Other Maker Source Lens")]
    public void AliasLookupNormalizesSuppliedMakerPrefix(string make, string name)
    {
        WriteTable("same-optics", "Source Lens\tTarget Lens\n");
        Assert.Equal("Target Lens", new LensIdentityResolver(_directory).ResolveAlias(name, make));
    }

    private void WriteTable(string maker, string contents)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, $"{maker}.tsv"), contents);
    }

    private static LibRawLensIdentity Identity(
        ulong id,
        string? lens = null,
        uint mount = LibRawLensMounts.NikonF) => new(
        id, lens, 0, mount, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, null, 0, null, 0, null);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
