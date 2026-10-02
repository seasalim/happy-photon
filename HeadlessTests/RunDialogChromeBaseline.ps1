param(
    [ValidateSet('build', 'probe', 'startup', 'headless', 'policy')][string] $Mode = 'probe',
    [ValidateRange(1, 600)][int] $TimeoutSeconds = 115
)

$ErrorActionPreference = 'Stop'
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = 'dotnet'
$start.UseShellExecute = $false
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
        $verbosity = if ($Mode -eq 'headless') { 'normal' } else { 'detailed' }
        $arguments = @('test', 'HeadlessTests/HappyPhoton.Headless.Tests.csproj', '-c', 'Release',
            '--no-build', '--no-restore', '--logger', "console;verbosity=$verbosity")

        if ($Mode -eq 'probe') {
            $arguments += @('--filter', 'FullyQualifiedName~DialogChromeBaselineTests')
        } elseif ($Mode -eq 'startup') {
            $start.Environment['HAPPY_PHOTON_DIALOG_STARTUP_CAPTURE'] = '1'
            $arguments += @('--filter', 'FullyQualifiedName~DialogChromeStartupBaselineTests')
        }
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
Write-Output ("DIALOG BASELINE {0}: exit={1}; wall={2:F3}s" -f $Mode, $process.ExitCode, $watch.Elapsed.TotalSeconds)
exit $process.ExitCode
