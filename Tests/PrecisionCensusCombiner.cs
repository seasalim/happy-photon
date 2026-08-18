using System.Text;
using Xunit;

namespace HappyPhoton.Tests;

internal sealed record PrecisionCombinedResult(
    PrecisionCensusTerminal Terminal,
    string Statement,
    int EvidenceRows,
    int MaterialRows,
    int InvalidRows,
    int IndeterminateRows,
    int UnavailableAnalyticRows,
    int MissingCases,
    int MissingEvidenceRows,
    int UnexpectedEvidenceRows);

internal static class PrecisionCensusCombiner
{
    public static PrecisionCombinedResult Combine(
        byte[] first,
        byte[] second,
        IReadOnlyList<string> expectedCases)
    {
        if (!first.AsSpan().SequenceEqual(second))
        {
            return Result(
                PrecisionCensusTerminal.Invalid,
                0,
                0,
                1,
                0,
                0,
                expectedCases.Count,
                0,
                0,
                "byte-identical=false");
        }

        var lines = Encoding.UTF8.GetString(first)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
        var declared = lines.Where(line => line.StartsWith(
                "CENSUS_EXPECTED ", StringComparison.Ordinal))
            .Select(line => Tokens(line)["case"])
            .ToArray();
        var completed = lines.Where(line => line.StartsWith(
                "CENSUS_COMPLETE ", StringComparison.Ordinal))
            .Select(line => Tokens(line)["case"])
            .ToArray();
        var declaredEvidence = lines.Where(line => line.StartsWith(
                "CENSUS_EXPECTED_EVIDENCE ", StringComparison.Ordinal))
            .Select(Tokens)
            .Select(EvidenceKey)
            .ToArray();
        var evidence = lines.Where(line => line.StartsWith(
                "CENSUS_EVIDENCE ", StringComparison.Ordinal))
            .Select(Tokens)
            .ToArray();
        var structuralInvalid = 0;
        structuralInvalid += !declared.SequenceEqual(expectedCases) ? 1 : 0;
        structuralInvalid += !completed.SequenceEqual(expectedCases) ? 1 : 0;
        var missingCases = expectedCases.Count(expected =>
            !evidence.Any(row => row.GetValueOrDefault("case") == expected));
        structuralInvalid += missingCases;
        var actualEvidence = evidence.Select(EvidenceKey).ToArray();
        var missingEvidenceRows = declaredEvidence
            .Except(actualEvidence, StringComparer.Ordinal)
            .Count();
        var unexpectedEvidenceRows = actualEvidence
            .Except(declaredEvidence, StringComparer.Ordinal)
            .Count();
        var duplicateEvidenceRows = actualEvidence.Length -
            actualEvidence.Distinct(StringComparer.Ordinal).Count();
        structuralInvalid += declaredEvidence.Length == 0 ? 1 : 0;
        structuralInvalid += missingEvidenceRows + unexpectedEvidenceRows +
            duplicateEvidenceRows;

        var evidenceInvalid = 0;
        var material = 0;
        var indeterminateRows = 0;
        var unavailableAnalyticRows = 0;
        foreach (var row in evidence)
        {
            var oracle = row.GetValueOrDefault("oracle");
            if (oracle == "analytic")
            {
                var qualityState = row.GetValueOrDefault("qualityState");
                var qualityInapplicable = qualityState == "inapplicable" &&
                    row.GetValueOrDefault("qualityReason") ==
                    "fully-clipped-no-unclipped-pixels";
                if (row.GetValueOrDefault("clipState") != "available" ||
                    row.GetValueOrDefault("recoveryState") != "available" ||
                    qualityState == "unavailable" ||
                    qualityState == null)
                {
                    unavailableAnalyticRows++;
                }
                evidenceInvalid += row.GetValueOrDefault("clipState") !=
                    "available" ? 1 : 0;
                evidenceInvalid += row.GetValueOrDefault("recoveryState") !=
                    "available" ? 1 : 0;
                evidenceInvalid += qualityState != "available" &&
                    !qualityInapplicable ? 1 : 0;
                evidenceInvalid += qualityState == "available" &&
                    row.GetValueOrDefault("decisionBasis") !=
                    "exact-full-population" ? 1 : 0;
            }
            else if (oracle == "native-operator")
            {
                evidenceInvalid += row.GetValueOrDefault("storedChangeState") !=
                    "available" ? 1 : 0;
            }
            if (row.GetValueOrDefault("indeterminateCouldBeMaterial") == "true")
            {
                indeterminateRows++;
                evidenceInvalid++;
            }
            material += row.GetValueOrDefault("qualityState") == "available" &&
                row.GetValueOrDefault("p99Material") == "true" ? 1 : 0;
            material += row.GetValueOrDefault("recoveryState") == "available" &&
                ParseInt(row, "longestRecoverableRun") >= 8 ? 1 : 0;
            material += row.GetValueOrDefault("phaseZeroThresholdCrossed") ==
                "true" ? 1 : 0;
            material += row.GetValueOrDefault("plannedStageContractLoss") ==
                "true" ? 1 : 0;
        }

        var invalid = structuralInvalid + evidenceInvalid;
        var terminal = structuralInvalid > 0
            ? PrecisionCensusTerminal.Invalid
            : material > 0
                ? PrecisionCensusTerminal.Loss
                : evidenceInvalid > 0
                    ? PrecisionCensusTerminal.Invalid
                    : PrecisionCensusTerminal.Clean;
        return Result(
            terminal,
            evidence.Length,
            material,
            invalid,
            indeterminateRows,
            unavailableAnalyticRows,
            missingCases,
            missingEvidenceRows,
            unexpectedEvidenceRows,
            "byte-identical=true");
    }

    private static PrecisionCombinedResult Result(
        PrecisionCensusTerminal terminal,
        int evidenceRows,
        int materialRows,
        int invalidRows,
        int indeterminateRows,
        int unavailableAnalyticRows,
        int missingCases,
        int missingEvidenceRows,
        int unexpectedEvidenceRows,
        string repeatEvidence)
    {
        var outcome = terminal switch
        {
            PrecisionCensusTerminal.Clean => "P1A-CLEAN",
            PrecisionCensusTerminal.Loss => "P1A-LOSS",
            _ => "P1A-X"
        };
        var statement = $"{outcome} scope=working-storage-boundaries-only " +
            $"evidenceRows={evidenceRows} materialRows={materialRows} " +
            $"invalidConditions={invalidRows} " +
            $"indeterminateRows={indeterminateRows} " +
            $"unavailableAnalyticRows={unavailableAnalyticRows} " +
            $"missingCases={missingCases} " +
            $"missingEvidenceRows={missingEvidenceRows} " +
            $"unexpectedEvidenceRows={unexpectedEvidenceRows} {repeatEvidence} " +
            "phase1Selection=none selectsNone=P1-Q16,P1-FP,P1-X";
        return new PrecisionCombinedResult(
            terminal,
            statement,
            evidenceRows,
            materialRows,
            invalidRows,
            indeterminateRows,
            unavailableAnalyticRows,
            missingCases,
            missingEvidenceRows,
            unexpectedEvidenceRows);
    }

    private static Dictionary<string, string> Tokens(string line) => line
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Skip(1)
        .Select(token => token.Split('=', 2))
        .Where(parts => parts.Length == 2)
        .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

    private static int ParseInt(
        IReadOnlyDictionary<string, string> row,
        string key) =>
        int.TryParse(row.GetValueOrDefault(key), out var value) ? value : 0;

    private static string EvidenceKey(
        IReadOnlyDictionary<string, string> row) =>
        $"{row.GetValueOrDefault("case")}|" +
        $"{row.GetValueOrDefault("population")}|" +
        row.GetValueOrDefault("boundary");
}

public sealed class PrecisionCensusCombinerTests
{
    [Fact]
    public void Combiner_SelectsExactlyOneTerminalFromCompleteRepeatedEvidence()
    {
        var payload = Payload(Evidence());

        var result = PrecisionCensusCombiner.Combine(
            payload,
            payload,
            ["case"]);

        Assert.Equal(PrecisionCensusTerminal.Clean, result.Terminal);
        Assert.StartsWith("P1A-CLEAN ", result.Statement);
        Assert.DoesNotContain("P1A-LOSS", result.Statement);
        Assert.DoesNotContain("P1A-X scope", result.Statement);
    }

    [Theory]
    [InlineData("phaseZeroThresholdCrossed", "true")]
    [InlineData("plannedStageContractLoss", "true")]
    [InlineData("p99Material", "true")]
    [InlineData("longestRecoverableRun", "8")]
    public void Combiner_EachMaterialityBranchSelectsLoss(
        string key,
        string value)
    {
        var payload = Payload(Evidence().Replace(
            $"{key}={(key == "longestRecoverableRun" ? "0" : "false")}",
            $"{key}={value}",
            StringComparison.Ordinal));

        var result = PrecisionCensusCombiner.Combine(payload, payload, ["case"]);

        Assert.Equal(PrecisionCensusTerminal.Loss, result.Terminal);
    }

    [Theory]
    [InlineData("qualityState=unavailable")]
    [InlineData("indeterminateCouldBeMaterial=true")]
    public void Combiner_UnknownAnalyticEvidenceCannotSelectClean(string replacement)
    {
        var field = replacement.Split('=')[0];
        var original = field == "qualityState"
            ? "qualityState=available"
            : "indeterminateCouldBeMaterial=false";
        var payload = Payload(Evidence().Replace(
            original,
            replacement,
            StringComparison.Ordinal));

        var result = PrecisionCensusCombiner.Combine(payload, payload, ["case"]);

        Assert.Equal(PrecisionCensusTerminal.Invalid, result.Terminal);
    }

    [Fact]
    public void Combiner_ConfirmedMaterialLossPrecedesUnrelatedIndeterminacy()
    {
        var material = Evidence().Replace(
            "p99Material=false",
            "p99Material=true",
            StringComparison.Ordinal);
        var indeterminate = Evidence()
            .Replace("population=pop", "population=other", StringComparison.Ordinal)
            .Replace(
                "indeterminateCouldBeMaterial=false",
                "indeterminateCouldBeMaterial=true",
                StringComparison.Ordinal);
        var payload = Payload(
            material + "\n" + indeterminate,
            "pop|analytic", "other|analytic");

        var result = PrecisionCensusCombiner.Combine(payload, payload, ["case"]);

        Assert.Equal(PrecisionCensusTerminal.Loss, result.Terminal);
        Assert.Equal(1, result.MaterialRows);
        Assert.Equal(1, result.IndeterminateRows);
    }

    [Fact]
    public void Combiner_FullyClippedQualityIsInapplicableNotUnavailable()
    {
        var evidence = Evidence()
            .Replace("qualityState=available", "qualityState=inapplicable",
                StringComparison.Ordinal)
            .Replace("p99Material=false", "p99Material=null",
                StringComparison.Ordinal) +
            " qualityReason=fully-clipped-no-unclipped-pixels";
        var payload = Payload(evidence);

        var result = PrecisionCensusCombiner.Combine(payload, payload, ["case"]);

        Assert.Equal(PrecisionCensusTerminal.Clean, result.Terminal);
        Assert.Equal(0, result.UnavailableAnalyticRows);
    }

    [Fact]
    public void Combiner_InapplicableNativeOracleRequiresStoredChange()
    {
        var native = "CENSUS_EVIDENCE case=case population=pop boundary=native " +
            "oracle=native-operator required=stored-change clipState=inapplicable " +
            "recoveryState=inapplicable qualityState=inapplicable " +
            "storedChangeState=available";
        var payload = Payload(native);

        var result = PrecisionCensusCombiner.Combine(payload, payload, ["case"]);

        Assert.Equal(PrecisionCensusTerminal.Clean, result.Terminal);
    }

    private static string Evidence() =>
        "CENSUS_EVIDENCE case=case population=pop boundary=analytic " +
        "oracle=analytic required=clip,recovery,quality clipState=available " +
        "recoveryState=available qualityState=available " +
        "storedChangeState=inapplicable decisionBasis=exact-full-population " +
        "p99Material=false longestRecoverableRun=0 " +
        "phaseZeroThresholdCrossed=false plannedStageContractLoss=false " +
        "indeterminateCouldBeMaterial=false";

    private static byte[] Payload(
        string evidence,
        params string[] evidenceKeys) => Encoding.UTF8.GetBytes(
        "CENSUS_EXPECTED case=case\n" +
        string.Concat((evidenceKeys.Length == 0
                ? ["pop|" + (evidence.Contains(
                    "boundary=native", StringComparison.Ordinal)
                    ? "native"
                    : "analytic")]
                : evidenceKeys)
            .Select(key => key.Split('|'))
            .Select(parts => "CENSUS_EXPECTED_EVIDENCE case=case population=" +
                parts[0] + " boundary=" + parts[1] + "\n")) +
        evidence + "\nCENSUS_COMPLETE case=case\n");
}
