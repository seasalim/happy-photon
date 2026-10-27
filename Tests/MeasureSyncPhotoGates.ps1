param(
    [ValidateSet('G2', 'G3', 'G4', 'Sanity')][string]$Gate = 'Sanity',
    [ValidateRange(1, 120)][int]$TimeoutSeconds = 120,
    [ValidateRange(1, 600)][int]$BudgetSeconds = 600,
    [switch]$Transfer,
    [switch]$Probe
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'HappyPhoton.Tests.csproj'
$assembly = Join-Path $PSScriptRoot 'bin/Release/net10.0/HappyPhoton.Tests.dll'
if (!(Test-Path $assembly)) { throw 'Run dotnet build HappyPhoton.sln -c Release first.' }
$built = (Get-Item $assembly).LastWriteTimeUtc
if (Get-ChildItem (Join-Path $PSScriptRoot 'SyncPhoto*.cs') | Where-Object LastWriteTimeUtc -gt $built) {
    throw 'Rebuild the Release assembly after changing the sync photo instruments.'
}
$oldPerf = $env:HAPPY_PHOTON_PERF
$oldCpu = $env:HAPPY_PHOTON_FULL_CPU
$oldTransfer = $env:HAPPY_PHOTON_SYNC_PHOTO_TRANSFER
$budget = [Diagnostics.Stopwatch]::StartNew()
$records = [Collections.Generic.List[string]]::new()
$filters = switch ($Gate) {
    'G2' { 'FullyQualifiedName~SyncPhotoBatchGateTests.BatchControl' }
    'G3' { 'FullyQualifiedName~SyncPhotoBatchGateTests.BatchControl' }
    'G4' { 'FullyQualifiedName~SyncPhotoHeaderGateTests.G4' }
    'Sanity' { 'FullyQualifiedName~SyncPhotoBatchGateTests.BatchControl' }
}
if ($Transfer) {
    if ($Gate -notin @('G2', 'G3')) { throw 'Transfer requires G2 or G3.' }

    $filters = 'FullyQualifiedName~SyncPhotoBatchGateTests.LocalsTransfer_AfterImplementation'
}
$runs = if ($Probe -or $Gate -eq 'Sanity') { 1 } elseif ($Gate -eq 'G3') { 5 } else { 3 }
try {
    # The orchestrator owns the exclusive measurement lock.
    $env:HAPPY_PHOTON_SYNC_PHOTO_TRANSFER = if ($Transfer) { '1' } else { '0' }
    $env:HAPPY_PHOTON_PERF = '1'
    $env:HAPPY_PHOTON_FULL_CPU = '1'

    for ($sample = 0; $sample -lt $runs; $sample++) {
        $remaining = [Math]::Floor($BudgetSeconds - $budget.Elapsed.TotalSeconds)
        if ($remaining -le 0) { throw 'DEFERRED: gate budget exhausted.' }
        $limit = [Math]::Min($TimeoutSeconds, $remaining)
        $log = Join-Path ([IO.Path]::GetTempPath()) "sync-photo-$Gate-$([Guid]::NewGuid().ToString('N')).log"
        $arguments = @('test', "`"$project`"", '-c', 'Release', '--no-build', '--no-restore',
            '--filter', "`"$filters`"", '--logger', '"console;verbosity=detailed"')
        $process = Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $repo -PassThru `
            -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError "$log.err"

        try {
            if (!$process.WaitForExit($limit * 1000)) {
                $process.Kill($true)
                $process.WaitForExit()
                throw "DEFERRED: sample exceeded $limit seconds. Log: $log"
            }

            $lines = Get-Content -LiteralPath $log
            $lines | Where-Object { $_ -match 'SYNC_PHOTO |brush_history |Failed|Passed|Error|Error Message|Assert\.' } | Write-Host
            Get-Content -LiteralPath "$log.err" | Write-Host
            if ($process.ExitCode -ne 0) { throw "Gate failed. Log: $log" }
            $found = @($lines | Where-Object { $_ -match 'SYNC_PHOTO |brush_history ' })
            if ($found.Count -eq 0) { throw 'No records; skipped tests are not measurements.' }
            foreach ($line in $found) { $records.Add($line.Trim()) }
        }
        finally {
            if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
            $process.Dispose()
        }

        Write-Host "SAMPLE gate=$Gate run=$($sample + 1) cumulativeSeconds=$($budget.Elapsed.TotalSeconds) log=$log"
        if ($sample -eq 0 -and $budget.Elapsed.TotalSeconds * $runs -gt $BudgetSeconds) {
            throw 'DEFERRED: first sample predicts exceeding the gate budget.'
        }
    }

    $metrics = switch ($Gate) {
        'G2' { @('growthBytes') }
        'G3' { @('milliseconds') }
        'G4' { @('rawMedianMs', 'heicMedianMs') }
        default { @() }
    }

    foreach ($metric in $metrics) {
        $values = @($records | ForEach-Object {
            if ($_ -match "\b$metric=([0-9.]+)") { [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture) }
        } | Sort-Object)
        if ($values.Count -eq 0) { throw "Missing metric $metric" }
        $middle = [int][Math]::Floor($values.Count / 2)
        $median = if ($values.Count % 2) { $values[$middle] } else { ($values[$middle - 1] + $values[$middle]) / 2 }
        Write-Output "SYNC_PHOTO_MEDIAN gate=$Gate metric=$metric median=$median count=$($values.Count) values=$($values -join ',')"
        if ($Transfer -and $Gate -eq 'G3' -and $values.Count -eq 5 -and $median -gt 1000) {
            throw "G3 median $median exceeds 1,000 ms."
        }
    }
}
finally {
    $env:HAPPY_PHOTON_PERF = $oldPerf
    $env:HAPPY_PHOTON_FULL_CPU = $oldCpu
    $env:HAPPY_PHOTON_SYNC_PHOTO_TRANSFER = $oldTransfer
}

