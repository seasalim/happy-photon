<#
Wrap an executable in a Windows job; no shell parsing or lock acquisition.
Example (from PowerShell):
  ./scripts/measure-suite.ps1 -OutputDirectory artifacts/test-profile/short -TrxDirectory artifacts/test-profile/short/trx `
    -Command pwsh -CommandArguments @('-NoProfile','-File','./scripts/verify.ps1','-NoBuild',
      '-Filter','FullyQualifiedName~ExportProofCaptionTests','-ResultsDirectory','artifacts/test-profile/short/trx')
Full measurements require the caller's measure lease. Filtered validation uses the caller's heavy lease.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Command,
    [string[]] $CommandArguments = @(),
    [string] $OutputDirectory = "artifacts/test-profile/$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))",
    [string] $TrxDirectory,
    [ValidateSet('validation', 'solo', 'nightly')]
    [string] $Workload = 'validation'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/test-profile-common.ps1"
. "$PSScriptRoot/test-process-sampling.ps1"

if (-not $IsWindows) { throw "Job-accounted suite measurement requires Windows." }

Add-Type -Path (Join-Path $PSScriptRoot 'TestProcessJob.cs')
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot)).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$directory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Force -Path $directory
if (Test-Path "$directory/summary.json") { throw "Output already exists: $directory" }

$executable = (Get-Command $Command -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
# Packaged PowerShell brokers children outside its job, even through the real exe.
# Stage the installed runtime before timing; an ordinary installation needs no copy.
if ([IO.Path]::GetFileName($executable) -eq 'pwsh.exe' -and $executable -like '*\WindowsApps\*') {
    $executable = Join-Path $PSHOME 'pwsh.exe'

    if ($PSHOME -like '*\WindowsApps\*') {
        $runtime = Join-Path $directory 'powershell'
        Copy-Item -LiteralPath $PSHOME -Destination $runtime -Recurse
        $executable = Join-Path $runtime 'pwsh.exe'
    }
}

$job = [TestProcessJob]::new()
$writer = [IO.StreamWriter]::new("$directory/samples.txt", $false)
$instances = @{}
$clock = [Diagnostics.Stopwatch]::StartNew()
$started = [DateTimeOffset]::UtcNow
$child = $null
$exitCode = -1
$sampling = $null
$samplingError = $null
$samples = @()

try {
    $nodeReuse = $env:MSBUILDDISABLENODEREUSE
    $buildServer = $env:DOTNET_CLI_USE_MSBUILD_SERVER

    try {
        # Existing build servers are outside this job and cannot launch measured hosts.
        $env:MSBUILDDISABLENODEREUSE = '1'
        $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
        $child = $job.Start($executable, $CommandArguments, $root,
            "$directory/command.stdout.txt", "$directory/command.stderr.txt")
    } finally {
        $env:MSBUILDDISABLENODEREUSE = $nodeReuse
        $env:DOTNET_CLI_USE_MSBUILD_SERVER = $buildServer
    }
    $sampling = New-TestProcessSampling $job $child $root $clock $started
    $sampling.JobSamples.Add([pscustomobject]@{ before = 0.0; after = 0.0; cpu = 0.0 })
    $nextSample = 0.0

    do {
        foreach ($row in @(Update-TestProcessSampling $sampling)) {
            $writer.WriteLine(($row | ConvertTo-Json -Compress))
        }

        $writer.Flush()
        if ($child.HasExited) {
            foreach ($row in @(Update-TestProcessSampling $sampling)) {
                $writer.WriteLine(($row | ConvertTo-Json -Compress))
            }

            break
        }

        $nextSample += 500
        $delay = [Math]::Max(0, $nextSample - $clock.Elapsed.TotalMilliseconds)
        Start-Sleep -Milliseconds ([int]$delay)
    } while ($true)
} catch {
    $samplingError = $_.Exception.Message
    throw
} finally {
    # Keep the caller's lease until the wrapped command exits, even if sampling fails.
    if ($child) {
        $child.WaitForExit()
        $exitCode = $child.ExitCode
    }

    $writer.Dispose()
    $elapsed = $clock.Elapsed.TotalSeconds
    $outputComplete = $true

    $summary = [ordered]@{
        schemaVersion = 2; workload = $Workload; startedUtc = $started.ToString('o')
        command = $Command; arguments = $CommandArguments; exitCode = $exitCode
        wallSeconds = $elapsed; measurementValid = $false
        samplingError = $samplingError; reportingError = $null; cpu = $null
    }
    $attribution = @{
        hosts = @(); processes = @(); classes = @(); ambiguousHosts = @()
        g4Passed = $false; reportingError = $null
    }

    try {
        if ($sampling) {
            foreach ($track in $sampling.Tracks.Values | Where-Object IsHost) {
                $instances[$track.Key] = $track.Process
            }

            $cpu = Get-TestCpuWindowSummary $sampling.JobSamples @($sampling.Tracks.Values) $sampling.PeakChildren $started
            $cpuEvidence = @{
                job = @($sampling.JobSamples.ToArray())
                processes = @(foreach ($track in $sampling.Tracks.Values) {
                    @{ instance = $track.Key; pid = $track.Process.Id; parentPid = $track.ParentId; kind = $track.Kind
                       name = $track.Name; executionEvidence = $track.ExecutionEvidence
                       descendant = $track.Descendant; samples = @($track.Samples.ToArray()) }
                })
            }
            $cpuEvidence | ConvertTo-Json -Depth 8 | Set-Content "$directory/cpu-samples.json"
        } else {
            $cpu = $null
        }

        # A surviving child may still hold the inherited output handle after the command exits.
        $commandStream = [IO.File]::Open("$directory/command.stdout.txt", [IO.FileMode]::Open,
            [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $commandReader = [IO.StreamReader]::new($commandStream)

        try {
            $commandText = $commandReader.ReadToEnd()
        } finally {
            $commandReader.Dispose()
        }

        $phase = [regex]::Match([string]$commandText, 'tests\+reconciliation ([0-9.]+)s')
        $phaseSeconds = if ($phase.Success) { [double]::Parse($phase.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) } else { $null }
        $samples = @(Get-Content "$directory/samples.txt" | ForEach-Object { $_ | ConvertFrom-Json })
        $tests = @(if ($TrxDirectory -and (Test-Path $TrxDirectory)) { Read-ProfileTrx $TrxDirectory })
        $hosts = foreach ($group in @($samples | Group-Object instance)) {
            $rows = @($group.Group)
            $first = $rows[0]
            $last = $rows[-1]
            $peak = $rows | Sort-Object pagedBytes -Descending | Select-Object -First 1
            $sameHost = @($tests | Where-Object host -eq $first.host)
            $tracked = $instances[$group.Name]
            $stillAlive = -not $tracked.HasExited
            $end = if ($stillAlive) { [DateTimeOffset]::UtcNow } else { [DateTimeOffset]$tracked.ExitTime.ToUniversalTime() }

            $sameHost = @($sameHost | Where-Object {
                $_.start -ge [DateTimeOffset]$first.processStartUtc -and $_.end -le $end
            })
            $counts = @{ passed = @($sameHost | Where-Object outcome -eq Passed).Count
                   skipped = @($sameHost | Where-Object outcome -eq NotExecuted).Count
                   failed = @($sameHost | Where-Object outcome -eq Failed).Count }

            [pscustomobject][ordered]@{
                instance = $group.Name; host = $first.host; pid = $first.pid; path = $first.path
                processStartUtc = $first.processStartUtc; firstSampleUtc = $first.utc; lastSampleUtc = $last.utc
                wallSeconds = ($end - [DateTimeOffset]$first.processStartUtc).TotalSeconds
                peakBytes = $peak.pagedBytes; peakGB = $peak.pagedBytes / 1e9
                timeToPeakSeconds = ([DateTimeOffset]$peak.utc - [DateTimeOffset]$first.processStartUtc).TotalSeconds
                cpuSeconds = [TestProcessJob]::CpuSeconds($tracked); samples = $rows.Count; aliveAfterCommand = [bool]$stillAlive
                counts = $counts; endUtc = $end.ToString('o'); ambiguousTrxHost = $false
                oomTests = @($sameHost | Where-Object oom | Select-Object -ExpandProperty name)
            }
        }

        $unmatchedTests = [Collections.Generic.List[string]]::new()
        foreach ($test in $tests) {
            $owners = @($hosts | Where-Object {
                $_.host -eq $test.host -and $test.start -ge [DateTimeOffset]$_.processStartUtc -and $test.end -le [DateTimeOffset]$_.endUtc
            })

            if ($owners.Count -ne 1) {
                $unmatchedTests.Add($test.name)
                foreach ($owner in $owners) { $owner.ambiguousTrxHost = $true }
            }
        }

        $hostTotals = @(foreach ($group in @($hosts | Group-Object host)) {
            $results = @($tests | Where-Object host -eq $group.Name)
            $execution = @($group.Group | Where-Object { $_.counts.passed + $_.counts.skipped + $_.counts.failed -gt 0 })
            $peak = $group.Group | Sort-Object peakBytes -Descending | Select-Object -First 1
            [pscustomobject]@{
                host = $group.Name; peakBytes = $peak.peakBytes; peakGB = $peak.peakGB
                timeToPeakSeconds = $peak.timeToPeakSeconds
                wallSeconds = if ($execution.Count) { Get-ProfileSum $execution wallSeconds } else { Get-ProfileSum $group.Group wallSeconds }
                counts = @{ passed = @($results | Where-Object outcome -eq Passed).Count
                    skipped = @($results | Where-Object outcome -eq NotExecuted).Count
                    failed = @($results | Where-Object outcome -eq Failed).Count }
                oomTests = @($results | Where-Object oom | Select-Object -ExpandProperty name)
            }
        })

        $summary = [ordered]@{
            schemaVersion = 2; workload = $Workload; startedUtc = $started.ToString('o')
            command = $Command; executable = $executable; arguments = $CommandArguments; exitCode = $exitCode
            wallSeconds = $elapsed; testPhaseSeconds = $phaseSeconds; commandOutputComplete = $outputComplete; sampleIntervalSeconds = 0.5; metric = 'PagedMemorySize64'; gbDivisor = 1e9
            measurementValid = $null -eq $samplingError
            samplingError = $samplingError
            reportingError = $null
            cpu = if ($null -eq $samplingError) { $cpu } else { $null }
            processWalls = @(if ($TrxDirectory -and (Test-Path $TrxDirectory)) {
                Get-ChildItem -LiteralPath $TrxDirectory -Filter hosts.json -Recurse -File |
                    ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json }
            })
            hosts = @($hosts); hostTotals = $hostTotals; unmatchedTests = @($unmatchedTests.ToArray()); trxDirectory = $TrxDirectory
        }

        if ($TrxDirectory) {
            $attribution = Get-ProfileAttribution $samples $tests
            $attribution.ambiguousHosts = @($hosts | Where-Object ambiguousTrxHost | Select-Object -ExpandProperty host -Unique)
            $attribution.g4Passed = $null -eq $samplingError -and $attribution.hosts.Count -gt 0 -and $attribution.ambiguousHosts.Count -eq 0 -and $unmatchedTests.Count -eq 0 -and
                @($attribution.hosts | Where-Object { -not $_.g4Passed }).Count -eq 0
        }
    } catch {
        $summary.measurementValid = $false
        $summary.reportingError = $_.Exception.Message
        $summary.cpu = $null
        $attribution.g4Passed = $false
        $attribution.reportingError = $_.Exception.Message
        Write-Warning "Profile summarization failed: $($_.Exception.Message)"
    } finally {
        try {
            # Persist failure evidence too; reporting must not replace the command's exit code.
            if ($TrxDirectory) {
                try {
                    $attribution | ConvertTo-Json -Depth 12 | Set-Content "$directory/attribution.json"
                } catch {
                    $summary.measurementValid = $false
                    $summary.reportingError = $_.Exception.Message
                    $summary.cpu = $null
                    Write-Warning "Cannot write profile attribution: $($_.Exception.Message)"
                }
            }

            try {
                $summary | ConvertTo-Json -Depth 12 | Set-Content "$directory/summary.json"
            } catch {
                Write-Warning "Cannot write profile summary: $($_.Exception.Message)"
            }
        } finally {
            if ($sampling) {
                foreach ($track in $sampling.Tracks.Values) { $track.Process.Dispose() }
            }

            $job.Dispose()
            if ($child) { $child.Dispose() }
        }
    }
}

Write-Host "Profile: $directory ($($samples.Count) samples; command exit $exitCode)"
exit $exitCode
