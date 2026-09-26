using System.IO.Compression;
using System.Xml.Linq;
using HappyPhoton.MlSpike;

namespace MlSpike.Harness;

internal static class RuntimeEvidence
{
    internal static string Verify(string package)
    {
        var hash = LocalFiles.Hash(package);
        LocalFiles.Check(package);
        using var archive = ZipFile.OpenRead(package);
        var metadata = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using var stream = metadata.Open();
        var document = XDocument.Load(stream);
        var fields = document.Descendants().Where(e => e.Name.LocalName == "metadata").Single();
        string Value(string name) => fields.Elements().Single(e => e.Name.LocalName == name).Value;
        if (Value("id") != "Microsoft.ML.OnnxRuntime" || Value("version") != "1.30.0")
            throw new InvalidDataException("Runtime evidence must be Microsoft.ML.OnnxRuntime 1.30.0.");
        return hash;
    }
}
