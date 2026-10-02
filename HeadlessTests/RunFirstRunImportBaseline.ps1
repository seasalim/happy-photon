param(
    [ValidateSet('build', 'G2', 'G3', 'policy')][string] $Mode = 'G2',
    [ValidateRange(1, 120)][int] $TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = 'dotnet'
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['DOTNET_PROCESSOR_COUNT'] = '2'

switch ($Mode) {
    'build' { $arguments = @('build', 'HappyPhoton.sln', '-c', 'Release') }
    'policy' {
        $start.FileName = 'pwsh'
        $arguments = @('-NoProfile', '-File', 'scripts/verify.ps1', '-PolicyOnly')
    }
    default {
        $class = if ($Mode -eq 'G2') { 'FirstRunCardWidthBaselineTests' } else { 'ImportCloseSlotBaselineTests' }
        $arguments = @('test', 'HeadlessTests/HappyPhoton.Headless.Tests.csproj', '-c', 'Release',
            '--no-build', '--no-restore', '--logger', 'console;verbosity=detailed',
            '--filter', "FullyQualifiedName~$class")
    }
}

foreach ($argument in $arguments) {
    $start.ArgumentList.Add($argument)
}

$watch = [Diagnostics.Stopwatch]::StartNew()
$process = [Diagnostics.Process]::Start($start)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()

if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill($true)
    $process.WaitForExit()
    $stdout.Result
    $stderr.Result
    throw "$Mode exceeded $TimeoutSeconds seconds; process tree terminated"
}

$stdout.Result
$stderr.Result
Write-Output ("VISUALS-WP11 {0}: exit={1}; wall={2:F3}s" -f $Mode, $process.ExitCode, $watch.Elapsed.TotalSeconds)
exit $process.ExitCode
