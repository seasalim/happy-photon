[CmdletBinding()]
param(
    [ValidateSet("Discovery", "StableResults", "Results")]
    [string] $Mode = "Discovery",
    [string] $RegistryPath = "Tests/quarantined-tests.json",
    [string] $Configuration = "Release",
    [string] $ManifestDirectory,
    [hashtable] $MinimumTestCounts = @{
        "Tests/HappyPhoton.Tests.csproj" = 1300
        "HeadlessTests/HappyPhoton.Headless.Tests.csproj" = 340
    },
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

function Get-ProjectAssembly([string] $Project) {
    [xml]$definition = Get-Content -Raw -LiteralPath (Resolve-RepoPath $Project)
    return [string]$definition.Project.PropertyGroup.AssemblyName
}

function Get-ManifestPath([string] $Directory, [string] $Project) {
    return Join-Path $Directory "$(Get-ProjectAssembly $Project).manifest.json"
}

function Test-NameMatches([string] $Name, [string] $Prefix) {
    return $Name -eq $Prefix -or $Name.StartsWith("$Prefix(", [StringComparison]::Ordinal)
}

function Test-ResultMatchesEntry($Result, $Entry) {
    return $Result.Project -eq $Entry.project -and
        (Test-NameMatches $Result.TestName $Entry.fullyQualifiedName)
}

function Test-Quarantined($Case) {
    return $null -ne $Case.PSObject.Properties["Traits"] -and
        $null -ne $Case.Traits -and
        $null -ne $Case.Traits.PSObject.Properties["Category"] -and
        "Quarantined" -in @($Case.Traits.Category)
}

function Read-Manifest([string] $Directory, [string] $Project) {
    $path = Get-ManifestPath $Directory $Project
    $manifest = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.project -ne $Project -or
        @($manifest.tests).Count -eq 0) {
        throw "Invalid or empty test manifest for $Project at $path."
    }
    foreach ($case in $manifest.tests) {
        foreach ($field in @("Class", "Method", "DisplayName")) {
            if ([string]::IsNullOrWhiteSpace($case.$field)) {
                throw "Manifest test is missing $field in $path."
            }
        }
    }
    return $manifest
}

function Test-Discovery([string[]] $projects, $entries) {
    $directory = if ($ManifestDirectory) { Resolve-RepoPath $ManifestDirectory } else {
        $root = if ($ResultsDirectory) { $ResultsDirectory } else { "artifacts/test-results/discovery" }
        if (-not [IO.Path]::IsPathRooted($root)) { $root = Join-Path $repoRoot $root }
        $path = Join-Path $root "manifests"
        $null = New-Item -ItemType Directory -Path $path -Force
        $path
    }
    foreach ($project in $projects) {
        if (-not $ManifestDirectory) {
            [xml]$definition = Get-Content -Raw -LiteralPath (Resolve-RepoPath $project)
            $assembly = Get-ProjectAssembly $project
            $framework = [string]$definition.Project.PropertyGroup.TargetFramework
            $binary = Join-Path (Split-Path (Resolve-RepoPath $project)) `
                "bin/$Configuration/$framework/$assembly.dll"
            if (-not (Test-Path -LiteralPath $binary)) {
                throw "Prebuilt test executable is missing: $binary. Run dotnet build HappyPhoton.sln --configuration $Configuration first."
            }
            $output = & dotnet $binary -list full/json -noColor 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "In-process test listing failed for ${project}: $($output -join "`n")"
            }
            $cases = @($output -join "`n" | ConvertFrom-Json)
            [pscustomobject]@{ schemaVersion = 1; project = $project; tests = $cases } |
                ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Get-ManifestPath $directory $project)
        }
        $manifest = Read-Manifest $directory $project
        $registered = @($entries | Where-Object project -eq $project)
        $quarantined = @($manifest.tests | Where-Object { Test-Quarantined $_ })
        foreach ($case in $manifest.tests) {
            $matches = @($registered | Where-Object {
                Test-NameMatches $case.DisplayName $_.fullyQualifiedName
            })
            if ((Test-Quarantined $case) -ne ($matches.Count -gt 0)) {
                throw "Registered quarantine manifest mismatch: $($case.DisplayName) in $project."
            }
        }
        foreach ($entry in $registered) {
            if (-not ($quarantined | Where-Object {
                Test-NameMatches $_.DisplayName $entry.fullyQualifiedName
            })) {
                throw "Registered quarantine manifest mismatch: $($entry.fullyQualifiedName) is not traited in $project."
            }
        }
        $count = @($manifest.tests).Count
        if ($MinimumTestCounts.ContainsKey($project) -and $count -lt $MinimumTestCounts[$project]) {
            throw "Manifest count for $project is $count; minimum is $($MinimumTestCounts[$project])."
        }
        Write-Host "Validated $count test cases ($($quarantined.Count) quarantined) in $project. Manifest: $(Get-ManifestPath $directory $project)"
    }
}

function Read-TrxResults([string] $Directory, [string[]] $Projects) {
    $path = Resolve-RepoPath $Directory
    $files = @(Get-ChildItem -LiteralPath $path -Filter "*.trx" -File -Recurse)
    if ($files.Count -eq 0) {
        throw "No TRX files were found under $path."
    }

    $assemblyProjects = @{}
    foreach ($project in $Projects) {
        $assembly = "$(Get-ProjectAssembly $project).dll"
        if ($assemblyProjects.ContainsKey($assembly)) { throw "Ambiguous governed assembly: $assembly." }
        $assemblyProjects[$assembly] = $project
    }
    $projectFiles = @{}
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
        $definitions = @{}
        $fileProjects = [Collections.Generic.HashSet[string]]::new()
        foreach ($test in $trx.SelectNodes("//t:TestDefinitions/t:UnitTest", $namespace)) {
            $method = $test.SelectSingleNode("t:TestMethod", $namespace)
            if ($null -eq $method) { throw "TRX test definition is missing TestMethod in $file." }
            $assembly = ($method.GetAttribute("codeBase") -replace '\\', '/').Split('/')[-1]
            $project = if ($assemblyProjects.ContainsKey($assembly)) { $assemblyProjects[$assembly] } else { "" }
            $definitions[$test.GetAttribute("id")] = $project
            if ($project) { $null = $fileProjects.Add($project) }
        }
        foreach ($project in $fileProjects) {
            if ($projectFiles.ContainsKey($project)) { throw "Multiple TRX files for governed project $project." }
            $projectFiles[$project] = $file.FullName
        }
        foreach ($node in $trx.SelectNodes("//t:UnitTestResult", $namespace)) {
            $testId = $node.GetAttribute("testId")
            if (-not $definitions.ContainsKey($testId)) { throw "TRX result has no test definition: $testId." }
            [pscustomobject]@{
                Project = $definitions[$testId]
                TestName = $node.GetAttribute("testName")
                Outcome = $node.GetAttribute("outcome")
                Duration = $node.GetAttribute("duration")
            }
        }
    }

    foreach ($project in $Projects) {
        if (-not $projectFiles.ContainsKey($project)) { throw "Missing TRX for governed project $project." }
        if (-not ($results | Where-Object Project -eq $project)) { throw "Missing TRX results for governed project $project." }
    }
    return @($results)
}

function Test-StableResults($projects, $entries) {
    $results = Read-TrxResults $ResultsDirectory $projects
    foreach ($entry in $entries) {
        if ($results | Where-Object { (Test-ResultMatchesEntry $_ $entry) -and $_.Outcome -ne "NotExecuted" }) {
            throw "Default lane executed registered quarantined test: $($entry.fullyQualifiedName)."
        }
    }
    $directory = if ($ManifestDirectory) { Resolve-RepoPath $ManifestDirectory } else {
        Join-Path (Resolve-RepoPath $ResultsDirectory) "manifests"
    }
    foreach ($project in $projects) {
        $manifest = Read-Manifest $directory $project
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($result in @($results | Where-Object Project -eq $project)) {
            if ($result.Outcome -notin @("Passed", "Failed", "NotExecuted")) {
                throw "Unexpected stable result outcome: $($result.Outcome) for $($result.TestName)."
            }
            $null = $names.Add($result.TestName)
            $parenthesis = $result.TestName.IndexOf('(')
            if ($parenthesis -ge 0) { $null = $names.Add($result.TestName.Substring(0, $parenthesis)) }
        }
        foreach ($case in $manifest.tests) {
            if (-not (Test-Quarantined $case) -and -not $names.Contains($case.DisplayName)) {
                throw "Stable manifest test is missing from TRX: $($case.DisplayName) in $project."
            }
        }
    }
    Write-Host "Default TRX reconciled against all governed manifests; no quarantined test executed."
}

function Test-Results($projects, $entries) {
    $results = Read-TrxResults $ResultsDirectory $projects
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

if ($Mode -ne "Discovery" -and [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    throw "ResultsDirectory is required in $Mode mode."
}
$registry = Read-Registry
if ($Mode -eq "Discovery") {
    Test-Discovery $registry.Projects $registry.Entries
} elseif ($Mode -eq "StableResults") {
    Test-StableResults $registry.Projects $registry.Entries
} else {
    Test-Results $registry.Projects $registry.Entries
}
