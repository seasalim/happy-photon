[CmdletBinding()]
param(
    [ValidateRange(1, 10000)][int] $Runs = 50,
    [ValidateRange(1, 7200)][int] $BudgetSeconds = 600,
    [ValidateRange(1, 600)][int] $TimeoutSeconds = 120,
    [switch] $CpuLoad
)

# Caller holds the workflow host's exclusive measure lock. Build Release first.
# HAPPY_PHOTON_PERF is cleared: RealTermination uses its everyday 26-case fixture.
# The first sample probes whether the requested count fits the remaining budget.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$results = Join-Path $PSScriptRoot "TestResults/backup-crash-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $results
$timer = [Diagnostics.Stopwatch]::StartNew()
$workers = [Collections.Generic.List[Diagnostics.Process]]::new()
$samples = [Collections.Generic.List[double]]::new()
$process = $null
$failed = 0
$completed = 0
$deferred = $null
$previousPerf = [Environment]::GetEnvironmentVariable('HAPPY_PHOTON_PERF')
$previousChild = [Environment]::GetEnvironmentVariable('HAPPY_PHOTON_BACKUP_CHILD')
$previousKill = [Environment]::GetEnvironmentVariable('HAPPY_PHOTON_BACKUP_KILL')

try {
    $env:HAPPY_PHOTON_PERF = '0'
    $env:HAPPY_PHOTON_BACKUP_CHILD = $null
    $env:HAPPY_PHOTON_BACKUP_KILL = $null

    if ($CpuLoad) {
        $workerCode = @"
[Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'
"@
        $workerCode += [Environment]::NewLine + '$parent = [Diagnostics.Process]::GetProcessById(' + $PID + ')' + [Environment]::NewLine
        $workerCode += '$clock = [Diagnostics.Stopwatch]::StartNew(); [Console]::WriteLine("READY"); '
        $workerCode += 'while ($clock.Elapsed.TotalSeconds -lt ' + $BudgetSeconds + ' -and -not $parent.HasExited) { '
        $workerCode += 'for ($i = 0; $i -lt 10000; $i++) { $null = [Math]::Sqrt($i) } }'
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($workerCode))

        for ($core = 0; $core -lt [Environment]::ProcessorCount; $core++) {
            if ($timer.Elapsed.TotalSeconds -ge $BudgetSeconds) { throw 'Budget expired starting load workers.' }

            $start = @{
                FilePath = Join-Path $PSHOME 'pwsh.exe'
                ArgumentList = @('-NoProfile', '-NonInteractive', '-EncodedCommand', $encoded)
                PassThru = $true
                WindowStyle = 'Hidden'
                RedirectStandardOutput = Join-Path $results "load-$core.out"
                RedirectStandardError = Join-Path $results "load-$core.err"
            }
            $worker = Start-Process @start
            $workers.Add($worker)
            $worker.PriorityClass = 'BelowNormal'
        }

        # Readiness is observed, not assumed after a fixed settling delay.
        $readyLimit = [Math]::Min($BudgetSeconds, $TimeoutSeconds)

        while ($true) {
            $ready = @(Get-ChildItem $results -Filter 'load-*.out' |
                Where-Object { (Get-Content -Raw $_.FullName) -match 'READY' }).Count
            if ($ready -eq $workers.Count) { break }
            if (@($workers | Where-Object HasExited).Count) { throw 'A CPU load worker exited before readiness.' }
            if ($timer.Elapsed.TotalSeconds -ge $readyLimit) { throw 'CPU load readiness timed out.' }

            Start-Sleep -Milliseconds 50
        }
    }

    Write-Output "workload=RealTermination cases=26 cpu_load=$CpuLoad workers=$($workers.Count) priority=BelowNormal budget_s=$BudgetSeconds results=$results"

    for ($run = 1; $run -le $Runs; $run++) {
        $remaining = $BudgetSeconds - $timer.Elapsed.TotalSeconds

        if ($remaining -le 0) {
            $deferred = 'Budget exhausted before next sample.'
            break
        }

        if ($CpuLoad -and @($workers | Where-Object HasExited).Count) {
            throw 'A CPU load worker exited; loaded measurement is invalid.'
        }

        $stdout = Join-Path $results "run-$run.out"
        $stderr = Join-Path $results "run-$run.err"
        $xml = Join-Path $results "run-$run.xml"
        $arguments = @('Tests/bin/Release/net10.0/HappyPhoton.Tests.dll',
            '-method', 'HappyPhoton.Tests.BackupCrashTests.RealTermination_PreservesLiveCatalogAndCommitMarkers',
            '-noColor', '-showLiveOutput', '-reporter', 'verbose', '-xml', $xml)
        $sample = [Diagnostics.Stopwatch]::StartNew()
        $start = @{
            FilePath = 'dotnet'
            ArgumentList = $arguments
            WorkingDirectory = $repoRoot
            PassThru = $true
            WindowStyle = 'Hidden'
            RedirectStandardOutput = $stdout
            RedirectStandardError = $stderr
        }
        $process = Start-Process @start
        $limitMs = [int]([Math]::Min($TimeoutSeconds, $remaining) * 1000)

        if (-not $process.WaitForExit($limitMs)) {
            $process.Kill($true)
            $process.WaitForExit()
            $deferred = "Run $run exceeded its $limitMs ms timeout; process tree stopped."
            Get-Content $stdout, $stderr
            break
        }

        $sample.Stop()
        $completed++
        $samples.Add($sample.Elapsed.TotalMilliseconds)
        $assembly = $null

        try {
            [xml] $report = Get-Content -Raw $xml
            $assembly = $report.assemblies.assembly
        }
        catch {
            Write-Output "RESULT_READ_FAILURE run=$run error=$_"
        }

        $valid = [int]$assembly.total -eq 26 -and [int]$assembly.skipped -eq 0
        $failure = $process.ExitCode -ne 0 -or -not $valid
        Write-Output "run=$run elapsed_ms=$($sample.Elapsed.TotalMilliseconds.ToString('F3')) exit=$($process.ExitCode) total=$($assembly.total) failed=$($assembly.failed) skipped=$($assembly.skipped)"

        if ($failure) {
            $failed++
            Write-Output "FAILURE_EVIDENCE run=$run"
            Get-Content $stdout, $stderr
        }
        elseif ((Get-Content -Raw $stdout) -match 'BACKUP_DIAGNOSTIC') {
            Write-Output "NONFAILING_DIAGNOSTIC run=$run"
            Get-Content $stdout
        }

        $process.Dispose()
        $process = $null

        if ($run -eq 1 -and $Runs -gt 1) {
            $projected = $samples[0] / 1000 * ($Runs - 1)
            $remaining = $BudgetSeconds - $timer.Elapsed.TotalSeconds

            if ($projected -gt $remaining) {
                $deferred = "First sample projects $($projected.ToString('F3')) s for remaining $($Runs - 1) runs; only $($remaining.ToString('F3')) s remain."
                break
            }
        }
    }
}
finally {
    if ($null -ne $process) {
        if (-not $process.HasExited) { $process.Kill($true) }
        $process.WaitForExit()
        $process.Dispose()
    }

    foreach ($worker in $workers) {
        if (-not $worker.HasExited) { $worker.Kill($true) }
        $worker.WaitForExit()
        $worker.Dispose()
    }

    [Environment]::SetEnvironmentVariable('HAPPY_PHOTON_PERF', $previousPerf)
    [Environment]::SetEnvironmentVariable('HAPPY_PHOTON_BACKUP_CHILD', $previousChild)
    [Environment]::SetEnvironmentVariable('HAPPY_PHOTON_BACKUP_KILL', $previousKill)
}

$median = $null

if ($samples.Count) {
    $ordered = @($samples | Sort-Object)
    $middle = [int][Math]::Floor($ordered.Count / 2)
    $median = if ($ordered.Count % 2) { $ordered[$middle] } else {
        ($ordered[$middle - 1] + $ordered[$middle]) / 2
    }
}

Write-Output "SUMMARY requested=$Runs completed=$completed failing_runs=$failed median_ms=$median total_s=$($timer.Elapsed.TotalSeconds.ToString('F3'))"
if ($deferred) { Write-Output "DEFERRED: $deferred"; exit 2 }
if ($failed) { exit 1 }
