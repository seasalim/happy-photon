[CmdletBinding()]
param(
    [ValidateRange(1, 120)][int] $TimeoutSeconds = 120,
    [string] $Method = "",
    [switch] $Probe
)

# Invoke under the workflow host's exclusive measure lock. Build Release first.
# Bounds the entire process tree, including fixture setup and all five samples.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$results = Join-Path $PSScriptRoot "TestResults/backup-baseline-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $results
$previous = [Environment]::GetEnvironmentVariable('HAPPY_PHOTON_PERF')
$previousProbe = [Environment]::GetEnvironmentVariable('HAPPY_PHOTON_PERF_PROBE')
try {
    $env:HAPPY_PHOTON_PERF = '1'
    $env:HAPPY_PHOTON_PERF_PROBE = if ($Probe) { '1' } else { '0' }
    # Execute xUnit in-process: the timeout owns the actual test process, without
    # VSTest's extra process layer potentially leaving an orphan test executable.
    $arguments = @('Tests/bin/Release/net10.0/HappyPhoton.Tests.dll',
        '-class', 'HappyPhoton.Tests.BackupBaselineTests', '-noColor',
        '-showLiveOutput', '-reporter', 'verbose')
    if ($Method) { $arguments += @('-method', $Method) }
    $process = Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $repoRoot `
        -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $results 'stdout.txt') `
        -RedirectStandardError (Join-Path $results 'stderr.txt')
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw "Backup baseline exceeded $TimeoutSeconds seconds; process tree stopped."
    }
    Get-Content (Join-Path $results 'stdout.txt'), (Join-Path $results 'stderr.txt')
    if ($process.ExitCode -ne 0) { throw "Backup baseline failed ($($process.ExitCode))." }
} finally {
    [Environment]::SetEnvironmentVariable('HAPPY_PHOTON_PERF', $previous)
    [Environment]::SetEnvironmentVariable('HAPPY_PHOTON_PERF_PROBE', $previousProbe)
}

