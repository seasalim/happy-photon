[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'test-process-sampling.ps1')

function Counter([double] $Time, [double] $Cpu) {
    return [pscustomobject]@{ before = $Time; after = $Time; cpu = $Cpu }
}

function Require([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$execution = Get-TestHostInvocationEvidence @('-automated', '-explicit', 'Off', '-run', '-3:serialized-case')
Require ($execution -ceq 'xunit-v3-automated-run') 'An explicit serialized test execution must be proven.'

foreach ($arguments in @(
    @('-assemblyInfo'), @('-automated', '-list', 'Discovery', '-preEnumerateTheories'),
    @('-list', 'full/json'), @('--server'), @('-automated'),
    @('-automated', '-run'), @('-automated', '-run', '-list'),
    @('-automated', '-run', '-3:case', '-list', 'Discovery')
)) {
    Require ((Get-TestHostInvocationEvidence $arguments) -ceq '') "Nonexecution invocation was proven: $arguments"
}

$start = [DateTimeOffset]::Parse('2026-01-01T00:00:00Z')
$job = @(Counter 0 0; Counter 5 150; Counter 10 200)
$driver = @{ Key = 'driver'; Kind = 'driver'; Samples = @(Counter 0 0; Counter 5 50; Counter 10 70) }
$test = @{ Key = 'test'; Kind = 'test'; Samples = @(Counter 0 0; Counter 5 80; Counter 10 90) }
$summary = Get-TestCpuWindowSummary $job @($driver, $test) 3 $start
Require ($summary.upper.processors -eq 20) 'Upper must include unseen processes and subtract only proven driver CPU.'
Require ($summary.lower.processors -eq 16) 'Lower must contain only proven test-tree CPU.'
Require ($summary.command.processors -eq 30) 'Whole-command CPU must remain separate.'
Require ($summary.peakChildProcesses -eq 3) 'Child-process peak was lost.'
Require ($summary.upper.startUtc -eq $start.ToString('o')) 'Peak window time is wrong.'

# A discovery helper must neither trigger the relaxed arm-A rule nor reduce the upper bound.
$discovery = @{ Key = 'discovery'; Kind = 'unknown'; Samples = @(Counter 0 0; Counter 5 130) }
$summary = Get-TestCpuWindowSummary $job @($discovery) 0 $start
Require ($summary.lower.processors -eq 0) 'Discovery-only host CPU must not enter the proven lower bound.'
Require ($summary.upper.processors -eq 30) 'Discovery-only host CPU must remain in the upper bound.'

# A driver first observed after the window starts cannot subtract its earlier lifetime CPU.
$late = @{ Key = 'late'; Kind = 'driver'; Samples = @(Counter 3 90; Counter 5 100) }
$summary = Get-TestCpuWindowSummary $job @($late) 0 $start
Require ($summary.upper.processors -eq 28) 'Late driver discovery incorrectly enabled a within-budget result.'

# A retained final counter contributes after the child has exited.
$exited = @{ Key = 'exited'; Kind = 'test'; Samples = @(Counter 1 0; Counter 2 130) }
$summary = Get-TestCpuWindowSummary $job @($exited) 1 $start
Require ($summary.lower.processors -eq 26) 'Exited child CPU was lost from the lower bound.'

# Irregular sampling brackets a five-second window rather than smoothing a CPU burst.
$irregular = @(Counter 0 0; Counter 4.8 0; Counter 5.2 130)
$summary = Get-TestCpuWindowSummary $irregular @() 0 $start
Require ($summary.upper.processors -eq 26) 'An unsampled burst was smoothed below budget.'
Require ($summary.lower.processors -eq 0) 'Unseen CPU cannot enable the arm-A budget clause.'
$fractionalJob = @(Counter 0 0; Counter 5 3.75)
$fractionalDriver = @{ Key = 'driver'; Kind = 'driver'; Samples = @(Counter 0 0; Counter 5 0.25) }
$fractionalTest = @{ Key = 'test'; Kind = 'test'; Samples = @(Counter 0 0; Counter 5 0.125) }
$summary = Get-TestCpuWindowSummary $fractionalJob @($fractionalDriver, $fractionalTest) 0 $start
Require ([Math]::Abs($summary.upper.processors - 0.7) -lt 0.00001) 'CPU fractions were rounded in the upper bound.'
Require ([Math]::Abs($summary.lower.processors - 0.025) -lt 0.00001) 'CPU fractions were rounded in the lower bound.'
Write-Host 'CPU bound fixtures passed (unseen processes, late drivers, exited children and irregular sampling).'

. (Join-Path $PSScriptRoot 'test-processor-budget.ps1')

function Reject([scriptblock] $Action, [string] $Message) {
    $caught = ''

    try { & $Action | Out-Null }
    catch { $caught = $_.Exception.Message }

    Require ($caught.Contains($Message)) "Expected '$Message'; got '$caught'."
}

$fullCpu = $env:HAPPY_PHOTON_FULL_CPU

try {
    $env:HAPPY_PHOTON_FULL_CPU = '0'
    Assert-TestProcessorBudget @(8, 8, 2, 2, 2, 2)
    Assert-TestProcessorBudget @(3, 3, 2, 2, 2, 2)
    Assert-TestProcessorBudget @(8, 8, 8)
    Assert-TestProcessorBudget @(8, 8, 8, 8) -Serial
    Reject { Assert-TestProcessorBudget @(0) } 'between 1 and 8'
    Reject { Assert-TestProcessorBudget @(9) } 'between 1 and 8'
    Reject { Assert-TestProcessorBudget @(9) -Serial } 'between 1 and 8'
    Reject { Assert-TestProcessorBudget @(8, 8, 8, 1) } 'maximum is 24'
    $env:HAPPY_PHOTON_FULL_CPU = '1'
    Assert-TestProcessorBudget @(32, 32, 32)
} finally {
    $env:HAPPY_PHOTON_FULL_CPU = $fullCpu
}

Write-Host 'Processor-budget fixtures passed (shards, overrides, concurrent/serial hosts and FullCpu exemption).'
