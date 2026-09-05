param(
    [ValidateSet('Build', 'Goldens', 'Kernels', 'Contention', 'Cancellation')][string]$Measurement = 'Goldens',
    [int]$TimeoutSeconds = 120,
    [string]$Label = 'probe',
    [switch]$Baseline
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$logs = Join-Path $PSScriptRoot 'bin/measure-render-sequence'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$env:HAPPY_PHOTON_FULL_CPU = '1'
$env:HAPPY_PHOTON_PERF = '1'
$env:HAPPY_PHOTON_BASELINE = if ($Baseline) { '1' } else { '0' }
$arguments = if ($Measurement -eq 'Build') {
    @('build', 'HappyPhoton.sln', '--configuration', 'Release')
} else {
    $filter = switch ($Measurement) {
        Goldens { 'FullyQualifiedName~RenderSequenceGoldenTests' }
        Kernels { 'FullyQualifiedName~RenderSequenceKernelTimingTests' }
        Contention { 'FullyQualifiedName~RenderSequenceContentionTests' }
        Cancellation { 'FullyQualifiedName~RestingRenderExecutionTests' }
    }
    @('test', 'Tests/HappyPhoton.Tests.csproj', '--configuration', 'Release',
        '--no-build', '--no-restore', '--filter', $filter, '--logger', 'console;verbosity=detailed')
}
$stdout = Join-Path $logs "$Measurement-$Label.out.txt"
$stderr = Join-Path $logs "$Measurement-$Label.err.txt"
$timer = [Diagnostics.Stopwatch]::StartNew()
$process = Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $repo `
    -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill($true)
    $process.WaitForExit()
    throw "$Measurement exceeded ${TimeoutSeconds}s; process tree terminated. Logs: $stdout"
}
Get-Content $stdout
Get-Content $stderr
Write-Output ("Elapsed: {0:F3}s; exit: {1}" -f $timer.Elapsed.TotalSeconds, $process.ExitCode)
if ($process.ExitCode -ne 0) { throw "$Measurement failed ($($process.ExitCode))" }
