<#
Wrap an executable and its argument array; no shell parsing, process termination or lock acquisition.
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
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot)).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$directory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Force -Path $directory
if (Test-Path "$directory/summary.json") { throw "Output already exists: $directory" }

$start = [Diagnostics.ProcessStartInfo]::new($Command)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.WorkingDirectory = $root

foreach ($argument in $CommandArguments) {
    $start.ArgumentList.Add($argument)
}

$writer = [IO.StreamWriter]::new("$directory/samples.txt", $false)
$instances = @{}
$clock = [Diagnostics.Stopwatch]::StartNew()
$started = [DateTimeOffset]::UtcNow
$child = $null
$exitCode = -1
$output = [IO.FileStream]::new("$directory/command.stdout.txt", [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
$errorOutput = [IO.FileStream]::new("$directory/command.stderr.txt", [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
$copyCancellation = [Threading.CancellationTokenSource]::new()
$copies = @()

try {
    $child = [Diagnostics.Process]::Start($start)
    $copies = @(
        $child.StandardOutput.BaseStream.CopyToAsync($output, $copyCancellation.Token)
        $child.StandardError.BaseStream.CopyToAsync($errorOutput, $copyCancellation.Token)
    )
    $nextSample = 0.0

    do {
        foreach ($process in Get-Process -Name HappyPhoton.Tests, HappyPhoton.Headless.Tests -ErrorAction SilentlyContinue) {
            try {
                $path = $process.Path
                if (-not $path -or -not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { continue }

                $process.Refresh()
                $processStart = $process.StartTime.ToUniversalTime()
                $instance = "$($process.Id)-$($processStart.Ticks)"
                $row = [ordered]@{
                    utc = [DateTimeOffset]::UtcNow.ToString('o'); instance = $instance
                    host = $process.ProcessName; pid = $process.Id; processStartUtc = $processStart.ToString('o')
                    path = $path; pagedBytes = $process.PagedMemorySize64
                    cpuSeconds = $process.TotalProcessorTime.TotalSeconds
                }
                if (-not $instances.ContainsKey($instance)) {
                    $tracked = [Diagnostics.Process]::GetProcessById($process.Id)
                    $null = $tracked.Handle
                    $instances[$instance] = $tracked
                }

                $writer.WriteLine(($row | ConvertTo-Json -Compress))
            } catch [InvalidOperationException], [ArgumentException] {
                # A host may exit between enumeration and its counters being read.
            } catch [ComponentModel.Win32Exception] {
                Write-Warning "Cannot sample process $($process.Id): $($_.Exception.Message)"
            } finally {
                $process.Dispose()
            }
        }

        $writer.Flush()
        if ($child.HasExited) { break }

        $nextSample += 500
        $delay = [Math]::Max(0, $nextSample - $clock.Elapsed.TotalMilliseconds)
        Start-Sleep -Milliseconds ([int]$delay)
    } while ($true)
} finally {
    # Keep the caller's lease until the wrapped command exits, even if sampling fails.
    if ($child) {
        $child.WaitForExit()
        $exitCode = $child.ExitCode
    }

    $writer.Dispose()
    $elapsed = $clock.Elapsed.TotalSeconds
    $outputComplete = [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]$copies, 2000)
    $copyCancellation.Cancel()
    $output.Dispose()
    $errorOutput.Dispose()
    $copyCancellation.Dispose()
    $commandText = [IO.File]::ReadAllText("$directory/command.stdout.txt")
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
            cpuSeconds = $last.cpuSeconds; samples = $rows.Count; aliveAfterCommand = [bool]$stillAlive
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

    foreach ($tracked in $instances.Values) { $tracked.Dispose() }

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
        schemaVersion = 1; workload = $Workload; startedUtc = $started.ToString('o')
        command = $Command; arguments = $CommandArguments; exitCode = $exitCode
        wallSeconds = $elapsed; testPhaseSeconds = $phaseSeconds; commandOutputComplete = $outputComplete; sampleIntervalSeconds = 0.5; metric = 'PagedMemorySize64'; gbDivisor = 1e9
        hosts = @($hosts); hostTotals = $hostTotals; unmatchedTests = @($unmatchedTests.ToArray()); trxDirectory = $TrxDirectory
    }

    $summary | ConvertTo-Json -Depth 12 | Set-Content "$directory/summary.json"

    if ($TrxDirectory) {
        $attribution = Get-ProfileAttribution $samples $tests
        $attribution.ambiguousHosts = @($hosts | Where-Object ambiguousTrxHost | Select-Object -ExpandProperty host -Unique)
        $attribution.g4Passed = $attribution.hosts.Count -gt 0 -and $attribution.ambiguousHosts.Count -eq 0 -and $unmatchedTests.Count -eq 0 -and
            @($attribution.hosts | Where-Object { -not $_.g4Passed }).Count -eq 0
        $attribution | ConvertTo-Json -Depth 12 | Set-Content "$directory/attribution.json"
    }

    if ($child) { $child.Dispose() }
}

Write-Host "Profile: $directory ($($samples.Count) samples; command exit $exitCode)"
exit $exitCode
