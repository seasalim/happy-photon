[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $OutputPath,
    [Parameter(Mandatory)] [string] $ResultsDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$transcript = Get-Content -Raw -LiteralPath $OutputPath
if ($transcript -notmatch 'Test host process crashed|inactivity time') { return }

Write-Host "Test host abort detected before stable-results reconciliation."
$abortLines = Select-String -LiteralPath $OutputPath -Pattern 'Test host process crashed|inactivity time|The test running when the crash occurred:' -Context 0,1

foreach ($match in $abortLines) {
    Write-Host $match.Line

    if ($match.Line -match 'The test running when the crash occurred:') {
        $match.Context.PostContext | ForEach-Object { Write-Host $_ }
    }
}

$reported = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

foreach ($file in Get-ChildItem -LiteralPath $ResultsDirectory -Filter "Sequence_*.xml" -File -Recurse) {
    try {
        [xml]$sequence = Get-Content -Raw -LiteralPath $file.FullName

        foreach ($test in $sequence.SelectNodes("/TestSequence/Test[@Completed='False']")) {
            $assembly = ($test.GetAttribute("Source") -replace '\\', '/').Split('/')[-1]
            $name = $test.GetAttribute("DisplayName")
            if (-not $name) { $name = $test.GetAttribute("Name") }
            if (-not $assembly) { $assembly = "unknown host" }
            if (-not $name) { $name = "unknown test" }

            if ($reported.Add("$assembly|$name")) {
                Write-Host "Aborted host: $assembly; test running: $name; sequence: $($file.FullName)"
            }
        }
    } catch {
        Write-Warning "Could not read abort sequence $($file.FullName): $($_.Exception.Message)"
    }
}

if ($reported.Count -eq 0) {
    Write-Host "Aborted host/test unavailable from Sequence files; inspect the abort output above."
}
