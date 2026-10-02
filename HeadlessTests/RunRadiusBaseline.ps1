[CmdletBinding()]
param(
    [ValidateSet("Build", "G1", "G2", "Policy")]
    [string] $Gate,
    [int] $TimeoutSeconds = 120,
    [int] $Sample = 1
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$logs = Join-Path $PSScriptRoot "bin/radius-baseline"
$null = New-Item -ItemType Directory -Force -Path $logs
$arguments = switch ($Gate) {
    "Build" { @("build", "HappyPhoton.sln", "-c", "Release") }
    "G1" { @("test", "HeadlessTests/HappyPhoton.Headless.Tests.csproj", "-c", "Release",
        "--no-build", "--no-restore", "--filter", "FullyQualifiedName~VisualsRadiusGateTests",
        "--logger", '"console;verbosity=detailed"') }
    "G2" { @("test", "Tests/HappyPhoton.Tests.csproj", "-c", "Release",
        "--no-build", "--no-restore", "--filter", '"FullyQualifiedName~ObserveRadiusRuleBaseline|FullyQualifiedName~RadiusMatcher"',
        "--logger", '"console;verbosity=detailed"') }
    "Policy" { @("-NoProfile", "-File", "scripts/verify.ps1", "-PolicyOnly") }
}
$executable = if ($Gate -eq "Policy") { "pwsh" } else { "dotnet" }
$stdout = Join-Path $logs "$Gate-$Sample.log"
$stderr = Join-Path $logs "$Gate-$Sample.err"
$clock = [Diagnostics.Stopwatch]::StartNew()
$process = Start-Process $executable -ArgumentList $arguments -WorkingDirectory $root `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden

if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill($true)
    $process.WaitForExit()
    Write-Output "$Gate sample=$Sample TIMEOUT ceiling=${TimeoutSeconds}s"
    exit 124
}

$process.WaitForExit()
Write-Output "$Gate sample=$Sample seconds=$($clock.Elapsed.TotalSeconds) exit=$($process.ExitCode)"
Get-Content $stdout
Get-Content $stderr
exit $process.ExitCode
