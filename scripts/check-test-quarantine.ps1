[CmdletBinding()]
param(
    [ValidateSet("Discovery", "Results")]
    [string] $Mode = "Discovery",
    [string] $RegistryPath = "Tests/quarantined-tests.json",
    [string] $Configuration = "Release",
    [switch] $NoBuild,
    [switch] $NoRestore,
    [string] $ResultsDirectory,
    [string] $TestStepOutcome = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Resolve-RepoPath([string] $Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return (Resolve-Path -LiteralPath $Path).Path
    }

    return (Resolve-Path -LiteralPath (Join-Path $repoRoot $Path)).Path
}

function Read-Registry {
    $path = Resolve-RepoPath $RegistryPath
    $registry = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    if ($registry.schemaVersion -ne 1) {
        throw "Unsupported quarantine registry schema: $($registry.schemaVersion)."
    }

    $projects = @($registry.projects)
    if ($projects.Count -eq 0) {
        throw "The quarantine registry must list the test projects it governs."
    }
    $projectDuplicates = @($projects | Group-Object | Where-Object Count -gt 1)
    if ($projectDuplicates.Count -gt 0) {
        throw "Duplicate quarantine project: $($projectDuplicates[0].Name)."
    }
    $projects | ForEach-Object { $null = Resolve-RepoPath $_ }

    $entries = @($registry.tests)
    $duplicates = @($entries | Group-Object fullyQualifiedName | Where-Object Count -gt 1)
    if ($duplicates.Count -gt 0) {
        throw "Duplicate quarantined test: $($duplicates[0].Name)."
    }

    $today = [DateOnly]::FromDateTime([DateTime]::UtcNow)
    foreach ($entry in $entries) {
        foreach ($property in @(
                "fullyQualifiedName", "project", "issue", "owner", "reason",
                "introducedOn", "expiresOn")) {
            if ([string]::IsNullOrWhiteSpace([string]$entry.$property)) {
                throw "Quarantine entry is missing '$property'."
            }
        }

        if ($entry.issue -notmatch '^https://github\.com/seasalim/happy-photon/issues/\d+$') {
            throw "Quarantine issue must be a Happy Photon GitHub issue: $($entry.issue)."
        }
        if ($entry.owner -notmatch '^@\S+$') {
            throw "Quarantine owner must be a GitHub handle: $($entry.owner)."
        }

        $introduced = [DateOnly]::ParseExact($entry.introducedOn, "yyyy-MM-dd")
        $expires = [DateOnly]::ParseExact($entry.expiresOn, "yyyy-MM-dd")
        $lifetimeDays = $expires.DayNumber - $introduced.DayNumber
        if ($lifetimeDays -lt 1 -or $lifetimeDays -gt 90) {
            throw "Quarantine for $($entry.fullyQualifiedName) must expire within 90 days."
        }
        if ($today -gt $expires) {
            throw "Quarantine expired on $expires for $($entry.fullyQualifiedName)."
        }

        if ($entry.project -notin $projects) {
            throw "Quarantine entry uses an unregistered project: $($entry.project)."
        }
    }

    return [pscustomobject]@{
        Projects = $projects
        Entries = $entries
    }
}

function Get-DiscoveredTests(
    [string] $Project,
    [string] $Settings,
    [string] $Filter = "") {
    $arguments = @(
        "test", (Resolve-RepoPath $Project),
        "--configuration", $Configuration,
        "--settings", (Resolve-RepoPath $Settings),
        "--list-tests"
    )
    if ($NoBuild) { $arguments += "--no-build" }
    if ($NoRestore) { $arguments += "--no-restore" }
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $arguments += @("--filter", $Filter)
    }

    $output = & dotnet @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | ForEach-Object { Write-Host $_ }
        throw "Test discovery failed for $Project."
    }

    return @($output |
        ForEach-Object { [string]$_ } |
        Where-Object { $_ -match '^\s+HappyPhoton\.Tests\.' } |
        ForEach-Object { $_.Trim() } |
        Sort-Object -Unique)
}

function Assert-SameTests(
    [string[]] $Expected,
    [string[]] $Actual,
    [string] $Description) {
    $difference = @(Compare-Object @($Expected) @($Actual))
    if ($difference.Count -gt 0) {
        $detail = $difference | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }
        throw "$Description mismatch:`n$($detail -join "`n")"
    }
}

function Test-ResultMatchesEntry($Result, $Entry) {
    return $Result.TestName -eq $Entry.fullyQualifiedName -or
        $Result.TestName.StartsWith("$($Entry.fullyQualifiedName)(", [StringComparison]::Ordinal)
}

function Test-Discovery([string[]] $projects, $entries) {
    $allSettings = "HappyPhoton.AllTests.runsettings"
    $stableSettings = "HappyPhoton.runsettings"
    foreach ($project in $projects) {
        $registered = @($entries |
            Where-Object project -eq $project |
            ForEach-Object fullyQualifiedName |
            Sort-Object -Unique)
        $all = @(Get-DiscoveredTests $project $allSettings)
        $stable = @(Get-DiscoveredTests $project $stableSettings)
        $quarantined = @(
            Get-DiscoveredTests $project $allSettings "Category=Quarantined")

        Assert-SameTests $registered $quarantined "Registered quarantine discovery"
        Assert-SameTests $all @($stable + $quarantined | Sort-Object -Unique) `
            "Stable plus quarantined discovery"
        Write-Host "Validated $($stable.Count) stable and $($quarantined.Count) quarantined tests in $project."
    }
}

function Read-TrxResults([string] $Directory) {
    $path = Resolve-RepoPath $Directory
    $files = @(Get-ChildItem -LiteralPath $path -Filter "*.trx" -File -Recurse)
    if ($files.Count -eq 0) {
        throw "No TRX files were found under $path."
    }

    $results = foreach ($file in $files) {
        [xml]$trx = Get-Content -Raw -LiteralPath $file.FullName
        $namespace = New-Object System.Xml.XmlNamespaceManager($trx.NameTable)
        $namespace.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
        $summary = $trx.SelectSingleNode("//t:ResultSummary", $namespace)
        $counters = if ($null -ne $summary) {
            $summary.SelectSingleNode("t:Counters", $namespace)
        } else {
            $null
        }
        if ($null -eq $counters) {
            throw "TRX summary counters are missing from $($file.FullName)."
        }
        foreach ($counter in @("error", "timeout", "aborted", "disconnected", "notRunnable")) {
            if ([int]$counters.GetAttribute($counter) -ne 0) {
                throw "TRX $counter counter is nonzero in $($file.FullName)."
            }
        }
        foreach ($node in $trx.SelectNodes("//t:UnitTestResult", $namespace)) {
            [pscustomobject]@{
                TestName = $node.GetAttribute("testName")
                Outcome = $node.GetAttribute("outcome")
                Duration = $node.GetAttribute("duration")
            }
        }
    }

    return @($results)
}

function Test-Results($entries) {
    if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
        throw "ResultsDirectory is required in Results mode."
    }

    $results = Read-TrxResults $ResultsDirectory
    $failed = @($results | Where-Object Outcome -In @("Failed", "Error", "Timeout", "Aborted"))
    $unregistered = @($failed | Where-Object {
            $result = $_
            -not ($entries | Where-Object { Test-ResultMatchesEntry $result $_ })
        })
    if ($unregistered.Count -gt 0) {
        throw "Observation suite has unregistered failures:`n$($unregistered.TestName -join "`n")"
    }

    $summary = [System.Collections.Generic.List[string]]::new()
    $summary.Add("# Quarantined test observation")
    $summary.Add("")
    $summary.Add("| Test | Outcome | Duration | Issue | Expires |")
    $summary.Add("| --- | --- | --- | --- | --- |")
    foreach ($entry in $entries) {
        $matches = @($results | Where-Object { Test-ResultMatchesEntry $_ $entry })
        if ($matches.Count -eq 0) {
            throw "Registered quarantined test did not execute: $($entry.fullyQualifiedName)."
        }
        if (@($matches | Where-Object Outcome -eq "NotExecuted").Count -gt 0) {
            throw "Registered quarantined test was not executed: $($entry.fullyQualifiedName)."
        }

        $outcomes = @($matches.Outcome | Sort-Object -Unique)
        $durations = @($matches.Duration) -join ", "
        $summary.Add("| ``$($entry.fullyQualifiedName)`` | $($outcomes -join ', ') | $durations | $($entry.issue) | $($entry.expiresOn) |")
        if (@($matches | Where-Object Outcome -In @("Failed", "Error", "Timeout", "Aborted")).Count -gt 0) {
            Write-Warning "Quarantined failure observed: $($entry.fullyQualifiedName)."
        }
    }

    if ($TestStepOutcome -in @("cancelled", "skipped")) {
        throw "Observation test step ended with outcome '$TestStepOutcome'."
    }
    if ($TestStepOutcome -eq "failure" -and $failed.Count -eq 0) {
        throw "Observation test step failed without a recorded test failure."
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
        $summary | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
    } else {
        $summary | ForEach-Object { Write-Host $_ }
    }
}

$registry = Read-Registry
if ($Mode -eq "Discovery") {
    Test-Discovery $registry.Projects $registry.Entries
} else {
    Test-Results $registry.Entries
}
