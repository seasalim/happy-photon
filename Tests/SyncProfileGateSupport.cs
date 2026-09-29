using System.Security.Cryptography;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal static class SyncProfileGateSupport
{
    internal const string Lens = "Canon EF 50mm f/1.8 MkII";

    internal static readonly string[] Names =
        ["canon-eos-6d-iso-6400.cr2", "canon-eos-350d.cr2", "nikon-d70-burst-1.nef"];

    private static readonly string[] Hashes =
    [
        "7727EE0280B44EA1D633962F49942F37F3C7EC6D704D22E108A5223666327C32",
        "8CBB84E04D93B005FE082DA9C954122A612B5281AF00AA088D767850F343FD38",
        "DD6405AEB33B0CD5BF66C98BA98CCBB478A765450CFD130810E470DAB8D1F4B4"
    ];

    internal static string[] ConfirmFixtures()
    {
        var database = new LensfunDatabase(Path.Combine(GoldenTestPaths.RepositoryRoot, "data", "lensfun"));
        var paths = Names.Select(GoldenTestPaths.Asset).ToArray();

        for (var index = 0; index < paths.Length; index++)
        {
            RequireLocal(paths[index]);
            AssertHash(paths[index], Hashes[index]);
            using var raw = LibRawContext.Open(paths[index]);
            var metadata = raw.GetMetadata();
            var choices = database.ListCompatibleLenses(metadata.NormalizedMake, metadata.NormalizedModel);
            Assert.NotNull(choices.Camera);

            if (index < 2)
            {
                Assert.Contains(Lens, choices.Lenses);
            }
            else
            {
                Assert.DoesNotContain(Lens, choices.Lenses);
            }
        }

        return paths;
    }

    internal static string[] CopyTargets(string directory, string[] originals)
    {
        var paths = new string[200];

        for (var index = 0; index < paths.Length; index++)
        {
            var original = originals[index < 150 ? 0 : index < 180 ? 1 : 2];
            RequireLocal(original);
            paths[index] = Path.Combine(directory, $"target-{index:D3}{Path.GetExtension(original)}");
            File.Copy(original, paths[index]);
        }

        return paths;
    }

    internal static void RequireLocal(string path) =>
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));

    internal static void AssertHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(stream)));
    }

    internal static double Median(IEnumerable<double> samples)
    {
        var sorted = samples.Order().ToArray();
        var middle = sorted.Length / 2;

        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }
}

