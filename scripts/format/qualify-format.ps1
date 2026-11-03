# Qualifies the pinned formatter commands on this machine. Run after install-format-tools.ps1; it downloads
# nothing, and every HTTP(S) attempt made through the proxy it sets is counted, including this shell's own:
# start it with POWERSHELL_TELEMETRY_OPTOUT=1. Exit 0: every acceptance property held; 1: a property was
# violated; 3: a formatter entrypoint is missing (capability absent, never a pass).
[CmdletBinding()]
param(
    [string]$ToolRoot,
    [string]$EvidencePath,
    [string]$CompareWith,
    [string]$DriftReport,
    [string]$FormatRoot = $PSScriptRoot
)

$version = $PSVersionTable.PSVersion

if ($version.Major -lt 7 -or ($version.Major -eq 7 -and $version.Minor -lt 4)) {
    Write-Output 'VERSION: PowerShell 7.4 or later is required'
    exit 1
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$FormatRoot = [IO.Path]::GetFullPath($FormatRoot)
$entrypoints = @('install-format-tools.ps1', 'check-format.ps1', 'apply-format.ps1', 'FormatTools.psm1')
$missing = @($entrypoints | Where-Object { -not (Test-Path -LiteralPath (Join-Path $FormatRoot $_) -PathType Leaf) })

if ($missing.Count) {
    $missing | ForEach-Object { Write-Host "MISSING $_" }
    Write-Host "RESULT missing-capability: $($missing.Count) of $($entrypoints.Count) formatter entrypoints absent"
    exit 3
}

. "$PSScriptRoot/QualifyHelpers.ps1"
. "$PSScriptRoot/QualifyControls.ps1"
. "$PSScriptRoot/QualifyEvidence.ps1"
$script:FormatRoot = $FormatRoot
$script:RepoRoot = [IO.Path]::GetFullPath((Join-Path $FormatRoot '../..'))
$script:ToolRoot = $ToolRoot

if (-not $script:ToolRoot) {
    $script:ToolRoot = Join-Path $script:RepoRoot '.tools/format'
}

$script:ToolRoot = [IO.Path]::GetFullPath($script:ToolRoot)

foreach ($output in @($EvidencePath, $DriftReport) | Where-Object { $_ }) {
    $full = [IO.Path]::GetFullPath($output)

    if ($full.StartsWith($script:RepoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Evidence must be written outside the repository: $output"
    }
}

$script:Cases = Get-Content -LiteralPath (Join-Path $FormatRoot 'fixtures/cases.json') -Raw | ConvertFrom-Json
$script:Properties = [Collections.Generic.List[object]]::new()
$script:Ledger = [Collections.Generic.List[string]]::new()
$script:Hashes = [ordered]@{}
$script:Families = @{}
$script:RepoCount = 0
$script:RestoreBuild = 0
$script:Pwsh = (Get-Process -Id $PID).Path
$script:Scratch = Join-Path ([IO.Path]::GetTempPath()) ('hp-qualify-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($script:Scratch)
$hostBefore = Get-HostState
$toolsBefore = Get-TreeState $script:ToolRoot
$requests = @()
Start-ProxySink

try {
    Test-ParserCases
    Test-ContentControls
    Test-SelectionControls
    Test-ToolControls
    Test-WriteInterference
    Test-ProcessControls
    Test-GuardControls
    Test-RealSource
    Test-SelfCheck

    if ($DriftReport) {
        Invoke-DriftReport $DriftReport
    }

    Test-Invocations
} finally {
    $requests = @(Stop-ProxySink)
    Remove-Item -LiteralPath $script:Scratch -Recurse -Force -ErrorAction SilentlyContinue
}

$connections = $requests.Count
[void](Add-Property 'no network attempts observed' ($connections -eq 0) ($requests -join '; '))
[void](Add-Property 'no persistent host changes' ((Get-HostState) -ceq $hostBefore))
[void](Add-Property 'installed tools unchanged' ((Get-TreeState $script:ToolRoot) -ceq $toolsBefore))
$exercised = @('csharp', 'powershell', 'prettier' | Where-Object { $script:Families.ContainsKey($_) -and $script:Families[$_] })
[void](Add-Property 'all three formatter families exercised' ($exercised.Count -eq 3) ($exercised -join ','))

if ($CompareWith) {
    $other = Get-Content -LiteralPath $CompareWith -Raw | ConvertFrom-Json
    $keys = @($script:Hashes.Keys) + @($other.hashes.PSObject.Properties.Name) | Sort-Object -Unique

    foreach ($key in $keys) {
        $theirs = $other.hashes.PSObject.Properties[$key]
        $same = $script:Hashes.Contains($key) -and $theirs -and $theirs.Value -ceq $script:Hashes[$key]
        [void](Add-Property "output hash matches $($other.os): $key" $same)
    }
}

$violated = @($script:Properties | Where-Object { -not $_.Passed }).Count
$digest = Get-Sha256 (Get-Utf8Bytes ((@($script:Hashes.Keys) | ForEach-Object { "$_=$($script:Hashes[$_])" }) -join "`n"))
$os = [Runtime.InteropServices.RuntimeInformation]::OSDescription

if ($EvidencePath) {
    $evidence = [ordered]@{
        os = $os
        pwsh = $PSVersionTable.PSVersion.ToString()
        node = (& node --version)
        pins = Get-Content -LiteralPath (Join-Path $FormatRoot 'config/versions.json') -Raw | ConvertFrom-Json
        violated = $violated
        families = $exercised
        restoreBuildInvocations = $script:RestoreBuild
        proxyConnections = $connections
        proxyRequests = $requests
        hashDigest = $digest
        hashes = $script:Hashes
        properties = $script:Properties
    }
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($EvidencePath), ($evidence | ConvertTo-Json -Depth 6) + "`n")
}

Write-Host "RESULT os=$os properties=$($script:Properties.Count) violated=$violated families=$($exercised.Count)/3 restoreBuild=$($script:RestoreBuild) proxyConnections=$connections hashDigest=$digest"

if ($violated) {
    exit 1
}

exit 0
