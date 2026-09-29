# Caller holds the workflow's exclusive measure lease. This runner writes no evidence files.
param(
    [ValidateSet('G1Tick','G2Export','G3Parity','G4Clipping','Contracts')]
    [string]$Gate = 'G1Tick',
    [ValidateSet('raw','standard')][string]$Fixture = 'raw',
    [ValidateRange(1,5)][int]$Processes = 1,
    [ValidateRange(1,120)][int]$TimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$env:HAPPY_PHOTON_FINISHING_CONTROLS = '1'
$env:HAPPY_PHOTON_FULL_CPU = '1'
$env:HAPPY_PHOTON_OPS_FIXTURE = $Fixture
$filter = if ($Gate -eq 'Contracts') { 'FullyQualifiedName~FinishingLookTests' }
          else { "FullyQualifiedName=HappyPhoton.Tests.FinishingBaselineTests.$Gate" }
$budget = [Diagnostics.Stopwatch]::StartNew()
for ($sample = 1; $sample -le $Processes; $sample++) {
    $remaining = 600 - $budget.Elapsed.TotalSeconds
    if ($remaining -le 0) { throw 'Gate exceeded its ten-minute budget' }
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.WorkingDirectory = $repo
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('test','Tests/HappyPhoton.Tests.csproj','-c','Release','--no-build',
        '--no-restore','--filter',$filter,'--logger','console;verbosity=detailed')) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $limit = [Math]::Min($TimeoutSeconds, $remaining)
        if (!$process.WaitForExit([int]($limit * 1000))) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "$Gate sample timed out after $limit seconds"
        }
        $lines = $stdout.GetAwaiter().GetResult()
        $lines
        $stderr.GetAwaiter().GetResult()
        Write-Output ("INVOCATION gate={0} fixture={1} sample={2} wallSeconds={3:F3}" -f
            $Gate, $Fixture, $sample, $watch.Elapsed.TotalSeconds)
        if ($process.ExitCode -ne 0) { throw "$Gate failed ($($process.ExitCode))" }
        if ($Gate -ne 'Contracts' -and $lines -notmatch 'FINISHING_CONTROL ') {
            throw 'No control records; skipped tests are not evidence'
        }
    }
    finally {
        if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}

