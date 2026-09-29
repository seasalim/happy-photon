param(
    [ValidateSet('G1', 'G2', 'Sanity')][string]$Gate = 'Sanity',
    [ValidateRange(1, 120)][int]$TimeoutSeconds = 120,
    [ValidateRange(1, 600)][int]$BudgetSeconds = 600,
    [switch]$Probe
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'HappyPhoton.Tests.csproj'
$assembly = Join-Path $PSScriptRoot 'bin/Release/net10.0/HappyPhoton.Tests.dll'
if (!(Test-Path $assembly)) { throw 'Run dotnet build HappyPhoton.sln -c Release first.' }
$built = (Get-Item $assembly).LastWriteTimeUtc
if (Get-ChildItem (Join-Path $PSScriptRoot 'SyncProfile*.cs') | Where-Object LastWriteTimeUtc -gt $built) {
    throw 'Rebuild Release after changing the profile instruments.'
}
$oldPerf = $env:HAPPY_PHOTON_PERF
$oldCpu = $env:HAPPY_PHOTON_FULL_CPU
$budget = [Diagnostics.Stopwatch]::StartNew()
$records = [Collections.Generic.List[string]]::new()
$filters = if ($Gate -eq 'G2') { 'FullyQualifiedName~SyncProfileBatchTests.G2_' }
    elseif ($Gate -eq 'G1') { 'FullyQualifiedName~SyncProfileBatchTests.G1_ColdProfilePasteAddedCostIsWithinHeaderControl' }
    else { 'FullyQualifiedName~SyncProfileBatchTests.G1_|FullyQualifiedName~SyncProfileBatchTests.G2_' }
$runs = if ($Probe -or $Gate -eq 'Sanity') { 1 } else { 5 }
try {
    # Caller owns the exclusive measurement lock. Each sample is a new process/catalog/fixture set.
    $env:HAPPY_PHOTON_PERF = '1'
    $env:HAPPY_PHOTON_FULL_CPU = '1'

    for ($sample = 0; $sample -lt $runs; $sample++) {
        $remaining = [Math]::Floor($BudgetSeconds - $budget.Elapsed.TotalSeconds)
        if ($remaining -le 0) { throw 'DEFERRED: gate budget exhausted.' }
        $limit = [Math]::Min($TimeoutSeconds, $remaining)
        $log = Join-Path ([IO.Path]::GetTempPath()) "sync-profile-$Gate-$([Guid]::NewGuid().ToString('N')).log"
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
            $lines | Where-Object { $_ -match 'SYNC_PROFILE_TRANSFER |Failed|Passed|Error|Assert\.' } | Write-Host
            Get-Content -LiteralPath "$log.err" | Write-Host
            if ($process.ExitCode -ne 0) { throw "Gate failed. Log: $log" }
            $found = @($lines | Where-Object { $_ -match 'SYNC_PROFILE_TRANSFER ' })
            if ($found.Count -eq 0) { throw 'No records; skipped tests are not measurements.' }

            foreach ($line in $found) {
                $records.Add($line.Trim())
            }
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

    $modes = if ($Gate -eq 'G1') { @('cold') } elseif ($Gate -eq 'G2') { @('warm', 'automatic') }
        else { @('cold', 'warm', 'automatic') }

    foreach ($mode in $modes) {
        $metrics = @('pasteMs', 'readerOpens', 'baseDecodes')
        if ($mode -eq 'cold') { $metrics += @('controlMs', 'headerMs', 'addedMs') }

        foreach ($metric in $metrics) {
            $values = @($records | Where-Object { $_ -match "mode=$mode " } | ForEach-Object {
                if ($_ -match "\b$metric=(-?[0-9.]+)") { [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture) }
            } | Sort-Object)
            if ($values.Count -ne $runs) { throw "Missing samples for $mode $metric" }
            $middle = [int][Math]::Floor($values.Count / 2)
            $median = if ($values.Count % 2) { $values[$middle] } else { ($values[$middle - 1] + $values[$middle]) / 2 }
            Write-Output "SYNC_PROFILE_MEDIAN gate=$Gate mode=$mode metric=$metric median=$median count=$($values.Count) values=$($values -join ',')"
        }
    }
}
finally {
    $env:HAPPY_PHOTON_PERF = $oldPerf
    $env:HAPPY_PHOTON_FULL_CPU = $oldCpu
}
