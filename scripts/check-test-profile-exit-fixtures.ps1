# Loaded after the synthetic host is built; no test suite or timing gate runs here.
& {
    foreach ($failSummary in @($false, $true)) {
        $scenario = if ($failSummary) { 'exited-host-summary-failure' } else { 'exited-host' }
        $destination = Join-Path $fixtureOutput $scenario
        $trxDirectory = Join-Path $destination 'trx'
        $null = New-Item -ItemType Directory -Path $trxDirectory -Force

        if ($failSummary) { '{invalid' | Set-Content (Join-Path $trxDirectory 'hosts.json') }

        $token = "Local\SamplerExit-$([Guid]::NewGuid().ToString('N'))"
        $ready = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::ManualReset, "$token-ready")
        $observed = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::ManualReset, "$token-observed")
        $release = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::ManualReset, "$token-release")
        $responsePath = Join-Path $destination 'execution.rsp'
        $releasedPath = Join-Path $destination 'released.txt'
        @('-automated', '-run', '-3:fixture', "$token-ready", "$token-release", $releasedPath) | Set-Content $responsePath
        $fixtureState = @{ process = $null }
        $settingsPath = Join-Path $destination 'settings.json'
        @{ hostPath = $hostPath; responsePath = $responsePath; ready = "$token-ready"; observed = "$token-observed" } |
            ConvertTo-Json | Set-Content $settingsPath
        $savedFixture = $env:HP_SAMPLER_FIXTURE
        $env:HP_SAMPLER_FIXTURE = $settingsPath
        $command = @'
$settings = Get-Content -Raw -LiteralPath $env:HP_SAMPLER_FIXTURE | ConvertFrom-Json
$start = [Diagnostics.ProcessStartInfo]::new($settings.hostPath)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.ArgumentList.Add('@@')
$start.ArgumentList.Add($settings.responsePath)
$process = [Diagnostics.Process]::Start($start)
$ready = [Threading.EventWaitHandle]::OpenExisting($settings.ready)
$observed = [Threading.EventWaitHandle]::OpenExisting($settings.observed)

try {
    if (-not $ready.WaitOne(30000)) { throw 'Fixture host never became ready.' }
    if (-not $observed.WaitOne(30000)) { throw 'Sampler never retained the host exit.' }

    $process.WaitForExit()
    exit 7
} finally {
    $ready.Dispose()
    $observed.Dispose()
    $process.Dispose()
}
'@

        function Get-Process {
            param($Name, $ErrorAction)

            foreach ($process in Microsoft.PowerShell.Management\Get-Process -Name $Name -ErrorAction $ErrorAction) {
                if ($process.Path -eq $hostPath -and $null -eq $fixtureState.process) {
                    $fixtureState.process = [Diagnostics.Process]::GetProcessById($process.Id)
                    $null = $fixtureState.process.Handle
                }

                $process
            }

            foreach ($track in $State.Tracks.Values | Where-Object Path -EQ $hostPath) {
                # Release only after a live sample, and finish the command only after final CPU.
                if ($track.Samples.Count -gt 1) { $null = $release.Set() }
                if ($track.Final) { $null = $observed.Set() }
            }
        }

        try {
            $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
            & $sampler -Command pwsh -CommandArguments @('-NoProfile', '-EncodedCommand', $encoded) -OutputDirectory $destination -TrxDirectory $trxDirectory
            Assert-Equal $LASTEXITCODE 7 "$scenario preserves the wrapped failure"
            $rows = @(Get-Content "$destination/samples.txt" | ForEach-Object { $_ | ConvertFrom-Json })
            Assert-Equal ($rows.Count -gt 0) $true "$scenario observed live memory"
            Assert-Equal @($rows | Where-Object pagedBytes -LE 0).Count 0 "$scenario has no fabricated exit memory"
            $exitTime = [DateTimeOffset]$fixtureState.process.ExitTime.ToUniversalTime()
            Assert-Equal @($rows | Where-Object { [DateTimeOffset]$_.utc -ge $exitTime }).Count 0 "$scenario excludes exited hosts from memory"
            $cpu = Get-Content -Raw "$destination/cpu-samples.json" | ConvertFrom-Json
            $track = @($cpu.processes | Where-Object pid -EQ $fixtureState.process.Id)
            Assert-Equal $track.Count 1 "$scenario retains exited host CPU"
            Assert-Equal $track[0].kind 'test' "$scenario proves the synthetic execution host"
            $summary = Get-Content -Raw "$destination/summary.json" | ConvertFrom-Json
            Assert-Equal $summary.exitCode 7 "$scenario records the wrapped failure"
            Assert-Equal $summary.measurementValid (-not $failSummary) "$scenario summary validity"
            $attribution = Get-Content -Raw "$destination/attribution.json" | ConvertFrom-Json

            if ($failSummary) {
                Assert-Equal ([string]::IsNullOrWhiteSpace($summary.reportingError)) $false 'Summary failure is recorded'
                Assert-Equal $attribution.reportingError $summary.reportingError 'Attribution records the summary failure'
                Assert-Equal $attribution.g4Passed $false 'Failed reporting cannot qualify'
                Assert-Equal ($null -eq $summary.cpu) $true 'Failed reporting has no selectable CPU'
            } else {
                Assert-Equal $attribution.processes.Count 1 'Exited host has attribution'
                Assert-Equal $summary.hosts[0].aliveAfterCommand $false 'The host exited before the command'
            }
        } finally {
            $env:HP_SAMPLER_FIXTURE = $savedFixture
            $null = $release.Set()
            $null = $observed.Set()

            if ($null -ne $fixtureState.process) {
                try {
                    if (-not $fixtureState.process.WaitForExit(30000)) { throw 'Fixture host did not accept cleanup signal.' }
                } finally {
                    $fixtureState.process.Dispose()
                }
            }

            $ready.Dispose()
            $observed.Dispose()
            $release.Dispose()
        }
    }
}
