[CmdletBinding()]
param(
    [ValidateSet('All', 'Model', 'Locals', 'Crop', 'Geometry', 'RawProfile', 'Lens', 'Preset', 'DeletedPreset', 'CropDraft')]
    [string] $Case = 'All',
    [ValidateRange(1, 5)]
    [int] $Runs = 3,
    [ValidateRange(30, 240)]
    [int] $TimeoutSeconds = 120,
    [switch] $Record
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$logRoot = Join-Path $PSScriptRoot 'obj/SyncParity'
$null = New-Item -ItemType Directory -Force $logRoot
$cases = @('Model', 'Locals', 'Crop', 'Geometry', 'RawProfile', 'Lens', 'Preset', 'DeletedPreset', 'CropDraft')
if ($Case -ne 'All') { $cases = @($Case) }
if ($Record) { $Runs = 1 }
$previousRecord = $env:HAPPY_PHOTON_RECORD_SYNC_PARITY

try {
    $env:HAPPY_PHOTON_RECORD_SYNC_PARITY = if ($Record) { '1' } else { $null }
    for ($run = 1; $run -le $Runs; $run++) {
        foreach ($item in $cases) {
            $project = if ($item -eq 'Model') { 'Tests/HappyPhoton.Tests.csproj' } else { 'HeadlessTests/HappyPhoton.Headless.Tests.csproj' }
            $method = if ($item -eq 'Model') { 'G1_ModelTransferAndPresetBytes' } else { "G1_$item" }
            $class = if ($item -eq 'Model') { 'SyncTransferParityModelTests' } else { 'SyncTransferParityViewModelTests' }
            $label = if ($Record) { "record-$item" } else { "replay-$run-$item" }
            $stdout = Join-Path $logRoot "$label.log"
            $stderr = Join-Path $logRoot "$label.err"
            $arguments = @('test', $project, '-c', 'Release', '--no-build', '--no-restore',
                '--filter', "FullyQualifiedName=HappyPhoton.Tests.$class.$method",
                '--logger', '"console;verbosity=detailed"', '--results-directory', 'Tests/obj/SyncParity')
            $timer = [Diagnostics.Stopwatch]::StartNew()
            $process = Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $repo -WindowStyle Hidden `
                -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
            if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
                $process.Kill($true)
                throw "$label exceeded the $TimeoutSeconds-second process ceiling; see $stdout"
            }
            $timer.Stop()
            $text = [IO.File]::ReadAllText($stdout)
            Write-Host ("{0}: {1:F3} s, exit={2}" -f $label, $timer.Elapsed.TotalSeconds, $process.ExitCode)
            $text -split "`n" | Where-Object { $_ -match 'G1 (recorded|replay)' } | Write-Host
            if ($process.ExitCode -ne 0 -or $text -notmatch 'G1 (recorded|replay)') {
                Get-Content $stdout -Tail 55
                Get-Content $stderr -Tail 25
                throw "$label failed or did not execute the requested test"
            }
        }
    }
} finally {
    $env:HAPPY_PHOTON_RECORD_SYNC_PARITY = $previousRecord
}
