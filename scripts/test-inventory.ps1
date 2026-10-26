<#
Consumes existing runs; never launches tests or takes a lease. RunsDirectory contains sampler
outputs labelled solo/nightly. ManifestDirectory
contains the ordinary quarantine check's *.manifest.json discovery files.
ResolutionsPath defaults to the committed source reviews, shaped as JSON: [{project,class,method,category,variables,predicted,reason}].
Environment permits Passed/NotExecuted only for reviewed platform/build-conditioned methods.
Optional casePredictions [{name,predicted}] exhaustively pin runtime cases with explicit opt-in gates.
Resolutions document reviewed source conditions; they cannot waive a TRX mismatch.
Incomplete evidence writes inventory/report but exits nonzero, and suppresses the retire table.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $RunsDirectory,
    [Parameter(Mandatory)][string] $ManifestDirectory,
    [Parameter(Mandatory)][string] $SpecsDirectory,
    [string] $ResolutionsPath = "$PSScriptRoot/test-profile-resolutions.json",
    [string] $OutputDirectory = "artifacts/test-profile/$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))-inventory"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/test-profile-common.ps1"
. "$PSScriptRoot/test-profile-conditions.ps1"
. "$PSScriptRoot/test-profile-reconciliation.ps1"
. "$PSScriptRoot/test-profile-report.ps1"
$root = Split-Path $PSScriptRoot
$issues = [Collections.Generic.List[string]]::new()
$inventory = [Collections.Generic.List[object]]::new()
$resolutions = if ($ResolutionsPath) { @(Get-Content -Raw $ResolutionsPath | ConvertFrom-Json | ForEach-Object { $_ }) } else { @() }
$registry = Get-Content -Raw "$root/Tests/quarantined-tests.json" | ConvertFrom-Json
$runs = @(foreach ($file in Get-ChildItem $RunsDirectory -Filter summary.json -Recurse -File) {
    $summary = Get-Content -Raw $file | ConvertFrom-Json
    $attributionPath = Join-Path $file.DirectoryName 'attribution.json'
    $attribution = if (Test-Path $attributionPath) { Get-Content -Raw $attributionPath | ConvertFrom-Json } else { $null }
    $tests = if ($summary.trxDirectory -and (Test-Path $summary.trxDirectory)) { @(Read-ProfileTrx $summary.trxDirectory) } else { @() }
    [pscustomobject]@{ path = $file.DirectoryName; summary = $summary; attribution = $attribution; tests = $tests }
})
$citationFiles = @(
    Get-ChildItem "$root/docs" -Filter *.md -Recurse -File
    Get-ChildItem "$root/scripts" -Filter *.ps1 -File
    Get-ChildItem "$root/Tests" -Filter *.ps1 -File
    Get-ChildItem $SpecsDirectory -Filter *.md -Recurse -File
)
$citationSources = @(foreach ($file in $citationFiles) {
    $content = Get-Content -Raw $file
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
    $topSpec = $file.DirectoryName -eq (Resolve-Path $SpecsDirectory).Path
    $qualifies = Test-ProfileKeepCitation $relative $topSpec $file.Name $content
    [pscustomobject]@{ path = $file.FullName; text = $content; qualifies = $qualifies }
})

# Scan citation text once, keyed by the discovered short class name.
$citationIndex = @{}

foreach ($project in $registry.projects) {
    $hostName = [IO.Path]::GetFileNameWithoutExtension($project)
    $discovery = Get-Content -Raw (Join-Path $ManifestDirectory "$hostName.manifest.json") | ConvertFrom-Json

    foreach ($name in $discovery.tests.Class | Sort-Object -Unique) {
        $citationIndex[($name -split '\.')[-1]] = [Collections.Generic.List[object]]::new()
    }
}

foreach ($source in $citationSources) {
    $lineNumber = 0

    foreach ($line in $source.text -split '\r?\n') {
        $lineNumber++
        $words = [Collections.Generic.HashSet[string]]::new()

        foreach ($word in [regex]::Matches($line, '[A-Za-z_]\w*')) {
            if ($citationIndex.ContainsKey($word.Value) -and $words.Add($word.Value)) {
                $citationIndex[$word.Value].Add([pscustomobject]@{
                    path = $source.path; line = $lineNumber; keepQualifying = $source.qualifies; text = $line.Trim()
                })
            }
        }
    }
}
foreach ($project in $registry.projects) {
    $hostName = [IO.Path]::GetFileNameWithoutExtension($project)
    $manifestPath = Join-Path $ManifestDirectory "$hostName.manifest.json"
    $manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
    if ($manifest.project -ne $project -or $manifest.schemaVersion -ne 1) { throw "Invalid discovery manifest: $manifestPath" }

    $index = Read-ProfileProject (Join-Path $root $project)
    $solo = @($runs | Where-Object { $_.summary.workload -eq 'solo' -and $hostName -in $_.summary.hosts.host })
    $nightly = @($runs | Where-Object { $_.summary.workload -eq 'nightly' -and $hostName -in $_.summary.hosts.host })

    foreach ($workload in @('solo', 'nightly')) {
        $selected = @(if ($workload -eq 'solo') { $solo } else { $nightly })
        if ($selected.Count -ne 1) { $issues.Add("${hostName}: expected one $workload run; found $($selected.Count).") }
    }

    $registered = @($registry.tests | Where-Object project -eq $project)
    $quarantinedNames = @($registered.fullyQualifiedName)
    $manifestQuarantine = @($manifest.tests | Where-Object {
        $_.PSObject.Properties['Traits'] -and $_.Traits.PSObject.Properties['Category'] -and 'Quarantined' -in $_.Traits.Category
    } | ForEach-Object { "$($_.Class).$($_.Method)" } | Sort-Object -Unique)

    if (@(Compare-Object $quarantinedNames $manifestQuarantine).Count) { $issues.Add("${hostName}: quarantine registry/discovery mismatch.") }

    foreach ($classGroup in $manifest.tests | Group-Object Class) {
        $class = $classGroup.Name
        $methods = @(foreach ($methodGroup in $classGroup.Group | Group-Object Method) {
            $method = $methodGroup.Name
            $condition = Get-MethodCondition $index $class $method
            $condition = Resolve-ProfileCondition $condition $resolutions $project $class $method

            $quarantined = "$class.$method" -in $quarantinedNames
            if ($quarantined) { $condition.predicted = 'Excluded' }

            foreach ($problem in $condition.unresolved) {
                $issues.Add("$project $class.${method}: $problem")
            }

            $observations = @(foreach ($run in @($solo) + @($nightly)) {
                $observed = @($run.tests | Where-Object { $_.host -eq $hostName -and $_.class -eq $class -and $_.method -eq $method })
                $comparison = Compare-ProfileMethod $condition @($methodGroup.Group.DisplayName) $observed $quarantined

                foreach ($problem in $comparison.issues) {
                    $issues.Add("$project $class.${method}: $problem in $($run.path)")
                }

                [pscustomobject]@{ workload = $run.summary.workload; run = $run.path; outcomeCounts = $comparison.outcomeCounts }
            })

            [pscustomobject]@{
                method = $method; discoveredCases = $methodGroup.Count; cases = @($methodGroup.Group.DisplayName)
                quarantined = $quarantined; condition = $condition; observations = $observations
            }
        })
        $categories = @(($methods | ForEach-Object { $_.condition.category }) | Sort-Object -Unique)
        $shortName = ($class -split '\.')[-1]
        $citations = @($citationIndex[$shortName].ToArray())
        $timings = @(foreach ($run in @($solo) + @($nightly)) {
            $rows = @($run.tests | Where-Object { $_.host -eq $hostName -and $_.class -eq $class })
            $growth = @(if ($run.attribution) { $run.attribution.classes | Where-Object { $_.host -eq $hostName -and $_.class -eq $class } })
            [pscustomobject]@{
                workload = $run.summary.workload
                wallSeconds = (Get-ProfileSum $rows seconds)
                attributedGrowthBytes = if ($growth.Count) { Get-ProfileSum $growth growthBytes } else { $null }
            }
        })
        $inventory.Add([pscustomobject]@{
            project = $project; host = $hostName; class = $class
            category = if ($categories.Count -eq 1) { $categories[0] } else { 'mixed' }
            variables = @(($methods | ForEach-Object { $_.condition.variables }) | Sort-Object -Unique)
            methods = $methods; citations = $citations
            runnerWorkloads = @(Get-ProfileRunnerWorkloads $citations)
            measurements = $timings
        })
    }

    Write-Host "Inventoried $project ($($inventory.Count) classes so far)."
    $discoveredMethods = @($manifest.tests | ForEach-Object { "$($_.Class).$($_.Method)" } | Sort-Object -Unique)

    foreach ($run in @($solo) + @($nightly)) {
        foreach ($test in $run.tests | Where-Object host -eq $hostName) {
            if ("$($test.class).$($test.method)" -notin $discoveredMethods) {
                $issues.Add("${project}: TRX method absent from discovery: $($test.class).$($test.method)")
            }
        }
    }
}

$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
$data = @{
    schemaVersion = 1; classes = @($inventory.ToArray()); unresolved = @($issues.ToArray())
    retireTableReady = $issues.Count -eq 0
    counts = @($inventory | Group-Object project | ForEach-Object {
        @{ project = $_.Name; classes = $_.Count
           optInClasses = @($_.Group | Where-Object { Test-ProfileOptInClass $_ }).Count
           quarantinedMethods = @($_.Group.methods | Where-Object quarantined).Count }
    })
}
$data | ConvertTo-Json -Depth 30 | Set-Content "$OutputDirectory/inventory.json"
Write-ProfileReport $data "$OutputDirectory/report.md"
Write-Host "Inventory: $OutputDirectory ($($inventory.Count) classes; $($issues.Count) unresolved)"
if ($issues.Count) { exit 1 }
