param(
    [ValidateSet('G1', 'G2', 'G3', 'Sanity')][string]$Gate = 'Sanity',
    [ValidateRange(1, 120)][int]$TimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'HeadlessTests/HappyPhoton.Headless.Tests.csproj'
$assembly = Join-Path $repo 'HeadlessTests/bin/Release/net10.0/HappyPhoton.Headless.Tests.dll'
if (!(Test-Path $assembly)) { throw 'Run dotnet build HappyPhoton.sln -c Release first.' }
$built = (Get-Item $assembly).LastWriteTimeUtc
if (Get-ChildItem (Join-Path $repo 'HeadlessTests/Paste*Gate*.cs') | Where-Object LastWriteTimeUtc -gt $built) {
    throw 'Rebuild the Release assembly after changing the paste instruments.'
}
$oldPerf = $env:HAPPY_PHOTON_PERF
$oldCpu = $env:HAPPY_PHOTON_FULL_CPU
$oldSanity = $env:HAPPY_PHOTON_PASTE_SANITY
$records = [Collections.Generic.List[object]]::new()
$budget = [Diagnostics.Stopwatch]::StartNew()
function RunSample([string]$filter) {
    $remaining = [Math]::Floor(600 - $budget.Elapsed.TotalSeconds)
    if ($remaining -le 0) { throw 'DEFERRED: ten-minute gate budget exhausted.' }
    $limit = [Math]::Min($TimeoutSeconds, $remaining)
    $log = Join-Path ([IO.Path]::GetTempPath()) "paste-gate-$([Guid]::NewGuid().ToString('N')).log"
    $arguments = @('test', "`"$project`"", '-c', 'Release', '--no-build', '--no-restore',
        '--filter', $filter, '--logger', '"console;verbosity=detailed"')
    $process = Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $repo -PassThru `
        -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    try {
        if (!$process.WaitForExit($limit * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "DEFERRED: sample exceeded $limit seconds. Log: $log"
        }
        $lines = Get-Content -LiteralPath $log
        $lines | Where-Object { $_ -match 'PASTE_GATE |G2 |Failed|Passed|Total tests|Error' } | Write-Host
        Get-Content -LiteralPath "$log.err" | Write-Host
        if ($process.ExitCode -ne 0) { throw "Gate failed. Log: $log" }
        $found = 0
        foreach ($line in $lines) {
            if ($line -match '^\s*PASTE_GATE (\{.*\})\s*$') {
                $records.Add(($Matches[1] | ConvertFrom-Json))
                $found++
            }
        }
        if ($found -eq 0) { throw 'No gate records; a skipped test is not a measurement.' }
    }
    finally {
        if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}
try {
    # The caller owns the exclusive host measurement lock. This runner does not acquire it.
    $env:HAPPY_PHOTON_PERF = if ($Gate -in @('G1', 'G3')) { '1' } else { '0' }
    $env:HAPPY_PHOTON_FULL_CPU = '1'
    $env:HAPPY_PHOTON_PASTE_SANITY = '1'
    switch ($Gate) {
        'G1' {
            for ($pair = 0; $pair -lt 5; $pair++) {
                RunSample 'FullyQualifiedName=HappyPhoton.Tests.PasteDevelopGateTests.G1_Raw'
                RunSample 'FullyQualifiedName=HappyPhoton.Tests.PasteDevelopGateTests.G1_Heic'
                if ($pair -eq 0 -and $budget.Elapsed.TotalSeconds * 5 -gt 600) {
                    throw 'DEFERRED: the first process pair predicts more than ten minutes.'
                }
            }
        }
        'G2' { RunSample 'FullyQualifiedName~PasteDevelopGateTests.G2_NoDecode' }
        'G3' {
            for ($sample = 0; $sample -lt 5; $sample++) {
                RunSample 'FullyQualifiedName=HappyPhoton.Tests.PasteDialogGateTests.G3_PasteSettings'
                if ($sample -eq 0 -and $budget.Elapsed.TotalSeconds * 5 -gt 600) {
                    throw 'DEFERRED: the first sample predicts more than ten minutes.'
                }
            }
        }
        'Sanity' {
            RunSample 'FullyQualifiedName~PasteDevelopGateTests.Sanity|FullyQualifiedName~PasteDialogGateTests.Sanity'
        }
    }
    if ($Gate -in @('G1', 'G3')) {
        foreach ($group in ($records | Group-Object fixture)) {
            $samples = @($group.Group)
            if ($samples.Count -ne 5 -or @($samples.pid | Sort-Object -Unique).Count -ne 5) {
                throw 'Each median requires five samples from five distinct test processes.'
            }
            $values = @($samples.milliseconds | Sort-Object)
            Write-Output "PASTE_MEDIAN gate=$Gate fixture=$($group.Name) ms=$($values[2]) samples=5"
        }
    }
}
finally {
    $env:HAPPY_PHOTON_PERF = $oldPerf
    $env:HAPPY_PHOTON_FULL_CPU = $oldCpu
    $env:HAPPY_PHOTON_PASTE_SANITY = $oldSanity
}
