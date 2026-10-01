[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Traversal', 'Exposure', 'Kelvin')][string] $Scenario,
    [Parameter(Mandatory)][int] $X,
    [Parameter(Mandatory)][int] $Y,
    [Parameter(Mandatory)][string] $EvidencePrefix
)

# Windows PowerShell 5.1, disposable catalog, entry readout at physical client X/Y.
# Caller owns launch/teardown and bounds the app process to 120 seconds.
# Exposure briefly foregrounds the app to send a real Shift+Up chord.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/app-window.ps1"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [Windows.Automation.AutomationElement]::FromHandle($script:AppHwnd)

function Assert-Entry([string] $Name, [string] $Expected) {
    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::NameProperty, "$Name value")
    $entry = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $entry -or -not $entry.Current.HasKeyboardFocus) {
        throw "The requested entry is not focused: $Name"
    }

    $actual = $entry.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($actual -cne $Expected) { throw "Expected [$Expected], found [$actual] in $Name" }

    Write-Output "$Name UI field: [$actual]"
}

function Save-Step([string] $Step) {
    Save-AppShot "$EvidencePrefix-$Step.png"
}

function Send-ShiftUp {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class SliderKeyboardInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
"@
    $shell = New-Object -ComObject WScript.Shell
    [void]$shell.AppActivate($script:AppProcess.Id)
    [void][SliderKeyboardInput]::SetForegroundWindow($script:AppHwnd)
    $activation = [Diagnostics.Stopwatch]::StartNew()

    while ([SliderKeyboardInput]::GetForegroundWindow() -ne $script:AppHwnd -and
        $activation.Elapsed.TotalSeconds -lt 5) {
        Start-Sleep -Milliseconds 25
    }

    if ([SliderKeyboardInput]::GetForegroundWindow() -ne $script:AppHwnd) {
        throw 'Happy Photon must be foreground before sending Shift+Up.'
    }

    try {
        [SliderKeyboardInput]::keybd_event(0x10, 0x2A, 0, [UIntPtr]::Zero)
        [SliderKeyboardInput]::keybd_event(0x26, 0x48, 1, [UIntPtr]::Zero)
    } finally {
        [SliderKeyboardInput]::keybd_event(0x26, 0x48, 3, [UIntPtr]::Zero)
        [SliderKeyboardInput]::keybd_event(0x10, 0x2A, 2, [UIntPtr]::Zero)
    }

    # UIA observes completion; this ceiling only bounds a stalled input dispatch.
    $clock = [Diagnostics.Stopwatch]::StartNew()

    do {
        try {
            Assert-Entry 'Exposure' '+0.60'

            return
        } catch {
            if ($clock.Elapsed.TotalSeconds -ge 5) { throw }
            Start-Sleep -Milliseconds 25
        }
    } while ($true)
}

Save-Step '01-before'
Send-AppClick $X $Y
Save-Step '02-open'

switch ($Scenario) {
    'Traversal' {
        Assert-Entry 'Contrast' '0'
        Send-AppKeys 0x33, 0x35
        Assert-Entry 'Contrast' '35'
        Save-Step '03-contrast-35'
        Send-AppKeys 0x09
        Assert-Entry 'Saturation' '0'
        Save-Step '04-tab-saturation'
        Send-AppKeys 0x31, 0x32
        Assert-Entry 'Saturation' '12'
        Save-Step '05-saturation-12'
        Send-AppKeys 0x0D
        Save-Step '06-committed'
        Send-AppClick $X $Y
        Assert-Entry 'Contrast' '+35'
        Send-AppKeys 0x09
        Assert-Entry 'Saturation' '+12'
        Send-AppKeys 0x1B
    }

    'Exposure' {
        Assert-Entry 'Exposure' '0.00'
        Send-AppKeys 0x26
        Assert-Entry 'Exposure' '+0.05'
        Save-Step '03-up-one'
        Send-AppKeys 0x26
        Assert-Entry 'Exposure' '+0.10'
        Save-Step '04-up-two'
        Send-ShiftUp
        Save-Step '05-shift-up'
        Send-AppKeys 0x1B
        Save-Step '06-escape'
    }

    'Kelvin' {
        Assert-Entry 'Kelvin' '6500'
        Send-AppKeys 0x26
        Assert-Entry 'Kelvin' '6550'
        Save-Step '03-up'
        Send-AppKeys 0x1B
        Save-Step '04-escape'
    }
}
