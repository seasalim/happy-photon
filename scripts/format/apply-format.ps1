# Rewrites the named files whose only finding is DRIFT; every other finding is left for a person.
# Exit 0: all clean afterwards; 1: findings remain; 2: refused or could not run (nothing written).
[CmdletBinding()]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments)][string[]]$Path,
    [string]$RepoRoot,
    [string]$ToolRoot
)

$version = $PSVersionTable.PSVersion

if ($version.Major -lt 7 -or ($version.Major -eq 7 -and $version.Minor -lt 4)) {
    Write-Output 'VERSION: PowerShell 7.4 or later is required'
    exit 2
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'FormatTools.psm1') -Force
$result = @(Invoke-FormatCommand -Path $Path -RepoRoot $RepoRoot -ToolRoot $ToolRoot -Apply)[-1]
$result.Lines
exit $result.ExitCode
