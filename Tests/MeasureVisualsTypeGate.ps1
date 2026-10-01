[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Build', 'G1', 'G2', 'G3', 'Verify')]
    [string] $Gate,
    [ValidateRange(1, 120)]
    [int] $TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$start = [Diagnostics.ProcessStartInfo]::new()
$start.WorkingDirectory = $repoRoot
$start.UseShellExecute = $false
$start.FileName = 'dotnet'
$arguments = @('test', 'HeadlessTests/HappyPhoton.Headless.Tests.csproj', '-c', 'Release',
    '--no-build', '--no-restore', '--logger', 'console;verbosity=detailed', '--filter')

switch ($Gate) {
    'Build' { $arguments = @('build', 'HappyPhoton.sln', '-c', 'Release') }
    'G1' { $arguments += 'FullyQualifiedName~VisualsTypeGate' }
    'G2' {
        $names = @('SliderAndFooterMetricTests', 'DevelopHeaderBaselineTests',
            'DevelopCollapseBaselineTests', 'BrowseFooterLayoutTests', 'ExportVisualStyleTests',
            'SettingsDialogTests', 'ToolTipThemeTests', 'ThemeResourceTests')
        $arguments += ($names | ForEach-Object { "FullyQualifiedName~HappyPhoton.Tests.$_" }) -join '|'
    }
    'G3' {
        $arguments[1] = 'Tests/HappyPhoton.Tests.csproj'
        $arguments += 'FullyQualifiedName~ThemeSourceGuardTests.ObserveTypeRuleBaseline|FullyQualifiedName~ThemeSourceGuardTests.TypeMatcherRecognizesSourceForms'
    }
    'Verify' {
        $start.FileName = 'pwsh'
        $arguments = @('-NoProfile', '-File', 'scripts/verify.ps1', '-NoBuild', '-NoRestore',
            '-ResultsDirectory', 'Tests/TestResults/type-gate-verify')
    }
}

foreach ($argument in $arguments) {
    $start.ArgumentList.Add($argument)
}

# xUnit can outlive VSTest, so also identify newly started test executables on timeout.
$testExecutables = @(
    (Join-Path $repoRoot 'Tests/bin/Release/net10.0/HappyPhoton.Tests.exe'),
    (Join-Path $repoRoot 'HeadlessTests/bin/Release/net10.0/HappyPhoton.Headless.Tests.exe'),
    (Join-Path $repoRoot 'Interop/HappyPhoton.LibRaw.Interop.Tests/bin/Release/net10.0/HappyPhoton.LibRaw.Interop.Tests.exe')
) | ForEach-Object { [IO.Path]::GetFullPath($_) }
$existingTestIds = @(Get-Process | Where-Object { $_.Path -in $testExecutables } |
    Select-Object -ExpandProperty Id)
$startedAt = Get-Date
$clock = [Diagnostics.Stopwatch]::StartNew()
$process = [Diagnostics.Process]::Start($start)

try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        Get-Process | Where-Object {
            $_.Id -notin $existingTestIds -and $_.Path -in $testExecutables -and
            $_.StartTime -ge $startedAt
        } | Stop-Process -Force
        throw "$Gate timed out after $TimeoutSeconds seconds; its test processes were terminated."
    }

    Write-Host ("{0} elapsed={1:F3}s exit={2}" -f $Gate, $clock.Elapsed.TotalSeconds, $process.ExitCode)
    exit $process.ExitCode
}
finally {
    $process.Dispose()
}
