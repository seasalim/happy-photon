[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [switch] $NoBuild,
    [switch] $NoRestore,
    [switch] $PolicyOnly,
    [switch] $SkipPolicy,
    [switch] $SkipQuarantine,
    [switch] $BlameHang,
    [string] $BlameHangTimeout = "90s",
    [string] $LogFilePrefix = "",
    [string] $ResultsDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($PolicyOnly -and $SkipPolicy) {
    throw "PolicyOnly and SkipPolicy cannot be used together."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$clock = [Diagnostics.Stopwatch]::StartNew()
$buildSeconds = 0.0
$policySeconds = 0.0
$testSeconds = 0.0
$phase = "build"
try {
    if (-not $PolicyOnly -and -not $NoBuild) {
        $buildArguments = @("build", (Join-Path $repoRoot "HappyPhoton.sln"), "--configuration", $Configuration)
        if ($NoRestore) { $buildArguments += "--no-restore" }
        & dotnet @buildArguments
        if ($LASTEXITCODE -ne 0) { throw "Solution build failed with exit code $LASTEXITCODE." }
    }
    $buildSeconds = $clock.Elapsed.TotalSeconds
    $clock.Restart()
    $phase = "policy"
    if (-not $SkipPolicy) {
        & (Join-Path $PSScriptRoot "check-source-lines.ps1")
        & (Join-Path $PSScriptRoot "check-test-waits.ps1")
        & (Join-Path $PSScriptRoot "check-test-teardown.ps1")
        & (Join-Path $PSScriptRoot "check-test-teardown-fixtures.ps1")
        & (Join-Path $PSScriptRoot "check-test-sqlite-pools.ps1")
        & (Join-Path $PSScriptRoot "check-test-quarantine-fixtures.ps1")
    }
    if ($PolicyOnly) { return }

    $resultsRoot = if ($ResultsDirectory) { $ResultsDirectory } else { "artifacts/test-results" }
    if (-not [IO.Path]::IsPathRooted($resultsRoot)) { $resultsRoot = Join-Path $repoRoot $resultsRoot }
    $runDirectory = Join-Path $resultsRoot "verify-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))-$([Guid]::NewGuid().ToString('N'))"
    $null = New-Item -ItemType Directory -Path $runDirectory
    Write-Host "Current-run test evidence: $runDirectory"
    $checker = Join-Path $PSScriptRoot "check-test-quarantine.ps1"
    if (-not $SkipQuarantine) {
        & $checker -Configuration $Configuration -ResultsDirectory $runDirectory
    }
    $policySeconds = $clock.Elapsed.TotalSeconds
    $clock.Restart()
    $phase = "tests"
    $prefix = if ($LogFilePrefix) { $LogFilePrefix } else { "verify" }
    $testArguments = @(
        "test", (Join-Path $repoRoot "HappyPhoton.sln"),
        "--configuration", $Configuration, "--no-build", "--no-restore",
        "--logger", "trx;LogFilePrefix=$prefix", "--results-directory", $runDirectory)
    if ($BlameHang) {
        $testArguments += @("--blame-hang", "--blame-hang-timeout", $BlameHangTimeout)
    }
    & dotnet @testArguments
    $testExitCode = $LASTEXITCODE
    if (-not $SkipQuarantine) {
        & $checker -Mode StableResults -ResultsDirectory $runDirectory
    }
    if ($testExitCode -ne 0) { throw "Solution tests failed with exit code $testExitCode." }
} finally {
    switch ($phase) {
        "build" { $buildSeconds = $clock.Elapsed.TotalSeconds }
        "policy" { $policySeconds = $clock.Elapsed.TotalSeconds }
        "tests" { $testSeconds = $clock.Elapsed.TotalSeconds }
    }
    Write-Host ("Verification phases: build {0:F2}s; policy+discovery {1:F2}s; tests+reconciliation {2:F2}s; total {3:F2}s." -f `
        $buildSeconds, $policySeconds, $testSeconds, ($buildSeconds + $policySeconds + $testSeconds))
}
