[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Audits exactly the package graphs the committed lock files hold, restored in
# locked mode, and fails when NuGet knows an advisory for any package in them.
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$smokeProject = 'scripts/libraw-single-file-smoke/LibRawSingleFileSmoke.csproj'
$graphs = @(
    @{ Target = 'HappyPhoton.sln'; Properties = @('-p:MagickFlavor=OpenMP-x64') }
    @{ Target = 'HappyPhoton.sln'; Properties = @('-p:MagickFlavor=AnyCPU') }
    foreach ($rid in @('win-x64', 'linux-x64', 'osx-arm64')) {
        @{ Target = 'HappyPhoton.csproj'; Properties = @("-p:RuntimeIdentifier=$rid", '-p:PublishReadyToRun=true') }
    }
    @{ Target = $smokeProject; Properties = @() }
    foreach ($rid in @('win-x64', 'linux-x64')) {
        @{ Target = $smokeProject; Properties = @("-p:RuntimeIdentifier=$rid") }
    }
)

$findings = [System.Collections.Generic.List[object]]::new()
$failures = [System.Collections.Generic.List[string]]::new()

function Add-Finding([string] $graph, [string] $project, [string] $package, [string] $version, [string] $severity, [string] $advisory) {
    $findings.Add([pscustomobject]@{
            Graph = $graph
            Project = $project
            Package = $package
            Version = $version
            Severity = (Get-Culture).TextInfo.ToTitleCase($severity.ToLowerInvariant())
            Advisory = $advisory
        })
}

Push-Location $repositoryRoot
try {
    foreach ($graph in $graphs) {
        $label = (@($graph.Target) + $graph.Properties) -join ' '
        Write-Host "::group::$label"
        $restoreOutput = @(dotnet restore $graph.Target --locked-mode -p:ContinuousIntegrationBuild=true @($graph.Properties) 2>&1 |
                ForEach-Object { "$_" })
        $restoreExit = $LASTEXITCODE
        $restoreOutput | Write-Host
        Write-Host '::endgroup::'

        if ($restoreExit -ne 0) {
            $failures.Add("Restore failed ($restoreExit): $label")
            continue
        }

        # Restore's own audit also covers RID-specific packages.
        foreach ($line in $restoreOutput) {
            if ($line -match '(?<project>[^\\/\s]+\.csproj) : warning NU190[1-4]: Package ''(?<package>[^'']+)'' (?<version>\S+) has a known (?<severity>\w+) severity vulnerability, (?<advisory>\S+)') {
                Add-Finding $label $Matches.project $Matches.package $Matches.version $Matches.severity $Matches.advisory
            }
        }

        # `dotnet list package` takes no -p: options; MSBuild reads the same
        # properties from the environment, so it evaluates the restored graph.
        $propertyNames = @($graph.Properties | ForEach-Object {
                $name, $value = $_.Substring(3) -split '=', 2
                Set-Item "env:$name" $value
                $name
            })
        try {
            $listOutput = @(dotnet list $graph.Target package --vulnerable --include-transitive --no-restore --format json 2>&1 |
                    ForEach-Object { "$_" })
            $listExit = $LASTEXITCODE
        }
        finally {
            $propertyNames | ForEach-Object { Remove-Item "env:$_" }
        }

        if ($listExit -ne 0) {
            $listOutput | Write-Host
            $failures.Add("Vulnerable-package listing failed ($listExit): $label")
            continue
        }

        $report = ($listOutput -join "`n") | ConvertFrom-Json
        foreach ($project in @($report.projects)) {
            foreach ($framework in @($project.PSObject.Properties['frameworks']?.Value)) {
                if ($null -eq $framework) { continue }

                $packages = @($framework.PSObject.Properties['topLevelPackages']?.Value) +
                    @($framework.PSObject.Properties['transitivePackages']?.Value)
                foreach ($package in $packages) {
                    if ($null -eq $package) { continue }

                    foreach ($vulnerability in @($package.vulnerabilities)) {
                        Add-Finding $label (Split-Path -Leaf $project.path) $package.id $package.resolvedVersion `
                            $vulnerability.severity $vulnerability.advisoryurl
                    }
                }
            }
        }
    }
}
finally {
    Pop-Location
}

$unique = @($findings | Group-Object Package, Version, Advisory | Sort-Object Name | ForEach-Object {
        $first = $_.Group[0]
        [pscustomobject]@{
            Package = $first.Package
            Version = $first.Version
            Severity = $first.Severity
            Advisory = $first.Advisory
            Projects = (@($_.Group.Project | Sort-Object -Unique) -join ', ')
        }
    })

$summary = [System.Collections.Generic.List[string]]::new()
if ($unique.Count -gt 0) {
    $summary.Add('## Vulnerable packages')
    $summary.Add('')
    $summary.Add('| Package | Version | Severity | Advisory | Projects |')
    $summary.Add('|---|---|---|---|---|')
    foreach ($item in $unique) {
        $summary.Add("| $($item.Package) | $($item.Version) | $($item.Severity) | $($item.Advisory) | $($item.Projects) |")
        Write-Host "::error::$($item.Package) $($item.Version) ($($item.Severity)): $($item.Advisory)"
    }
}

foreach ($failure in $failures) {
    $summary.Add('')
    $summary.Add("Audit incomplete: $failure")
    Write-Host "::error::$failure"
}

if ($unique.Count -eq 0 -and $failures.Count -eq 0) {
    $summary.Add("No vulnerable packages in $($graphs.Count) locked package graphs.")
}

$summary | Write-Host
if ($env:GITHUB_STEP_SUMMARY) {
    $summary | Out-File -LiteralPath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

if ($unique.Count -gt 0 -or $failures.Count -gt 0) { exit 1 }
