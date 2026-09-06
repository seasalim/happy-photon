param([ValidateSet('G1','G2','G5','G9','G9Stage','QualifiedG5','QualifiedRadialG9Eight')][string]$Gate = 'G1',
    [ValidateSet('raw','standard','synthetic')][string]$Fixture = 'raw',
    [ValidateRange(1,100)][int]$Samples = 5,
    [ValidateRange(1,120)][int]$TimeoutSeconds = 120)
$ErrorActionPreference = 'Stop'
$env:HAPPY_PHOTON_PERF = '1'
$env:HAPPY_PHOTON_FULL_CPU = '1'
$env:HAPPY_PHOTON_LOCALS_FIXTURE = $Fixture
$env:LOCALS_SAMPLES = "$Samples"
$log = Join-Path $PSScriptRoot "locals-$Gate-$Fixture-$Samples.log"
$err = "$log.err"
$argsList = @('test', 'Tests/HappyPhoton.Tests.csproj', '--configuration', 'Release', '--no-build', '--no-restore',
    '--filter', "FullyQualifiedName=HappyPhoton.Tests.LocalsFusedBaselineTests.$Gate", '--logger', '"console;verbosity=detailed"')
$process = Start-Process dotnet -ArgumentList $argsList -PassThru -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError $err
try {
    if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        Get-Content $log, $err
        throw "DEFERRED: $Gate/$Fixture timed out at $TimeoutSeconds seconds"
    }
    Get-Content $log, $err
    if ($process.ExitCode -ne 0) { throw "Measurement failed: $($process.ExitCode)" }
}
finally {
    $process.Dispose()
    Remove-Item -LiteralPath $log, $err -ErrorAction SilentlyContinue
}
