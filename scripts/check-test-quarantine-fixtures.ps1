[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$checker = Join-Path $PSScriptRoot "check-test-quarantine.ps1"
$seed = Get-Content -Raw (Join-Path $PSScriptRoot "fixtures/test-quarantine/cases.json")
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "happy-photon-quarantine-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $fixtureRoot
$stepSummary = $env:GITHUB_STEP_SUMMARY
$env:GITHUB_STEP_SUMMARY = Join-Path $fixtureRoot "summary.md"

function Write-Fixture([string] $Scenario) {
    $projects = @($seed | ConvertFrom-Json)
    $directory = Join-Path $fixtureRoot $Scenario
    $null = New-Item -ItemType Directory -Path $directory
    $entry = [pscustomobject]@{
        fullyQualifiedName = "Other.Headless.Tests.Flaky"
        project = $projects[1].project
        issue = "https://github.com/seasalim/happy-photon/issues/6"
        owner = "@seasalim"
        reason = "Fixture quarantine"
        introducedOn = [DateTime]::UtcNow.ToString("yyyy-MM-dd")
        expiresOn = [DateTime]::UtcNow.AddDays(30).ToString("yyyy-MM-dd")
    }
    if ($Scenario -eq "unregistered-trait") { $projects[0].tests[0].Traits = @{ Category = @("Quarantined") } }
    if ($Scenario -eq "untraited-entry") { $entry.fullyQualifiedName = "Other.Headless.Tests.Stable" }
    if ($Scenario -eq "expired") { $entry.introducedOn = "2020-01-01"; $entry.expiresOn = "2020-01-02" }
    if ($Scenario -eq "unregistered-project") { $entry.project = "Unknown.csproj" }
    $entries = @($entry)
    if ($Scenario -eq "duplicate-entry") { $entries += $entry }
    $registry = @{ schemaVersion = 1; projects = @($projects.project); tests = $entries }
    if ($Scenario -eq "duplicate-project") { $registry.projects += $projects[0].project }
    $registry | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $directory "registry.json")
    foreach ($project in $projects) {
        @{ schemaVersion = 1; project = $project.project; tests = $project.tests } |
            ConvertTo-Json -Depth 8 | Set-Content (Join-Path $directory "$($project.assembly).manifest.json")
        if ($Scenario -in @("missing-project", "nightly-missing-project") -and $project -eq $projects[1]) { continue }
        $definitions = @()
        $results = @()
        foreach ($case in $project.tests) {
            if ($case.Method -eq "Flaky" -and $Scenario -notin @("executed-quarantine", "nightly-pass", "nightly-skipped")) { continue }
            if ($Scenario -eq "missing-stable" -and $case.Method -eq "Stable" -and $project -eq $projects[0]) { continue }
            $id = [Guid]::NewGuid().ToString()
            $name = $case.DisplayName
            if ($case.Method -eq "Theory") { $name += "(value: 1)" }
            $outcome = if ($case.Method -eq "Stable" -or ($Scenario -eq "nightly-skipped" -and $case.Method -eq "Flaky")) { "NotExecuted" } else { "Passed" }
            $definitions += "<UnitTest id='$id'><TestMethod codeBase='C:\tests\$($project.assembly).dll' /></UnitTest>"
            $results += "<UnitTestResult testId='$id' testName='$name' outcome='$outcome' duration='00:00:00.001' />"
        }
        $trx = "<TestRun xmlns='http://microsoft.com/schemas/VisualStudio/TeamTest/2010'><TestDefinitions>$($definitions -join '')</TestDefinitions><Results>$($results -join '')</Results><ResultSummary><Counters error='0' timeout='0' aborted='0' disconnected='0' notRunnable='0' /></ResultSummary></TestRun>"
        $trx | Set-Content (Join-Path $directory "$($project.assembly).trx")
    }
    if ($Scenario -eq "duplicate-trx") {
        Copy-Item (Join-Path $directory "HappyPhoton.Tests.trx") (Join-Path $directory "duplicate.trx")
    }
    return $directory
}

function Assert-Fixture([string] $Scenario, [string] $Mode, [string] $ExpectedError = "") {
    $directory = Write-Fixture $Scenario
    $message = ""
    $minimum = if ($Scenario -eq "low-count") { @{ "Tests/HappyPhoton.Tests.csproj" = 3 } } else { @{} }
    try {
        & $checker -Mode $Mode -RegistryPath (Join-Path $directory "registry.json") `
            -ManifestDirectory $directory -ResultsDirectory $directory -MinimumTestCounts $minimum 6>$null
    } catch {
        $message = $_.Exception.Message
    }
    if ($ExpectedError) {
        if ($message -notlike "$ExpectedError*") { throw "Fixture $Scenario expected '$ExpectedError', got '$message'." }
    } elseif ($message) { throw "Fixture $Scenario failed: $message" }
}

try {
    Assert-Fixture "discovery-pass" "Discovery"
    Assert-Fixture "stable-pass" "StableResults"
    Assert-Fixture "nightly-pass" "Results"
    Assert-Fixture "unregistered-trait" "Discovery" "Registered quarantine manifest mismatch"
    Assert-Fixture "untraited-entry" "Discovery" "Registered quarantine manifest mismatch"
    Assert-Fixture "executed-quarantine" "StableResults" "Default lane executed registered"
    Assert-Fixture "missing-stable" "StableResults" "Stable manifest test is missing"
    Assert-Fixture "missing-project" "StableResults" "Missing TRX for governed project"
    Assert-Fixture "nightly-missing-project" "Results" "Missing TRX for governed project"
    Assert-Fixture "low-count" "Discovery" "Manifest count"
    Assert-Fixture "nightly-missing" "Results" "Registered quarantined test did not execute"
    Assert-Fixture "nightly-skipped" "Results" "Registered quarantined test was not executed"
    Assert-Fixture "duplicate-trx" "StableResults" "Multiple TRX files"
    Assert-Fixture "duplicate-entry" "Discovery" "Duplicate quarantined test"
    Assert-Fixture "duplicate-project" "Discovery" "Duplicate quarantine project"
    Assert-Fixture "unregistered-project" "Discovery" "Quarantine entry uses an unregistered project"
    Assert-Fixture "expired" "Discovery" "Quarantine expired"
    Write-Host "Quarantine fixtures passed (three passing sets and fourteen rejected cases)."
} finally {
    $env:GITHUB_STEP_SUMMARY = $stepSummary
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Fixture cleanup path is outside the temporary directory: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
