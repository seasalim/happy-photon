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

if (-not $SkipPolicy) {
    & (Join-Path $PSScriptRoot "check-source-lines.ps1")
    & (Join-Path $PSScriptRoot "check-test-waits.ps1")
}
if ($PolicyOnly) {
    return
}

if (-not $SkipQuarantine) {
    $quarantineArguments = @{ Configuration = $Configuration }
    if ($NoBuild) { $quarantineArguments.NoBuild = $true }
    if ($NoRestore) { $quarantineArguments.NoRestore = $true }
    & (Join-Path $PSScriptRoot "check-test-quarantine.ps1") `
        @quarantineArguments
}

$testArguments = @(
    "test",
    (Join-Path (Split-Path -Parent $PSScriptRoot) "HappyPhoton.sln"),
    "--configuration", $Configuration)
if ($NoBuild) { $testArguments += "--no-build" }
if ($NoRestore) { $testArguments += "--no-restore" }
if ($BlameHang) {
    $testArguments += @("--blame-hang", "--blame-hang-timeout", $BlameHangTimeout)
}
if (-not [string]::IsNullOrWhiteSpace($LogFilePrefix)) {
    $testArguments += @("--logger", "trx;LogFilePrefix=$LogFilePrefix")
}
if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $testArguments += @("--results-directory", $ResultsDirectory)
}

& dotnet @testArguments
if ($LASTEXITCODE -ne 0) {
    throw "Solution tests failed with exit code $LASTEXITCODE."
}
