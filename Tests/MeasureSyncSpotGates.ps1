param(
    [ValidateSet('G1', 'G2', 'G3', 'Sanity')][string]$Gate = 'Sanity',
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
if (Get-ChildItem (Join-Path $PSScriptRoot 'SyncSpot*.cs') | Where-Object LastWriteTimeUtc -gt $built) {
    throw 'Rebuild after changing the sync spot instruments.'
}
$oldPerf = $env:HAPPY_PHOTON_PERF
$oldCpu = $env:HAPPY_PHOTON_FULL_CPU
$budget = [Diagnostics.Stopwatch]::StartNew()
$records = [Collections.Generic.List[string]]::new()
$filter = switch ($Gate) {
    'G1' { 'FullyQualifiedName~SyncSpotPasteGateTests.G1' }
    'G2' { 'FullyQualifiedName~SyncSpotPasteGateTests.G2|FullyQualifiedName~SyncSpotOrientationTests.G2' }
    'G3' { 'FullyQualifiedName~SyncSpotStorageTests.G3_SpotRemoval' }
    'Sanity' { 'FullyQualifiedName~SyncSpotControlTests.Sanity' }
}
$runs = if ($Probe) { 1 } else { 3 }
try {
    # The orchestrator owns the host measurement lock. Each sample gets a fresh process/catalog.
    $env:HAPPY_PHOTON_PERF = '1'
    $env:HAPPY_PHOTON_FULL_CPU = '1'

    for ($sample = 0; $sample -lt $runs; $sample++) {
        $remaining = [Math]::Floor($BudgetSeconds - $budget.Elapsed.TotalSeconds)
        if ($remaining -le 0) { throw 'DEFERRED: gate budget exhausted.' }
        $limit = [Math]::Min($TimeoutSeconds, $remaining)
        $log = Join-Path ([IO.Path]::GetTempPath()) "sync-spot-$Gate-$([Guid]::NewGuid().ToString('N')).log"
        $arguments = @('test', "`"$project`"", '-c', 'Release', '--no-build', '--no-restore',
            '--filter', "`"$filter`"", '--logger', '"console;verbosity=detailed"')
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $process = Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $repo -PassThru `
            -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError "$log.err"

        try {
            if (!$process.WaitForExit($limit * 1000)) {
                $process.Kill($true)
                $process.WaitForExit()
                throw "DEFERRED: sample exceeded $limit seconds. Log: $log"
            }

            $lines = Get-Content -LiteralPath $log
            $lines | Where-Object { $_ -match 'SYNC_SPOT |Failed|Passed|Error|Assert\.' } | Write-Host
            Get-Content -LiteralPath "$log.err" | Write-Host
            if ($process.ExitCode -ne 0) { throw "Gate failed. Log: $log" }
            $found = @($lines | Where-Object { $_ -match 'SYNC_SPOT ' })
            if ($found.Count -eq 0) { throw 'No records; skipped tests are not measurements.' }
            foreach ($line in $found) { $records.Add($line.Trim()) }
        }
        finally {
            if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
            $process.Dispose()
        }

        Write-Output "SAMPLE gate=$Gate run=$($sample + 1) seconds=$($watch.Elapsed.TotalSeconds) cumulativeSeconds=$($budget.Elapsed.TotalSeconds) log=$log"
        if ($sample -eq 0 -and $budget.Elapsed.TotalSeconds * $runs -gt $BudgetSeconds) {
            throw 'DEFERRED: first sample predicts exceeding the gate budget.'
        }
    }

    $metrics = switch ($Gate) {
        'G1' { @('differingCodes') }
        'G2' { @('centerErrorUnits') }
        'G3' { @('payloadBytes', 'growthBytes') }
        'Sanity' { @('mutationChecks', 'payloadBytes') }
    }

    foreach ($metric in $metrics) {
        $values = @($records | ForEach-Object {
            if ($_ -match "\b$metric=([0-9.]+)") { [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture) }
        } | Sort-Object)
        if ($values.Count -ne $runs) { throw "Missing or duplicate metric $metric" }
        $median = $values[[int][Math]::Floor($values.Count / 2)]
        Write-Output "SYNC_SPOT_MEDIAN gate=$Gate metric=$metric median=$median count=$($values.Count) values=$($values -join ',')"
    }

    if ($Gate -eq 'G2') {
        foreach ($orientation in @(3, 6, 8)) {
            $values = @($records | Where-Object { $_ -match "\bexif=$orientation " } | ForEach-Object {
                if ($_ -match '\bpixelMaxError=([0-9]+)') { [int]$Matches[1] }
            } | Sort-Object)
            if ($values.Count -ne $runs) { throw "Missing pixel samples for EXIF $orientation" }
            Write-Output "SYNC_SPOT_MEDIAN gate=G2 exif=$orientation metric=pixelMaxError median=$($values[[int][Math]::Floor($values.Count / 2)]) count=$runs values=$($values -join ',')"
        }
    }
}
finally {
    $env:HAPPY_PHOTON_PERF = $oldPerf
    $env:HAPPY_PHOTON_FULL_CPU = $oldCpu
}
