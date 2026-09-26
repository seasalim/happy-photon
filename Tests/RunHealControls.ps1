param(
    [ValidateSet('C1PreviewParity','C2ProofExportParity','C3LoupeRefinement','C4FitPrivateBytes')]
    [string]$Control = 'C3LoupeRefinement',
    [ValidateSet('raw','standard')][string]$Fixture = 'raw',
    [ValidateRange(1,5)][int]$Samples = 5,
    [ValidateRange(1,120)][int]$TimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$env:HAPPY_PHOTON_PERF = '1'
$env:HAPPY_PHOTON_FULL_CPU = '1'
$env:HAPPY_PHOTON_HEAL_FIXTURE = $Fixture
if ($Control -ne 'C1PreviewParity' -and $Fixture -ne 'raw') { throw 'C2/C3/C4 require the 20 MP RAW' }
if ($Control -in @('C1PreviewParity','C2ProofExportParity') -and $Samples -ne 1) {
    throw 'Deterministic controls need -Samples 1'
}
$values = [Collections.Generic.List[double]]::new()
$culture = [Globalization.CultureInfo]::InvariantCulture
for ($sample = 1; $sample -le $Samples; $sample++) {
    # Every sample is a fresh process; transient logs stay in system temp.
    $log = Join-Path ([IO.Path]::GetTempPath()) "happy-photon-heal-$([guid]::NewGuid().ToString('N')).log"
    $err = "$log.err"
    $argsList = @('test', 'Tests/HappyPhoton.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore',
        '--filter', "FullyQualifiedName=HappyPhoton.Tests.HealControlBaselineTests.$Control",
        '--logger', '"console;verbosity=detailed"')
    $process = Start-Process dotnet -ArgumentList $argsList -WorkingDirectory $repo -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $log -RedirectStandardError $err
    try {
        if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            Get-Content -LiteralPath $log, $err
            throw "DEFERRED: $Control sample $sample timed out at $TimeoutSeconds seconds"
        }
        $lines = Get-Content -LiteralPath $log
        $lines
        Get-Content -LiteralPath $err
        if ($process.ExitCode -ne 0) { throw "Control failed: $($process.ExitCode)" }
        $records = @($lines | Where-Object { $_ -match 'HEAL_CONTROL' })
        if ($records.Count -eq 0) { throw 'No control measurement (test may have skipped)' }
        $field = if ($Control -eq 'C3LoupeRefinement') { 'seconds' } else { 'fit_private_bytes' }
        if ($Control -in @('C3LoupeRefinement','C4FitPrivateBytes')) {
            if ($records.Count -ne 1 -or $records[0] -notmatch "\b$field=([0-9.Ee+-]+)") {
                throw "Missing or ambiguous $field measurement"
            }
            $value = [double]::Parse($Matches[1], $culture)
            $values.Add($value)
            Write-Output "HEAL_SAMPLE control=$Control sample=$sample $field=$($value.ToString('R', $culture))"
        }
    }
    finally {
        if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
        Remove-Item -LiteralPath $log, $err -ErrorAction SilentlyContinue
    }
}
if ($values.Count -gt 0) {
    $sorted = @($values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    $median = if ($sorted.Count % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
    $kind = if ($Samples -eq 1) { 'smoke_only' } else { 'repeated_samples' }
    Write-Output "HEAL_MEDIAN control=$Control samples=$Samples $kind median_$field=$($median.ToString('R', $culture))"
}
