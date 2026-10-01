param(
    [ValidateSet('build', 'r1', 'r2', 'r3', 'policy')][string] $Mode = 'r1',
    [ValidateSet('baseline', 'post')][string] $Expectation = 'baseline'
)
$ErrorActionPreference = 'Stop'
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = 'dotnet'
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['HAPPY_PHOTON_ROTATE_MEASURE'] = $Expectation
switch ($Mode) {
    'build' { $arguments = @('build', 'HappyPhoton.sln', '-c', 'Release') }
    'policy' {
        $start.FileName = 'pwsh'
        $arguments = @('-NoProfile', '-File', 'scripts/verify.ps1', '-PolicyOnly')
    }
    default {
        $filter = switch ($Mode) {
            'r1' { 'FullyQualifiedName~ProvisionalRotateMeasurementTests.R1' }
            'r2' { 'FullyQualifiedName~ProvisionalRotateMeasurementTests.R2' }
            'r3' {
                (@('RapidRotatesCommitSeparatelyBeforeCropMode', 'OutOfOrderRotationRendersCommitInClickOrder',
                    'ClippingOutcomeCorrelationTests', 'RenderFailureRollbackTests', 'PreviewBitmapLifetimeTests',
                    'DisplayChainTraceTests', 'HistoryGeometryRaceTests', 'PreviewCacheOutcomeOrderingTests') |
                    ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
            }
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
Write-Output ("ROTATE {0}: exit={1}; wall={2:F3}s" -f $Mode, $process.ExitCode, $watch.Elapsed.TotalSeconds)
exit $process.ExitCode
