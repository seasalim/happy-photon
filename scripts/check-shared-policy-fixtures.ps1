[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'run-shared-policy-tests.ps1'
$classes = @(Get-Content -Raw (Join-Path $PSScriptRoot 'shared-policy-tests.json') | ConvertFrom-Json)
$root = Join-Path (Split-Path $PSScriptRoot) "artifacts/shared-policy-fixtures/$([Guid]::NewGuid().ToString('N'))"

# Exercise the real reader with synthetic TRX results and a successful test-command exit.
function dotnet {
    $global:LASTEXITCODE = 0
    Write-Output 'Synthetic shared-policy test command.'
}

$cases = @(
    @{ name = 'passed'; outcome = ''; missing = $false; skipOnly = $false; error = '' },
    @{ name = 'skipped'; outcome = 'NotExecuted'; missing = $false; skipOnly = $false; error = '' },
    @{ name = 'missing'; outcome = ''; missing = $true; skipOnly = $false; error = "missing executed TRX results: $($classes[0])" },
    @{ name = 'skip-only'; outcome = ''; missing = $false; skipOnly = $true; error = "missing executed TRX results: $($classes[0])" }
)

foreach ($outcome in @('Failed', 'Error', 'Timeout', 'Aborted', 'Inconclusive')) {
    $cases += @{ name = $outcome; outcome = $outcome; missing = $false; skipOnly = $false; error = "Sentinel: $outcome" }
}

foreach ($case in $cases) {
    $directory = Join-Path $root $case.name
    $null = New-Item -ItemType Directory -Path $directory -Force
    $definitions = [Collections.Generic.List[string]]::new()
    $results = [Collections.Generic.List[string]]::new()

    for ($i = 0; $i -lt $classes.Count; $i++) {
        if ($case.missing -and $i -eq 0) { continue }

        $class = $classes[$i]
        $outcome = if ($case.skipOnly -and $i -eq 0) { 'NotExecuted' } else { 'Passed' }
        $definitions.Add("<UnitTest id='$i'><TestMethod className='$class' /></UnitTest>")
        $results.Add("<UnitTestResult testId='$i' testName='$class.Executed' outcome='$outcome' />")
    }

    if ($case.outcome) {
        $definitions.Add("<UnitTest id='sentinel'><TestMethod className='$($classes[0])' /></UnitTest>")
        $results.Add("<UnitTestResult testId='sentinel' testName='$($classes[0]).Sentinel' outcome='$($case.outcome)' />")
    }

    $xml = "<TestRun><TestDefinitions>$($definitions -join '')</TestDefinitions><Results>$($results -join '')</Results></TestRun>"
    Set-Content -LiteralPath (Join-Path $directory 'probe.trx') -Value $xml
    $failure = ''
    $report = ''

    try {
        $report = & $runner -TestArguments @('--no-build') -RunDirectory $directory -LogFilePrefix 'probe' 6>&1 | Out-String
    } catch {
        $failure = $_.Exception.Message
    }

    if ($case.error) {
        if (-not $failure.Contains($case.error)) {
            throw "Fixture $($case.name) expected '$($case.error)', got '$failure'."
        }
    } elseif ($failure -or -not $report.Contains("$($classes.Count)/$($classes.Count) classes executed")) {
        throw "Fixture $($case.name) failed: $failure $report"
    }

    Set-Content -LiteralPath (Join-Path $directory 'reader-result.txt') -Value "$report$failure"
    Write-Host "Shared-policy reader fixture passed: $($case.name)"
}

Write-Host "Shared-policy reader fixtures passed ($($cases.Count)); evidence: $root"
