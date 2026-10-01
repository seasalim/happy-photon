[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $EntryName,
    [Parameter(Mandatory)][int] $X,
    [Parameter(Mandatory)][int] $Y,
    [Parameter(Mandatory)][string] $EvidencePrefix,
    [switch] $ExpectBlocked
)

# Run with Windows PowerShell 5.1 against a disposable catalog and a visible entry.
# Coordinates are physical client pixels, as in app-window.ps1. The caller owns
# launch/teardown and must bound the app process to 120 seconds.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/app-window.ps1"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [Windows.Automation.AutomationElement]::FromHandle($script:AppHwnd)

function Find-Entry {
    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::NameProperty, $EntryName)
    $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}

function Read-Entry {
    $entry = Find-Entry
    if ($null -eq $entry) { throw "Entry not found: $EntryName" }

    $entry.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
}

function Assert-Text([string] $Expected) {
    $actual = Read-Entry
    if ($actual -ne $Expected) { throw "Expected [$Expected], found [$actual] in $EntryName" }
}

Send-AppClick $X $Y
$entry = Find-Entry
if ($null -eq $entry -or -not $entry.Current.HasKeyboardFocus) {
    throw "The requested entry is not focused: $EntryName"
}

# Home/Delete works without modifier state, which posted app-window keys lack.
Send-AppKeys 0x24
Send-AppKeys 0x2E -Repeat 12
Assert-Text ''
Send-AppKeys 0x33, 0x35
$typed = Read-Entry
Save-AppShot "$EvidencePrefix-typed.png"
Write-Output "$EntryName after 35: [$typed]"

if ($ExpectBlocked) {
    Assert-Text ''
    Send-AppKeys 0x0D
    Save-AppShot "$EvidencePrefix-applied.png"

    return
}

Assert-Text '35'
# Each of these is an application shortcut. It must stay in the focused field.
Send-AppKeys 0x44, 0x47, 0x50, 0x58
Assert-Text '35dgpx'
Send-AppKeys 0x24, 0x2E
Assert-Text '5dgpx'
Send-AppKeys 0x2E -Repeat 5
Assert-Text ''
Send-AppKeys 0x33, 0x35
Assert-Text '35'
Send-AppKeys 0x0D
Save-AppShot "$EvidencePrefix-applied.png"
Write-Output "${EntryName}: typed 35, shadowed D/G/P/X/Delete, and pressed Enter"
