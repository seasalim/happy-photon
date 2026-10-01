param(
    [Parameter(Mandatory = $true)][string] $Filter,
    [Parameter(Mandatory = $true)][string] $Name,
    [switch] $Freeze
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts/318-raw-orientation'
$null = New-Item -ItemType Directory -Force $out
$env:HAPPY_PHOTON_PERF = '1'
$env:HAPPY_PHOTON_FULL_CPU = '1'
$env:HAPPY_PHOTON_ORIENTATION_MEASURE = '1'
$env:HAPPY_PHOTON_ORIENTATION_FREEZE = if ($Freeze) { '1' } else { '0' }
$env:HAPPY_PHOTON_ORIENTATION_RUN = $Name
$info = [Diagnostics.ProcessStartInfo]::new('dotnet')
$info.WorkingDirectory = $root
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
foreach ($arg in @('test', 'Tests/HappyPhoton.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore', '--filter', $Filter, '--logger', 'console;verbosity=detailed')) {
    $info.ArgumentList.Add($arg)
}
$watch = [Diagnostics.Stopwatch]::StartNew()
$process = [Diagnostics.Process]::Start($info)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
$timedOut = -not $process.WaitForExit(110000)
if ($timedOut) {
    $process.Kill($true)
    if (-not $process.WaitForExit(5000)) { throw 'Process tree did not stop after timeout' }
}
$log = $stdout.WaitAsync([TimeSpan]::FromSeconds(2)).GetAwaiter().GetResult() + $stderr.WaitAsync([TimeSpan]::FromSeconds(2)).GetAwaiter().GetResult()
[IO.File]::WriteAllText((Join-Path $out "$Name.log"), $log)
Write-Output $log
Write-Output ("SAMPLE name={0} elapsedSeconds={1:F3} timedOut={2} exit={3}" -f $Name, $watch.Elapsed.TotalSeconds, $timedOut, $process.ExitCode)
if ($timedOut) { exit 124 }
exit $process.ExitCode
