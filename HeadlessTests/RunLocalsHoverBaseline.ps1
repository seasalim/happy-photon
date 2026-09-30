param([ValidateSet('build', 'locals', 'spots', 'policy')][string] $Mode = 'locals')
$ErrorActionPreference = 'Stop'
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = 'dotnet'
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
switch ($Mode) {
    'build' { $arguments = @('build', 'HappyPhoton.sln', '-c', 'Release') }
    'policy' {
        $start.FileName = 'pwsh'
        $arguments = @('-NoProfile', '-File', 'scripts/verify.ps1', '-PolicyOnly')
    }
    default {
        $filter = if ($Mode -eq 'spots') { 'SpotHoverBaselineTests' } else { 'LocalsHoverBaselineTests' }
        $arguments = @('test', 'HeadlessTests/HappyPhoton.Headless.Tests.csproj', '-c', 'Release',
            '--no-build', '--no-restore', '--filter', "FullyQualifiedName~$filter",
            '--logger', 'console;verbosity=detailed')
    }
}
foreach ($argument in $arguments) {
    $start.ArgumentList.Add($argument)
}
$watch = [Diagnostics.Stopwatch]::StartNew()
$process = [Diagnostics.Process]::Start($start)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
if (-not $process.WaitForExit(115000)) {
    $process.Kill($true)
    throw "$Mode exceeded 115 seconds; process tree terminated"
}
$stdout.Result
$stderr.Result
Write-Output ("BASELINE {0}: exit={1}; wall={2:F3}s" -f $Mode, $process.ExitCode, $watch.Elapsed.TotalSeconds)
exit $process.ExitCode
