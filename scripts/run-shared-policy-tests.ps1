[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]] $TestArguments,
    [Parameter(Mandatory)] [string] $RunDirectory,
    [Parameter(Mandatory)] [string] $LogFilePrefix,
    [int] $ProcessorCount
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$classes = @(Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'shared-policy-tests.json') | ConvertFrom-Json)

if ($classes.Count -eq 0 -or @($classes | Sort-Object -Unique).Count -ne $classes.Count) {
    throw 'The shared-policy list must be nonempty and contain distinct classes.'
}

foreach ($class in $classes) {
    if ($class -notmatch '^HappyPhoton\.Tests\.[A-Za-z0-9_]+$') { throw "Invalid shared-policy class: $class" }
}

$clauses = @($classes | ForEach-Object { "FullyQualifiedName~$_." })
$arguments = $TestArguments + @('--filter', "(Category!=Quarantined)&($($clauses -join '|'))",
    '--logger', "trx;LogFilePrefix=$LogFilePrefix")

if ($ProcessorCount) {
    $arguments += @('--', "RunConfiguration.EnvironmentVariables.DOTNET_PROCESSOR_COUNT=$ProcessorCount")
}

Write-Host "`nShared-policy set: $($classes -join ', ')"
$clock = [Diagnostics.Stopwatch]::StartNew()
$log = Join-Path $RunDirectory 'shared-policy.log'
& dotnet test (Join-Path $repoRoot 'HappyPhoton.sln') @arguments 2>&1 | Tee-Object -FilePath $log
$exitCode = $LASTEXITCODE
& (Join-Path $PSScriptRoot 'report-test-abort.ps1') -OutputPath $log -ResultsDirectory $RunDirectory
$observed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$failed = [Collections.Generic.List[string]]::new()

foreach ($file in Get-ChildItem -LiteralPath $RunDirectory -Filter "$LogFilePrefix*.trx") {
    [xml]$trx = Get-Content -Raw -LiteralPath $file.FullName
    $methods = @{}

    foreach ($definition in $trx.SelectNodes("//*[local-name()='UnitTest']")) {
        $methods[[string]$definition.id] = [string]$definition.TestMethod.className
    }

    foreach ($result in $trx.SelectNodes("//*[local-name()='UnitTestResult']")) {
        $class = $methods[[string]$result.testId]

        if ($class -in $classes) {
            if ($result.outcome -in @('Passed', 'Failed')) { $null = $observed.Add($class) }
            if ($result.outcome -notin @('Passed', 'NotExecuted')) { $failed.Add("$($result.testName): $($result.outcome)") }
        }
    }
}

$missing = @($classes | Where-Object { -not $observed.Contains($_) })
Write-Host ("Shared-policy set: {0}/{1} classes executed; {2:F2}s." -f $observed.Count, $classes.Count, $clock.Elapsed.TotalSeconds)

if ($missing.Count -gt 0) { throw "Shared-policy classes missing executed TRX results: $($missing -join ', ')." }
if ($failed.Count -gt 0) { throw "Shared-policy tests did not pass: $($failed -join '; ')." }
if ($exitCode -ne 0) { throw "Shared-policy tests failed with exit code $exitCode." }
