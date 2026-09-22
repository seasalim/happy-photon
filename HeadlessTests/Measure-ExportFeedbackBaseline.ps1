param([int]$Samples = 3, [int]$FirstSample = 1, [switch]$Build)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Set-Location $repoRoot
$logRoot = Join-Path $PSScriptRoot 'TestResults/ExportFeedbackBaseline'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
function Invoke-BoundedDotnet([string[]]$Arguments, [string]$Name) {
    $stdout = Join-Path $logRoot "$Name.stdout.log"
    $stderr = Join-Path $logRoot "$Name.stderr.log"
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process dotnet -ArgumentList $Arguments -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (-not $process.WaitForExit(120000)) {
        taskkill /PID $process.Id /T /F
        throw "$Name exceeded the 120-second process-tree timeout; defer remaining samples."
    }
    Get-Content $stdout
    Get-Content $stderr
    Write-Output "$Name wall time: $($elapsed.Elapsed.TotalSeconds) seconds"
    if ($process.ExitCode -ne 0) { throw "$Name exited $($process.ExitCode)" }
}
if ($Build) {
    Invoke-BoundedDotnet @('build', 'HappyPhoton.sln', '-c', 'Release') 'build'
}
foreach ($sample in $FirstSample..($FirstSample + $Samples - 1)) {
    Invoke-BoundedDotnet @('test', 'HeadlessTests/HappyPhoton.Headless.Tests.csproj',
        '-c', 'Release', '--no-build', '--no-restore',
        '--filter', 'FullyQualifiedName~ExportFeedbackBaselineTests',
        '--logger', '"console;verbosity=detailed"') "sample-$sample"
}

