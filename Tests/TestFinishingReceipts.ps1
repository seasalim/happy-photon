# Focused regression checks for the same aggregator used by RunFinishingGates.ps1.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'FinishingReceipts.ps1')
$candidatePath = [IO.Path]::GetTempFileName()
$original = '{"id":"builtin_regression","settings":{"version":4,"saturation":-7}}'
$changed = $original.Replace(':-7', ':-8')
$checks = 0

function Assert-Pass($receipt, [bool]$expected, [string]$case) {
    if ($receipt.pass -ne $expected) { throw "Unexpected receipt verdict: $case" }
    $script:checks++
}

try {
    foreach ($gate in 'G1','G2','G3','G4') {
        [IO.File]::WriteAllText($candidatePath, $original)
        $loadedBytes = [IO.File]::ReadAllBytes($candidatePath)
        $loadedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($loadedBytes))
        $assemblyHash = (Get-FileHash -LiteralPath $PSCommandPath).Hash
        $productionPath = Join-Path $PSScriptRoot 'FinishingReceipts.ps1'
        $productionHash = (Get-FileHash -LiteralPath $productionPath).Hash
        $count = switch ($gate) { 'G1' { 5 } 'G2' { 1 } 'G3' { 4 } 'G4' { 11 } }
        $rows = @(1..$count | ForEach-Object {
            @{ candidate = 'builtin_regression'; gate = $gate; pid = $_;
                candidateSha256 = $loadedHash; assemblySha256 = $assemblyHash; productionSha256 = $productionHash;
                values = @{ pass = $true; control = 30; active = 40; increment = 10; controlMin = 22; controlMax = 40 } }
        })
        $arguments = @{ Records = $rows; GateId = $gate; FixtureId = 'raw'; CandidateId = 'builtin_regression';
            CandidatePath = $candidatePath; AssemblyPath = $PSCommandPath; ProductionPath = $productionPath; LogStamp = 'test' }
        Assert-Pass (New-FinishingReceipt @arguments) $true "$gate unchanged"

        # The file changes after measurement loaded its bytes, before aggregation reads the file.
        [IO.File]::WriteAllText($candidatePath, $changed)
        $receipt = New-FinishingReceipt @arguments
        Assert-Pass $receipt $false "$gate changed between measurement and aggregation"
        if ($receipt.candidateSha256 -cne (Get-FileHash -LiteralPath $candidatePath).Hash) {
            throw 'Receipt does not identify the current file'
        }

        [IO.File]::WriteAllText($candidatePath, $original)

        foreach ($field in 'candidateSha256','assemblySha256','productionSha256') {
            $saved = $rows[-1][$field]
            $rows[-1][$field] = 'stale'
            Assert-Pass (New-FinishingReceipt @arguments) $false "$gate mixed $field"
            $rows[-1].Remove($field)
            Assert-Pass (New-FinishingReceipt @arguments) $false "$gate missing $field"
            $rows[-1][$field] = $saved
        }
    }
}
finally {
    Remove-Item -LiteralPath $candidatePath
}

Write-Output "Finishing receipt regression checks passed: $checks"
