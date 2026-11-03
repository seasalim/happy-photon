# Reports formatter findings for the named files, or with -Adopted for the adopted scope, without writing anything.
# Exit 0: all clean; 1: findings; 2: refused or could not run (no verdicts).
[CmdletBinding()]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments)][string[]]$Path,
    [switch]$Adopted,
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
$result = @(Invoke-FormatCommand -Path $Path -RepoRoot $RepoRoot -ToolRoot $ToolRoot -Adopted:$Adopted)[-1]
$result.Lines
exit $result.ExitCode
