# Loaded by check-test-profile-fixtures.ps1; synthetic processes never run a test suite.
$sampler = Join-Path $PSScriptRoot 'measure-suite.ps1'
$fixtureOutput = Join-Path (Split-Path $PSScriptRoot) "artifacts/test-profile/fixtures-$([Guid]::NewGuid().ToString('N'))"

& {
    $exitedObjects = [Collections.Generic.List[object]]::new()

    function Get-Process {
        param($Name, $ErrorAction)

        $current = [Diagnostics.Process]::GetCurrentProcess()

        try {
            foreach ($processId in @(-1, $PID)) {
                $fake = [pscustomobject]@{
                    Id = $processId; Path = Join-Path $root 'fixture.exe'
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
    Assert-Equal ($rows.Count -gt 0) $true 'R3-1 sampling continues after an exited PID'
    Assert-Equal @($rows | Where-Object pid -eq -1).Count 0 'R3-1 no sample for an exited PID'
    Assert-Equal $summary.hosts.Count 1 'R3-1 only the surviving instance is tracked'
    Assert-Equal $summary.hosts[0].aliveAfterCommand $true 'R3-1 surviving host reported without termination'
    Assert-Equal (Test-Path "$destination/attribution.json") $true 'R3-1 attribution still written'
}

& {
    $signalName = "Local\TestProfile-$([Guid]::NewGuid().ToString('N'))"
    $signal = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::ManualReset, $signalName)
    $state = @{ child = $null }
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
        $state.child = $child
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
        Assert-Equal $state.child.FixtureWaitCalled $true 'R3-1 cleanup waits for the running command'
        $summary = Get-Content -Raw "$destination/summary.json" | ConvertFrom-Json
        Assert-Equal $summary.exitCode 7 'R3-1 failed sampler observes actual command completion'
        Assert-Equal $summary.commandOutputComplete $true 'R3-1 output drains before the error propagates'
        Assert-Equal ((Get-Content -Raw "$destination/command.stdout.txt").Trim()) 'command completed' 'R3-1 command completes without termination'
    } finally {
        # Also release the fixture command if the regression returns prematurely.
        $null = $signal.Set()
        $signal.Dispose()
    }
}
