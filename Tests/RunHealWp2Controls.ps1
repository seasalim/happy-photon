# Frozen control definitions: docs/pipeline/TESTING.md#heal-wp2-frozen-controls (section 5).
param(
    [ValidateSet('Build','G1','G2','Policy')][string]$Gate,
    [ValidateRange(1,120)][int]$TimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.WorkingDirectory = Split-Path -Parent $PSScriptRoot
if ($Gate -eq 'Policy') {
    $startInfo.FileName = 'pwsh'
    $arguments = @('-NoProfile', '-File', './scripts/verify.ps1', '-PolicyOnly')
} else {
    $startInfo.FileName = 'dotnet'
    if ($Gate -eq 'Build') {
        $arguments = @('build', 'HappyPhoton.sln', '-c', 'Release')
    } else {
        $class = if ($Gate -eq 'G2') { 'HealHistoryControlTests' } else { 'HealSerializationControlTests' }
        $arguments = @('test', 'Tests/HappyPhoton.Tests.csproj', '-c', 'Release',
            '--no-build', '--no-restore', '--filter', "FullyQualifiedName~$class",
            '--logger', 'console;verbosity=detailed')
        $startInfo.Environment['HEAL_CONTROL'] = '1'
    }
}
foreach ($argument in $arguments) { $startInfo.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$clock = [Diagnostics.Stopwatch]::StartNew()
try {
    $null = $process.Start()
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw "DEFERRED: $Gate exceeded $TimeoutSeconds seconds"
    }
    $stdout.GetAwaiter().GetResult()
    $stderr.GetAwaiter().GetResult()
    Write-Host "$Gate elapsed $($clock.Elapsed.TotalSeconds) seconds"
    if ($process.ExitCode -ne 0) { throw "$Gate failed: $($process.ExitCode)" }
} finally { $process.Dispose() }

