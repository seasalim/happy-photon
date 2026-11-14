using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class RenderDedupHashControlTests
{
    public static IEnumerable<object[]> ProofCases()
    {
        foreach (var space in new[] { OutputColorSpace.Srgb, OutputColorSpace.DisplayP3 })
        foreach (var sharpening in new[] { OutputSharpeningMode.Screen, OutputSharpeningMode.Print })
        foreach (var resize in new[] { false, true })
            yield return [space, sharpening, resize];
    }

    [Theory]
    [MemberData(nameof(ProofCases))]
    public void FinalizeProof_MatchesFrozenHash(
        OutputColorSpace space,
        OutputSharpeningMode sharpening,
        bool resize)
    {
        using var source = CreateImage(Width, Height, 0x68E31DA4);

        using var proof = RenderFinalizer.Finalize(
            source, resize ? ResizeLimit : null, space, sharpening, wasResized: false,
            effects: Effects());

        var key = $"proof-{space}-{sharpening}-{(resize ? "resize" : "native")}";
        AssertFrozen(key, ImageHash(proof));
    }

    [Theory]
    [InlineData(OutputColorSpace.Srgb)]
    [InlineData(OutputColorSpace.DisplayP3)]
    public void FinalizeOwnedResting_MatchesFrozenHashAtEveryCap(OutputColorSpace space)
    {
        var hashes = new List<string>();

        foreach (var cap in Caps())
        {
            var source = CreateImage(Width, Height, 0x1B873593);
            using var result = RenderFinalizer.FinalizeOwnedResting(
                source, ResizeLimit, space, Effects(), Resting(cap));
            hashes.Add(ImageHash(result));
        }

        Assert.All(hashes, hash => Assert.Equal(hashes[0], hash));
        AssertFrozen($"owned-resting-{space}", hashes[0]);
    }

    [Theory]
    [InlineData(OutputColorSpace.Srgb)]
    [InlineData(OutputColorSpace.DisplayP3)]
    public void FinalizeOwned_MatchesFrozenHash(OutputColorSpace space)
    {
        var source = CreateImage(Width, Height, 0x1B873593);

        using var result = RenderFinalizer.FinalizeOwned(
            source, ResizeLimit, space, OutputSharpeningMode.Off,
            false, true, out var frame, effects: Effects());

        AssertFrozen($"owned-{space}", ImageHash(result));
        using var hash = new HashBuilder();
        hash.AddInt32(frame!.Value.Layout.Channels);
        hash.AddInt32(frame.Value.Alpha ?? -1);
        hash.AddUInt16s(frame.Value.Samples);
        AssertFrozen($"owned-frame-{space}", hash.Finish());
    }

    [Fact]
    public void ResizeInLinearLight_MatchesFrozenHash()
    {
        using var image = CreateImage(Width, Height, 0x85EBCA6B);

        RenderColorEncoding.ResizeInLinearLight(image, ResizeLimit);

        AssertFrozen("resize-linear", ImageHash(image));

        foreach (var cap in Caps())
        {
            using var resting = CreateImage(Width, Height, 0x85EBCA6B);
            RenderColorEncoding.ResizeInLinearLight(resting, ResizeLimit, Resting(cap));
            Assert.Equal(ImageHash(image), ImageHash(resting));
        }
    }

    [Theory]
    [InlineData(OutputColorSpace.Srgb)]
    [InlineData(OutputColorSpace.DisplayP3)]
    public void ConvertEncodedRec2020ToTarget_MatchesFrozenHash(OutputColorSpace space)
    {
        using var image = CreateImage(Width, Height, 0xC2B2AE35);

        var frame = RenderColorEncoding.ConvertEncodedRec2020ToTarget(image, space);

        using var hash = new HashBuilder();
        hash.AddInt32(frame.Layout.Channels);
        hash.AddInt32(frame.Alpha ?? -1);
        hash.AddUInt16s(frame.Samples);
        hash.AddBytes(Convert.FromHexString(ImageHash(image)));
        AssertFrozen($"encode-{space}", hash.Finish());

        foreach (var cap in Caps())
        {
            using var resting = CreateImage(Width, Height, 0xC2B2AE35);
            RenderColorEncoding.ConvertEncodedRec2020ToTarget(resting, space, Resting(cap));
            Assert.Equal(ImageHash(image), ImageHash(resting));
        }
    }

    public static IEnumerable<object[]> EffectCases()
    {
        yield return ["vignette-negative", -45, 30, 0, GrainSize.Medium];
        yield return ["vignette-positive", 55, 70, 0, GrainSize.Medium];
        yield return ["grain-fine", 0, 50, 38, GrainSize.Fine];
        yield return ["grain-medium", 0, 50, 38, GrainSize.Medium];
        yield return ["grain-coarse", 0, 50, 38, GrainSize.Coarse];
        yield return ["combined", -37, 61, 42, GrainSize.Coarse];
    }

    [Theory]
    [MemberData(nameof(EffectCases))]
    public void RenderEffects_MatchesFrozenHash(
        string name,
        int vignette,
        int midpoint,
        int grain,
        GrainSize size)
    {
        var settings = new EffectsSettings
        {
            Vignette = vignette,
            Midpoint = midpoint,
            Grain = grain,
            GrainSize = size
        };
        using var image = CreateImage(Width, Height, 0x27D4EB2F);

        RenderEffects.Apply(image, settings);

        var hash = ImageHash(image);
        AssertFrozen($"effects-{name}", hash);

        foreach (var cap in Caps())
        {
            using var resting = CreateImage(Width, Height, 0x27D4EB2F);
            RenderEffects.Apply(resting, settings, Resting(cap));
            Assert.Equal(hash, ImageHash(resting));
        }
    }
}
