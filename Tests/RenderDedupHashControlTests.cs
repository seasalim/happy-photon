using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

// SLIM-WP6 bit-identity controls. Frozen at b53d0c9, before any production edit; never regenerate.
// Every value is hashed in explicit little-endian order (doubles as their IEEE-754 bits).
public sealed partial class RenderDedupHashControlTests(ITestOutputHelper output)
{
    // 480 x 360 = 172,800 pixels: every 32,768-pixel or 16-row chunked kernel uses several workers.
    private const int Width = 480;
    private const int Height = 360;
    private const int ResizeLimit = 320;

    private static readonly Dictionary<string, string> Frozen = new()
    {
        ["agx-compose-cached"] = "188FCEEE1BFCE1C1E0BA9F6E63456C9CD9DB144AC1A6E1A416591BCD0A87F7C4",
        ["characterization-derived"] = "4727AEDBF04C28FF8D90F8614027CC989742F2E6411AB3754DAAA61EA7066F77",
        ["characterization-profile"] = "640D3C1D00C266DC764E6736C3554E9888E48B59C8406E32AECADACAE06CB569",
        ["characterization-usable"] = "61DEDC52D9F8523E393ED533B947BEF6D636E74FF130685A2C532FB21AEC041F",
        ["clipping-analyze"] = "26B802FC802C1AC175602B0831CBE0026C80425D13B137B3287126E052B27CEE",
        ["dcp-huesat-dual"] = "D048D30FF6B4B89DACF560957B763EE6EB745EF21EAEA940A27B48EF3E0F036E",
        ["dcp-huesat-linear"] = "A12E89C91AD12EA89777FA0064150368FF08001D055B921CC97A5BAD7AFF1609",
        ["dcp-huesat-srgb"] = "0FC0800565C4840FA4696D021DFEAC09368B092E9439B6FE1DF2A3922A643A2B",
        ["dcp-matrix"] = "2E52721BDE58F3692F0CD35EA91FBD6B14F915EDD3C11E3EC17C46247BCA1671",
        ["effects-combined"] = "062D33B1209A956294376B5EDA5129B9F9B7135020AC583A2E19A2994DAA07B3",
        ["effects-grain-coarse"] = "B6AAF89CDDE85F49BE87842DFE67BA0FB6BEDEC919AABAF9BFE624744E8FF9E7",
        ["effects-grain-fine"] = "B882BF76EA956F72D5404787D352BA538A2F71C0D820258C76CC44BF3252E2DD",
        ["effects-grain-medium"] = "A72038806C68ABBD378EE14E069CF7539B53E46166D0C90545908B6A9718B2B0",
        ["effects-vignette-negative"] = "E4B775E3A80A1AB9E1949AFFA5F2D074FA7D0F806BB30524EEB6D664B52C8047",
        ["effects-vignette-positive"] = "CA39820F051228E6AD1FBDD764EBD37B11B81732F3477CB23E018D6AC6B0EDDC",
        ["encode-DisplayP3"] = "A2EAE431AB027E73D7B0CC93559D36C7B4065C518D37E25724CDC76A75FD94EE",
        ["encode-Srgb"] = "CB3B28EFFAC3252E043CF5744A88B3819781E9D611B134562686BCBBA0377473",
        ["geometry-perspective"] = "B365C64A4E048ED2C5A220D31A06067DF86775C008873A78255B24E0A454F61C",
        ["geometry-rotate"] = "D49723EE9DF3E1BDA5B47D21F10C0615CA9F6616F4628F924B033818EA334857",
        ["histogram-preview-fixed"] = "402902E254B1FEC13814ECE368596594F4307089591DDBD7DDAEF8361B3EEBDF",
        ["histogram-preview-scaled"] = "402902E254B1FEC13814ECE368596594F4307089591DDBD7DDAEF8361B3EEBDF",
        ["icc-softproof-p3-a2b1"] = "A1425F090E130905DE0271D0943D0138EE59A1AC21CFF139068B7AC54F2FA02F",
        ["icc-softproof-p3-curv1024"] = "C4F0CDE3ACEA55A1280E9AA188CDA7D976FDD7F918456BE393E354FEB64A13B7",
        ["icc-softproof-p3-d2b0"] = "A1425F090E130905DE0271D0943D0138EE59A1AC21CFF139068B7AC54F2FA02F",
        ["icc-softproof-p3-gamma22"] = "F551D999098206864EA56FDC7203E4526BA0FBFB07351E5051E339EA6AE039EB",
        ["icc-softproof-p3-mhc2"] = "EADBF09E4A6141B51581F0C45F08065508E893CB8EA63885527844BE408FD9F3",
        ["icc-softproof-srgb"] = "5C3D2A57A0AD50A68434F8D50FDD55209CCBD238725AFBB000EB332C282D35B6",
        ["icc-tonecurves"] = "7B89E28AF8D973F59C2BFFD4E80D6E73B4DF68EFE69037B0D8A00EF5F2924352",
        ["lens-import-o1"] = "180100D06DD6B8045D58D914CB598EA9147121D44AE1C02D63247338BA64ACB9",
        ["lens-import-o6"] = "85E192590F8E297209E709641807EF061959D51E05BD46A06F5122159CC517FF",
        ["lens-warpmask-o1"] = "1CAE20038271D43C7571A4C904D67509A21F302F9C7D11C16CCAB491BD76F47F",
        ["lens-warpmask-o6"] = "ADBF6755EC525DAAE92BB9702BF3ADC73A5103F74DBCAEF60CF643A2966B27A7",
        ["oklab-encoded-mixer"] = "1386267FA6EC4FB113372F99AF812AD69C794356CD5FF2CB619D1969B761D2AE",
        ["oklab-q16"] = "CCC1813AD1CD84DC8D98FFB1E2EC7F4ED60F573F3657CC8905A853936C40B4D0",
        ["oklab-q16-mixer"] = "5B7B1A065E677FE03C09A0E66BFD76D96DCD0FF04414FC004D85A82032C39793",
        ["owned-DisplayP3"] = "B3F63918CD11BCEF57FCCE62603B67B85C0C3E63822D80688F2BE5ED9C30CBDF",
        ["owned-Srgb"] = "86BF5EED147989334EEAB400FC39A3210DC30D22E45722CE3C84677835C683E3",
        ["owned-frame-DisplayP3"] = "8043DDE3A29C3FBD41A925F11730DB7978492C469D7327523FC512001B811EA6",
        ["owned-frame-Srgb"] = "68EF4CE6E7FF42CFD21CC171D44125A38BC119A790E0D06C9C8A99D17456913E",
        ["owned-resting-DisplayP3"] = "B3F63918CD11BCEF57FCCE62603B67B85C0C3E63822D80688F2BE5ED9C30CBDF",
        ["owned-resting-Srgb"] = "86BF5EED147989334EEAB400FC39A3210DC30D22E45722CE3C84677835C683E3",
        ["proof-DisplayP3-Print-native"] = "A83243115C11A5E50B8D5CC54241FAA967505948C153CF6D378F5D980784CCC7",
        ["proof-DisplayP3-Print-resize"] = "F283C684E7E8D4AA5812CE70E9EAB2A1B5144BCBC0D4B8D5F25ECA101963F94A",
        ["proof-DisplayP3-Screen-native"] = "4D017E09B1EE00A57B105BCB825B5C71165CA242CAE17CAF252F0902D699D047",
        ["proof-DisplayP3-Screen-resize"] = "868873F2FB406339C46C893B4CC079C820E6C07FC1D0A3AE3684581CE6A1A9FE",
        ["proof-Srgb-Print-native"] = "BB29F2F24655F824E3D73718B6CE897E1464C340BFF74B89AF8DA0ADEE123202",
        ["proof-Srgb-Print-resize"] = "7FAEB1C108D06055FDC00FBAAD7ED6E65D4E4BDAB2B5DF7EFC3D9218678F6822",
        ["proof-Srgb-Screen-native"] = "9401F6CDAFE62C89038EFF9DB334EBF43FB9A15624196A5B5A7EA6C832367333",
        ["proof-Srgb-Screen-resize"] = "036C4F0A9F8DFABF36F3A950A09355A61967B1680E02E876B25EC9D93FC7405A",
        ["raw-sensor-bayer-artifacts"] = "2F0F8379B2FF1DB07E0BFC3C85CD6C10F5EED5B4E0AB50BEDC9238940FA6A494",
        ["raw-sensor-bayer-sample"] = "952B0F6913EA73D42F3EDE4B87D0A187B16546C7E0A4E7F5F27047B1CFBB6371",
        ["raw-sensor-xtrans-artifacts"] = "72CA14FA9EEB5214DECBA86C97E677BB0E7A2F1D262433708AF3D90CF715077B",
        ["raw-sensor-xtrans-sample"] = "1B952FE14816D045743E154C9F2C29E4A8DC36769B4C150EE560ADABB958B686",
        ["resize-linear"] = "C68A1E9DCBC6112BA6FC6E078FB2823289BEB5EB1B8E21F91E3469EB9E45B502",
        ["saturation-capture-1023"] = "1AE4381422FDC79E10B3393BD406FFAF235893278ADBDF6EEA3191119E739779",
        ["saturation-capture-q16"] = "30101E5C1A378A9143238FB2E999A6A87BE2520041FFB5D75191C6DE02806661",
        ["tonelut-matrix-agx"] = "50BFE8CA498DDCC130B0F378D40371EEF6CFA8B6EAA5101A8777FB8E13E69E36",
        ["tonelut-single"] = "7631C15C8F3FEBE955512FF04BABD9BEBC6625BDE4E6E99CC5B6B36BA630423F",
        ["tonelut-srgb-transfer"] = "2C6B476F270983AE135394FB8FC1AFA09DF6BDABF150A124CB56BF95D6C840A1",
        ["working-space-srgb-to-rec2020"] = "B89CD09E95F465989D91EB1CE009644DD6CB3BDFFA370697977D4819E3A6A0CA",
    };

    private void AssertFrozen(string key, string hash)
    {
        output.WriteLine($"HASHCONTROL {key} {hash}");
        var windows = OperatingSystem.IsWindows() ? Frozen.GetValueOrDefault(key, "") : "";
        var expected = PlatformRenderGoldens.Expected("hashcontrol", key, windows);
        Assert.SkipWhen(
            expected.Length == 0 && !OperatingSystem.IsWindows(),
            "HASHCONTROL values are frozen on Windows; this platform has none recorded.");
        Assert.Equal(expected, hash);
    }

    private static ushort[] Samples(int width, int height, uint seed)
    {
        var samples = new ushort[width * height * 3];
        var state = seed;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)

        for (var channel = 0; channel < 3; channel++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            var ramp = channel switch
            {
                0 => x * 65535.0 / (width - 1),
                1 => y * 65535.0 / (height - 1),
                _ => (x + y) * 65535.0 / (width + height - 2)
            };
            var noise = (int)(state % 8192) - 4096;
            samples[(y * width + x) * 3 + channel] =
                (ushort)Math.Clamp((int)ramp + noise, 0, ushort.MaxValue);
        }

        return samples;
    }

    private static MagickImage CreateImage(int width, int height, uint seed) =>
        RawBaseLoader.ImportRgb16(
            MemoryMarshal.AsBytes(Samples(width, height, seed).AsSpan()),
            width,
            height);

    private static string ImageHash(MagickImage image)
    {
        using var hash = new HashBuilder();
        hash.AddInt32((int)image.Width);
        hash.AddInt32((int)image.Height);
        hash.AddUInt16s(RenderPipelineTestSupport.ReadPixels(image));

        return hash.Finish();
    }

    private static void AddMask(HashBuilder hash, SourceSaturationMask mask)
    {
        hash.AddInt32(mask.Width);
        hash.AddInt32(mask.Height);
        var flags = new byte[mask.Width * mask.Height];
        for (var y = 0; y < mask.Height; y++)
        for (var x = 0; x < mask.Width; x++)
            flags[y * mask.Width + x] = mask.GetFlags(x, y);
        hash.AddBytes(flags);
    }

    private static EffectsSettings Effects() => new()
    {
        Vignette = -37,
        Midpoint = 61,
        Grain = 42,
        GrainSize = GrainSize.Medium
    };

    private static RenderExecutionOptions Resting(int cap) =>
        RenderExecutionOptions.Resting(CancellationToken.None, cap, _ => { });

    private static int[] Caps() => [1, 2, Environment.ProcessorCount];

    private sealed class HashBuilder : IDisposable
    {
        private readonly IncrementalHash _hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        internal void AddBytes(ReadOnlySpan<byte> values) => _hash.AppendData(values);

        internal void AddInt32(int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            _hash.AppendData(bytes);
        }

        internal void AddInt64(long value)
        {
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
            _hash.AppendData(bytes);
        }

        internal void AddDouble(double value) =>
            AddInt64(BitConverter.DoubleToInt64Bits(value));

        internal void AddUInt16s(ReadOnlySpan<ushort> values)
        {
            var bytes = new byte[values.Length * 2];
            for (var i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2, 2), values[i]);
            _hash.AppendData(bytes);
        }

        internal void AddInt32s(ReadOnlySpan<int> values)
        {
            var bytes = new byte[values.Length * 4];
            for (var i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4, 4), values[i]);
            _hash.AppendData(bytes);
        }

        internal void AddDoubles(ReadOnlySpan<double> values)
        {
            var bytes = new byte[values.Length * 8];

            for (var i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteInt64LittleEndian(
                    bytes.AsSpan(i * 8, 8),
                    BitConverter.DoubleToInt64Bits(values[i]));
            }

            _hash.AppendData(bytes);
        }

        internal void AddMatrix(double[,] matrix)
        {
            AddInt32(matrix.GetLength(0));
            AddInt32(matrix.GetLength(1));
            for (var row = 0; row < matrix.GetLength(0); row++)
            for (var column = 0; column < matrix.GetLength(1); column++)
                AddDouble(matrix[row, column]);
        }

        internal string Finish() => Convert.ToHexString(_hash.GetHashAndReset());

        public void Dispose() => _hash.Dispose();
    }
}
