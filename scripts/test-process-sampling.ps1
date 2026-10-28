. (Join-Path $PSScriptRoot 'test-host-invocation.ps1')

# Held handles keep final counters available after exit, including reused PIDs.
function New-TestProcessSampling($Job, $CommandProcess, [string] $Root, $Clock, [DateTimeOffset] $Started) {
    return @{
        Job = $Job
        CommandId = $CommandProcess.Id
        Root = $Root
        Clock = $Clock
        Started = $Started
        Tracks = @{}
        JobSamples = [Collections.Generic.List[object]]::new()
        PeakChildren = 0
    }
}

function Update-TestProcessSampling($State) {
    $parents = [TestProcessJob]::Parents()
    $hostNames = @('HappyPhoton.Tests', 'HappyPhoton.Headless.Tests', 'HappyPhoton.LibRaw.Interop.Tests')

    # A host escaping the job invalidates measurement; it must not silently disappear.
    foreach ($process in Get-Process -Name $hostNames -ErrorAction SilentlyContinue) {
        try {
            if (-not $process.HasExited -and $process.Path -and
                $process.Path.StartsWith($State.Root, [StringComparison]::OrdinalIgnoreCase) -and
                -not $State.Job.Contains($process)) {
                throw "Sampled test host is outside the measurement job: $($process.Id); parent $($parents[$process.Id]); job PIDs $($State.Job.ProcessIds() -join ',')."
            }
        } catch [ArgumentException], [InvalidOperationException], [ComponentModel.Win32Exception] {
            if (-not $process.HasExited) { throw }
        } finally {
            $process.Dispose()
        }
    }

    foreach ($processId in $State.Job.ProcessIds()) {
        $process = $null

        try {
            $process = [Diagnostics.Process]::GetProcessById($processId)
            $null = $process.Handle
            if (-not $State.Job.Contains($process)) { continue }

            $start = $process.StartTime.ToUniversalTime()
            $key = "$processId-$($start.Ticks)"
            if ($State.Tracks.ContainsKey($key)) { continue }

            $parentId = if ($parents.ContainsKey($processId)) { $parents[$processId] } else { -1 }
            $name = $process.ProcessName
            $birth = [Math]::Max(0.0, ($start - $State.Started.UtcDateTime).TotalSeconds)
            $samples = [Collections.Generic.List[object]]::new()
            $samples.Add([pscustomobject]@{ before = $birth; after = $birth; cpu = 0.0 })
            $State.Tracks[$key] = @{
                Key = $key
                Process = $process
                ParentId = $parentId
                Start = $start
                End = $null
                Name = $name
                Path = $process.Path
                IsHost = $name -in $hostNames
                ExecutionEvidence = if ($name -in $hostNames) { Get-TestHostExecutionEvidence $process } else { '' }
                Descendant = $false
                Kind = if ($processId -eq $State.CommandId) { 'driver' } else { 'unknown' }
                Samples = $samples
                Final = $false
            }
            $process = $null
        } catch [ArgumentException], [InvalidOperationException], [ComponentModel.Win32Exception] {
            # An unseen process remains in the job total and therefore in the upper bound.
        } finally {
            if ($process) { $process.Dispose() }
        }
    }

    $ordered = @($State.Tracks.Values | Sort-Object Start)
    $children = 0
    $byId = @{}

    foreach ($track in $ordered) {
        $process = $track.Process
        $parent = $byId[$track.ParentId]
        $byId[$process.Id] = $track

        if ($null -ne $parent -and $null -ne $parent.End -and $parent.End -lt $track.Start) { $parent = $null }

        $track.Descendant = $null -ne $parent -and ($parent.Kind -eq 'test' -or $parent.Descendant)

        if ($track.Descendant) {
            $track.Kind = 'test'
        } elseif ($track.IsHost) {
            # Root discovery/info helpers need execution evidence to prove test CPU.
            $track.Kind = if ($track.ExecutionEvidence) { 'test' } else { 'unknown' }
        } elseif ($null -ne $parent -and $parent.Kind -eq 'driver') {
            $track.Kind = 'driver'
        }

        if ($track.Descendant -and -not $process.HasExited) { $children++ }
        if ($track.Final) { continue }

        $before = $State.Clock.Elapsed.TotalSeconds
        $cpu = [TestProcessJob]::CpuSeconds($process)
        $after = $State.Clock.Elapsed.TotalSeconds
        $track.Samples.Add([pscustomobject]@{ before = $before; after = $after; cpu = $cpu })

        if ($process.HasExited) {
            # Read once more after observing exit, so the retained counter includes its final CPU.
            $before = $State.Clock.Elapsed.TotalSeconds
            $cpu = [TestProcessJob]::CpuSeconds($process)
            $after = $State.Clock.Elapsed.TotalSeconds
            $track.Samples.Add([pscustomobject]@{ before = $before; after = $after; cpu = $cpu })
            $track.End = $process.ExitTime.ToUniversalTime()
            $track.Final = $true
        }

        # Exit counters remain in the CPU series; memory is sampled only while alive.
        if ($track.IsHost -and -not $track.Final) {
            $process.Refresh()
            $pagedBytes = $process.PagedMemorySize64
            if ($process.HasExited) { continue }

            [pscustomobject]@{
                utc = [DateTimeOffset]::UtcNow.ToString('o')
                instance = $track.Key
                host = $track.Name
                pid = $process.Id
                parentPid = $track.ParentId
                processStartUtc = $track.Start.ToString('o')
                path = $track.Path
                pagedBytes = $pagedBytes
                cpuSeconds = $cpu
            }
        }
    }

    $State.PeakChildren = [Math]::Max($State.PeakChildren, $children)
    $before = $State.Clock.Elapsed.TotalSeconds
    $cpu = $State.Job.CpuSeconds()
    $after = $State.Clock.Elapsed.TotalSeconds
    $State.JobSamples.Add([pscustomobject]@{ before = $before; after = $after; cpu = $cpu })
}

# Use inner samples for proven CPU and outer job samples for an upper bound.
# This brackets exactly five seconds without interpolating counters across a burst.
function Get-TestCpuWindowSummary($JobSamples, $Tracks, [int] $PeakChildren, [DateTimeOffset] $Started) {
    $peaks = @{}

    foreach ($kind in @('upper', 'lower', 'command')) {
        $peaks[$kind] = @{ processors = 0.0; startUtc = $null; endUtc = $null }
    }

    $outer = 0
    $cursors = @{}

    foreach ($end in $JobSamples) {
        $stop = $end.before
        $start = $stop - 5
        if ($start -lt 0) { continue }

        while ($outer + 1 -lt $JobSamples.Count -and $JobSamples[$outer + 1].after -le $start) {
            $outer++
        }

        $command = [Math]::Max(0.0, $end.cpu - $JobSamples[$outer].cpu)
        $proven = @{ driver = 0.0; test = 0.0 }

        foreach ($track in $Tracks) {
            if ($track.Kind -eq 'unknown') { continue }

            if (-not $cursors.ContainsKey($track.Key)) { $cursors[$track.Key] = @{ first = 0; last = -1 } }

            $cursor = $cursors[$track.Key]
            $samples = $track.Samples

            while ($cursor.first -lt $samples.Count -and $samples[$cursor.first].before -lt $start) {
                $cursor.first++
            }

            while ($cursor.last + 1 -lt $samples.Count -and $samples[$cursor.last + 1].after -le $stop) {
                $cursor.last++
            }

            if ($cursor.last -le $cursor.first) { continue }

            $proven[$track.Kind] += [Math]::Max(0.0, $samples[$cursor.last].cpu - $samples[$cursor.first].cpu)
        }

        $values = @{ upper = [Math]::Max(0.0, $command - $proven.driver) / 5; lower = $proven.test / 5; command = $command / 5 }

        foreach ($kind in $values.Keys) {
            if ($values[$kind] -gt $peaks[$kind].processors) {
                $peaks[$kind] = @{
                    processors = $values[$kind]
                    startUtc = $Started.AddSeconds($start).ToString('o')
                    endUtc = $Started.AddSeconds($stop).ToString('o')
                }
            }
        }
    }

    return @{ windowSeconds = 5; upper = $peaks.upper; lower = $peaks.lower; command = $peaks.command; peakChildProcesses = $PeakChildren }
}
