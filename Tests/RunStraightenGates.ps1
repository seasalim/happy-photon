# Run under the workflow measure lease; baseline-role wrappers already hold it.
param(
    [ValidateSet('G1','G1Blurred','G2','G3','G4','G5','G5Detector','Dump','Owner','OwnerDump')][string]$Gate = 'G5',
    [ValidateSet('canon-eos-6d-iso-6400.cr2','iphone-14-pro-iso-1000.heic','nikon-d300-colorchecker.nef','skyline','fbm')]
    [string]$Fixture = 'canon-eos-6d-iso-6400.cr2',
    [switch]$HorizontalRelabelOnly,
    [ValidateRange(1,5)][int]$Processes = 5,
    [ValidateRange(1,120)][int]$TimeoutSeconds = 120,
    [ValidateRange(1,600)][int]$BudgetSeconds = 600
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$method = switch ($Gate) {
    'G1' { 'StraightenGateSyntheticTests.G1Before' }
    'G1Blurred' { 'StraightenGateSyntheticTests.G1BlurredBefore' }
    'G2' { 'StraightenGateSyntheticTests.G2Before' }
    'G3' { 'StraightenGateLabelTests.G3Before' }
    'G4' { 'StraightenGatePresenceTests.G4Before' }
    'G5' { 'StraightenGateTimingTests.G5Before' }
    'G5Detector' { 'StraightenGateTimingTests.G5Detector' }
    'Dump' { 'StraightenGateLabelTests.DumpPreviewBases' }
    'Owner' { 'StraightenGateOwnerTests.OwnerPresence|FullyQualifiedName~StraightenGateOwnerTests.OwnerG3Before' }
    'OwnerDump' { 'StraightenGateOwnerTests.DumpOwnerPreviewBases' }
}
$filter = "FullyQualifiedName~$method"

if ($HorizontalRelabelOnly) {
    if ($Gate -ne 'G3') { throw 'HorizontalRelabelOnly requires G3' }
    $filter += '&(DisplayName~m2462362|DisplayName~sony-a9m3)'
}

$clock = [Diagnostics.Stopwatch]::StartNew()
$records = [Collections.Generic.List[object]]::new()
$failed = $false

for ($run = 1; $run -le $Processes; $run++) {
    $remaining = $BudgetSeconds - $clock.Elapsed.TotalSeconds
    if ($remaining -le 0) { throw "Deferred ${Gate}: total budget exhausted" }
    $limit = [Math]::Min($TimeoutSeconds, $remaining)
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.WorkingDirectory = $repo
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('test', (Join-Path $PSScriptRoot 'HappyPhoton.Tests.csproj'), '-c', 'Release',
        '--no-build', '--no-restore', '--filter', $filter, '--logger', 'console;verbosity=detailed')) {
        $start.ArgumentList.Add($argument)
    }
    $start.Environment['HAPPY_PHOTON_STRAIGHTEN_FIXTURE'] = $Fixture
    $start.Environment['HAPPY_PHOTON_STRAIGHTEN_DUMP'] = $(if ($Gate -eq 'Dump') { '1' } else { '0' })
    $start.Environment['HAPPY_PHOTON_STRAIGHTEN_OWNER_DUMP'] = $(if ($Gate -eq 'OwnerDump') { '1' } else { '0' })
    # Inherit the owner's opt-in folder only from the caller, never from a saved path.
    $ownerDirectory = [Environment]::GetEnvironmentVariable('HAPPY_PHOTON_STRAIGHTEN_DIR')

    if ($null -eq $ownerDirectory) {
        $null = $start.Environment.Remove('HAPPY_PHOTON_STRAIGHTEN_DIR')
    }
    else {
        $start.Environment['HAPPY_PHOTON_STRAIGHTEN_DIR'] = $ownerDirectory
    }

    $start.Environment['HAPPY_PHOTON_PERF'] = $(if ($Gate -in @('G5', 'G5Detector')) { '1' } else { '0' })
    $start.Environment['HAPPY_PHOTON_FULL_CPU'] = '1'
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit([int]($limit * 1000))) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Deferred $Gate`: process $run exceeded $limit seconds"
        }
        $text = $stdout.GetAwaiter().GetResult()
        Write-Output $text
        Write-Output $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { $failed = $true }
        foreach ($line in ($text -split "`n")) {
            if ($line -match '^\s*STRAIGHTEN (\{.*\})\s*$') {
                $records.Add(($Matches[1] | ConvertFrom-Json))
            }
        }
    }
    finally {
        $process.Dispose()
    }
    Write-Output "STRAIGHTEN_PROCESS run=$run elapsed_total_seconds=$($clock.Elapsed.TotalSeconds)"
}

if ($Gate -in @('G5', 'G5Detector')) {
    $rows = @($records | Where-Object gate -eq $Gate)
    if ($rows.Count -ne $Processes -or @($rows.pid | Sort-Object -Unique).Count -ne $Processes) {
        throw 'Missing distinct fresh test-process records'
    }
    foreach ($row in $rows) {
        if ($row.values.samples.Count -ne 11 -or $row.values.warmups -ne 3 -or $row.values.cpu -le 2) {
            throw 'Invalid timing sample count, warm-up count, or CPU configuration'
        }
    }
    $medians = @($rows | ForEach-Object { $_.values.median })
    $ordered = @($medians | Sort-Object)
    $median = $ordered[[int][Math]::Floor($ordered.Count / 2)]
    # WP3's synthetic fallback frames: 55 ms from their own tier (the 30 ms floor was baseline-only).
    $synthetic = $Fixture -in @('skyline', 'fbm')
    $minimum = if ($synthetic) { 0 } elseif ($Gate -eq 'G5Detector') { 0 } elseif ($Fixture.EndsWith('.heic')) { 15 } else { 22 }
    $maximum = if ($synthetic) { 55 } elseif ($Gate -eq 'G5Detector') { $(if ($Fixture.EndsWith('.nef')) { 55 } else { 50 }) } elseif ($Fixture.EndsWith('.heic')) { 30 } else { 40 }
    $valid = $median -ge $minimum -and $median -le $maximum
    Write-Output ('STRAIGHTEN_SUMMARY ' + (@{ fixture = $Fixture; processes = $Processes;
        medians = $medians; median_ms = $median; valid = $valid; qualified_sample_count = ($Processes -eq 5);
        pids = @($rows.pid) } | ConvertTo-Json -Compress))
    if (-not $valid) { $failed = $true }
}

if ($failed) { throw "$Gate baseline outside the envelope or test failure; retain results for owner review" }



