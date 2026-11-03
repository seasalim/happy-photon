Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:FormatHome = $PSScriptRoot

. "$PSScriptRoot/FormatProcess.ps1"
. "$PSScriptRoot/FormatSelection.ps1"
. "$PSScriptRoot/FormatLanguages.ps1"
. "$PSScriptRoot/FormatPrettier.ps1"
. "$PSScriptRoot/FormatRun.ps1"

Export-ModuleMember -Function Invoke-FormatCommand, Invoke-FormatProcess, Get-ChildEnvironment, Get-FormatPins, Get-DefaultToolRoot
