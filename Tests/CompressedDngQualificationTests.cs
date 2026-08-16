using System.Security.Cryptography;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CompressedDngQualificationTests
{
    [Fact]
    public void CheckedInDng_CompressionTagParserReadsUncompressedIfd()
    {
        var path = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "pentax-k-r.dng");

        Assert.Contains((ushort)1, DngCompressionInspection.ReadCompressionTags(path));
    }

    [Fact]
    public void ExternalCorpus_CompressionTagsAndNativeDecodeAreVerified()
    {
        var lossy = Environment.GetEnvironmentVariable("HAPPY_PHOTON_DNG_LOSSY");
        var deflate = Environment.GetEnvironmentVariable("HAPPY_PHOTON_DNG_DEFLATE");
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(lossy) || string.IsNullOrWhiteSpace(deflate),
            "Set HAPPY_PHOTON_DNG_LOSSY and HAPPY_PHOTON_DNG_DEFLATE to the hash-pinned external corpus.");

        Verify(
            lossy!, 6_548_376,
            "91BE7341D999AE17A3C768CE394BCF9183BB3DD9D88479EF722A510EEC87E01F",
            34892);
        Verify(
            deflate!, 13_298_976,
            "EC304072B464F82D9FD8DDEB47EBE83ECF5B3007C6595C61257EC849F0919A08",
            8);
    }

    private static void Verify(string path, long length, string hash, ushort compression)
    {
        var file = new FileInfo(path);
        Assert.True(file.Exists, $"External DNG is missing: {path}");
        Assert.Equal(length, file.Length);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        Assert.Contains(compression, DngCompressionInspection.ReadCompressionTags(path));

        var loader = new RawBaseLoader();
        using var image = loader.LoadFullBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        Assert.NotNull(image);
        Assert.Equal(BaseSourceKind.RawLibRaw, image!.Info.Kind);
        Assert.True(image.Info.IsRawSource);
    }
}
