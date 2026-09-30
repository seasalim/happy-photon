param(
    [ValidateSet('build', 'probe', 'showcases', 'policy', 'locals')][string] $Mode = 'probe',
    [switch] $Baseline
)
$ErrorActionPreference = 'Stop'
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = 'dotnet'
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
if ($Baseline) {
    $start.Environment['HAPPY_PHOTON_MASK_LIST_BASELINE'] = '1'
} else {
    $start.Environment.Remove('HAPPY_PHOTON_MASK_LIST_BASELINE') | Out-Null
}
switch ($Mode) {
    'build' { $arguments = @('build', 'HappyPhoton.sln', '-c', 'Release') }
    'policy' {
        $start.FileName = 'pwsh'
        $arguments = @('-NoProfile', '-File', 'scripts/verify.ps1', '-PolicyOnly')
    }
    default {
        $filter = switch ($Mode) {
            'showcases' { 'FullyQualifiedName~LocalsShowcaseTests.Render' }
            'locals' { 'FullyQualifiedName~LocalsViewModelTests|FullyQualifiedName~LocalsShowcaseTests' }
            default { 'FullyQualifiedName~MaskListBaselineTests' }
        }
        $arguments = @('test', 'HeadlessTests/HappyPhoton.Headless.Tests.csproj', '-c', 'Release',
            '--no-build', '--no-restore', '--filter', $filter, '--logger', 'console;verbosity=detailed')
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
