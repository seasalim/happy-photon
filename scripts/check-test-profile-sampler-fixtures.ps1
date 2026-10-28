# Loaded by check-test-profile-fixtures.ps1; synthetic processes never run a test suite.
$sampler = Join-Path $PSScriptRoot 'measure-suite.ps1'
$fixtureOutput = Join-Path (Split-Path $PSScriptRoot) "artifacts/test-profile/fixtures-$([Guid]::NewGuid().ToString('N'))"

& {
    $exitedObjects = [Collections.Generic.List[object]]::new()

    function Get-Process {
        param($Name, $ErrorAction)

        $current = [Diagnostics.Process]::GetCurrentProcess()

        try {
            foreach ($processId in @(-1)) {
                $fake = [pscustomobject]@{
                    Id = $processId; Path = Join-Path $root 'fixture.exe'; HasExited = $true
                    StartTime = $current.StartTime; ProcessName = 'HappyPhoton.Tests'
                    PagedMemorySize64 = 100; TotalProcessorTime = [TimeSpan]::Zero
                }
                $fake | Add-Member NoteProperty Disposed $false
                if ($processId -eq -1) { $exitedObjects.Add($fake) }

                $fake | Add-Member ScriptMethod Refresh { }
                $fake | Add-Member ScriptMethod Dispose {
                    $this.Disposed = $true
                }
                $fake
            }
        } finally {
            $current.Dispose()
        }
    }

    $destination = Join-Path $fixtureOutput 'exited-pid'
    & $sampler -Command pwsh -CommandArguments @('-NoProfile', '-Command', 'exit 7') `
        -OutputDirectory $destination -TrxDirectory $fixtures -Workload solo
    Assert-Equal $LASTEXITCODE 7 'R3-1 command exit code preserved after an exited PID'
    Assert-Equal @($exitedObjects | Where-Object { -not $_.Disposed }).Count 0 'R3-1 exited enumerated process disposed'
    $summary = Get-Content -Raw "$destination/summary.json" | ConvertFrom-Json
    $rows = @(Get-Content "$destination/samples.txt" | ForEach-Object { $_ | ConvertFrom-Json })
    $cpu = Get-Content -Raw "$destination/cpu-samples.json" | ConvertFrom-Json
    Assert-Equal ($cpu.job.Count -gt 1) $true 'R3-1 sampling continues after an exited PID'
    Assert-Equal @($rows | Where-Object pid -eq -1).Count 0 'R3-1 no sample for an exited PID'
    Assert-Equal $summary.hosts.Count 0 'An exited PID must not become a tracked test host'
    Assert-Equal $summary.measurementValid $true 'An exited PID must not invalidate measurement'
    Assert-Equal (Test-Path "$destination/attribution.json") $true 'R3-1 attribution still written'
}

& {
    $signalName = "Local\TestProfile-$([Guid]::NewGuid().ToString('N'))"
    $signal = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::ManualReset, $signalName)
    $fixtureState = @{ child = $null }
    $childCommand = @"
`$ready = [Threading.EventWaitHandle]::OpenExisting('$signalName')
try {
    if (-not `$ready.WaitOne(30000)) { throw 'Sampler did not wait for its command.' }

    Write-Output 'command completed'
    exit 7
} finally {
    `$ready.Dispose()
}
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childCommand))

    function Get-Process {
        param($Name, $ErrorAction)

        # The command is blocked on this signal until cleanup calls WaitForExit.
        # Decorate the real process so the assertion tests ordering without a sleep.
        $fixtureState.child = $child
        $child | Add-Member NoteProperty FixtureSignal $signal
        $child | Add-Member NoteProperty FixtureWaitCalled $false
        $child | Add-Member ScriptMethod WaitForExit {
            $this.FixtureWaitCalled = $true
            $null = $this.FixtureSignal.Set()
            $null = [Diagnostics.Process].GetMethod('WaitForExit', [type[]]@()).Invoke($this, @())
        } -Force
        throw 'Injected sampling failure'
    }

    try {
        $destination = Join-Path $fixtureOutput 'sampling-failure'
        $caught = $null

        try {
            & $sampler -Command pwsh -CommandArguments @('-NoProfile', '-EncodedCommand', $encoded) `
                -OutputDirectory $destination -Workload solo
        } catch {
            $caught = $_.Exception.Message
        }

        Assert-Equal $caught 'Injected sampling failure' 'R3-1 original sampling error propagates'
        Assert-Equal $fixtureState.child.FixtureWaitCalled $true 'R3-1 cleanup waits for the running command'
        $summary = Get-Content -Raw "$destination/summary.json" | ConvertFrom-Json
        Assert-Equal $summary.exitCode 7 'R3-1 failed sampler observes actual command completion'
        Assert-Equal $summary.measurementValid $false 'Failed sampler evidence is not selectable'
        Assert-Equal ($null -eq $summary.cpu) $true 'Failed sampler must not publish CPU bounds'
        Assert-Equal $summary.commandOutputComplete $true 'R3-1 output drains before the error propagates'
        Assert-Equal ((Get-Content -Raw "$destination/command.stdout.txt").Trim()) 'command completed' 'R3-1 command completes without termination'
    } finally {
        # Also release the fixture command if the regression returns prematurely.
        $null = $signal.Set()
        $signal.Dispose()
    }
}

& {
    function Get-Process {
        param($Name, $ErrorAction)

        $outside = [Diagnostics.Process]::GetCurrentProcess()
        # Treat this real, uncontained process as a test host returned by enumeration.
        $State.Root = [IO.Path]::GetDirectoryName($outside.Path) + [IO.Path]::DirectorySeparatorChar

        return $outside
    }

    $destination = Join-Path $fixtureOutput 'outside-job'
    $caught = ''

    try {
        & $sampler -Command pwsh -CommandArguments @('-NoProfile', '-Command', 'exit 7') -OutputDirectory $destination
    } catch {
        $caught = $_.Exception.Message
    }

    Assert-Equal ($caught -like 'Sampled test host is outside the measurement job:*') $true 'Escaped host invalidates measurement'
    $summary = Get-Content -Raw "$destination/summary.json" | ConvertFrom-Json
    Assert-Equal $summary.measurementValid $false 'Escaped-host evidence is not selectable'
    Assert-Equal ($null -eq $summary.cpu) $true 'Escaped-host evidence must not publish CPU bounds'
}

& {
    . (Join-Path $PSScriptRoot 'test-process-sampling.ps1')

    function Get-Process {
        param($Name, $ErrorAction)

        # All fixture processes already have retained final counters.
    }

    $job = [pscustomobject]@{}
    $job | Add-Member ScriptMethod ProcessIds { return @() }
    $job | Add-Member ScriptMethod CpuSeconds { return 150.0 }
    $started = [DateTimeOffset]::Parse('2026-01-01T00:00:00Z')
    $clock = @{ Elapsed = @{ TotalSeconds = 5.0 } }
    $state = New-TestProcessSampling $job @{ Id = 1 } '' $clock $started
    $state.JobSamples.Add([pscustomobject]@{ before = 0.0; after = 0.0; cpu = 0.0 })
    $processes = @(
        @{ Id = 1; Parent = 0; Name = 'pwsh'; Evidence = ''; Cpu = 50.0 }
        @{ Id = 2; Parent = 1; Name = 'HappyPhoton.Tests'; Evidence = 'xunit-v3-automated-run'; Cpu = 10.0 }
        @{ Id = 3; Parent = 2; Name = 'dotnet'; Evidence = ''; Cpu = 10.0 }
        @{ Id = 4; Parent = 3; Name = 'HappyPhoton.Tests'; Evidence = ''; Cpu = 20.0 }
        @{ Id = 5; Parent = 1; Name = 'HappyPhoton.Tests'; Evidence = ''; Cpu = 60.0 }
    )

    foreach ($row in $processes) {
        $key = $row.Id.ToString()
        $state.Tracks[$key] = @{
            Key = $key
            Process = @{ Id = $row.Id; HasExited = $true }
            ParentId = $row.Parent
            Start = $started.UtcDateTime.AddTicks($row.Id)
            End = $started.UtcDateTime.AddSeconds(5)
            Name = $row.Name
            IsHost = $row.Name -eq 'HappyPhoton.Tests'
            ExecutionEvidence = $row.Evidence
            Descendant = $false
            Kind = if ($row.Id -eq 1) { 'driver' } else { 'unknown' }
            Final = $true
            Samples = @(
                [pscustomobject]@{ before = 0.0; after = 0.0; cpu = 0.0 }
                [pscustomobject]@{ before = 5.0; after = 5.0; cpu = $row.Cpu }
            )
        }
    }

    Update-TestProcessSampling $state | Out-Null
    Assert-Equal $state.Tracks['2'].Kind 'test' 'Execution evidence proves a root test host'
    Assert-Equal $state.Tracks['3'].Kind 'test' 'A nested dotnet driver belongs to the test tree'
    Assert-Equal $state.Tracks['4'].Kind 'test' 'A host-named descendant belongs to the test tree without execution evidence'
    Assert-Equal $state.Tracks['4'].Descendant $true 'Nested discovery retains proven test ancestry'
    Assert-Equal $state.Tracks['5'].Kind 'unknown' 'A root discovery helper remains unproven'
    Assert-Equal $state.Tracks['5'].Descendant $false 'Root discovery has no test ancestry'
    $summary = Get-TestCpuWindowSummary $state.JobSamples @($state.Tracks.Values) 0 $started
    Assert-Equal $summary.lower.processors 8 'The lower bound includes nested discovery CPU and excludes root discovery CPU'
    Assert-Equal $summary.upper.processors 20 'The upper bound subtracts only the root driver CPU'
}

. (Join-Path $PSScriptRoot 'check-test-profile-survivor-fixtures.ps1')
