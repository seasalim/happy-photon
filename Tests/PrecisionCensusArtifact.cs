using System.Text;

namespace HappyPhoton.Tests;

internal sealed class PrecisionCensusArtifact : IDisposable
{
    private readonly FileStream _stream;
    private readonly StreamWriter _writer;
    private readonly StringBuilder _payload = new();

    public PrecisionCensusArtifact(
        string path,
        PrecisionCensusManifest manifest,
        bool openMpControlled = true)
    {
        var fullPath = Path.GetFullPath(
            path,
            GoldenTestPaths.RepositoryRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _stream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            4096,
            FileOptions.WriteThrough);
        _writer = new StreamWriter(
            _stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true)
        {
            NewLine = "\n"
        };
        AppendLine($"CENSUS_MANIFEST schema={manifest.SchemaVersion} " +
            $"sha256={manifest.Digest}");
        AppendLine("CENSUS_SCOPE working-storage-boundaries-only " +
            "phase1Selection=none candidatesNotSelected=P1-Q16,P1-FP,P1-X");
        AppendLine(openMpControlled
            ? "CENSUS_EXECUTION_PIN openMpThreads=1 openMpDynamic=false " +
                "purpose=required-for-repeatable-raw-decode"
            : "CENSUS_EXECUTION_PIN openMpThreads=uncontrolled " +
                "openMpDynamic=uncontrolled " +
                "purpose=unpinned-repeatability-check");
        foreach (var expected in manifest.ExpectedCases)
        {
            AppendLine($"CENSUS_EXPECTED case={expected}");
        }
        foreach (var expected in PrecisionExpectedEvidence.Create(manifest))
        {
            AppendLine("CENSUS_EXPECTED_EVIDENCE " +
                $"case={expected.Case} population={expected.Population} " +
                $"boundary={expected.Boundary}");
        }
        Flush();
    }

    public string Payload => _payload.ToString();

    public void RecordCase(
        string caseName,
        StringBuilder casePayload,
        bool succeeded)
    {
        var content = casePayload.ToString().TrimEnd('\r', '\n');
        if (content.Length > 0)
        {
            foreach (var line in content.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n'))
            {
                AppendLine(line);
            }
        }
        if (succeeded)
        {
            AppendLine($"CENSUS_COMPLETE case={caseName}");
        }
        Flush();
    }

    public void Flush()
    {
        _writer.Flush();
        _stream.Flush(flushToDisk: true);
    }

    private void AppendLine(string value)
    {
        _payload.AppendLine(value);
        _writer.WriteLine(value);
    }

    public void Dispose()
    {
        _writer.Dispose();
        _stream.Dispose();
    }
}
