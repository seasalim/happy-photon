param(
    [ValidateSet('Oracle','Synthetic','Public','WP1','G3','Report','Owners','Metadata','Harness')]
    [string]$Gate = 'Synthetic',
    [ValidateRange(1,5)][int]$Processes = 3,
    [ValidateRange(1,120)][int]$TimeoutSeconds = 120,
    [ValidateRange(1,600)][int]$BudgetSeconds = 600
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$filter = switch ($Gate) {
    'Oracle' { 'FullyQualifiedName~StraightenGateWp3BaselineTests.SkylineOracle|FullyQualifiedName~StraightenGateWp3BaselineTests.SkylineReferenceClosedLoop' }
    'Synthetic' { 'FullyQualifiedName~StraightenGateWp3BaselineTests.SyntheticMembership|FullyQualifiedName~StraightenGateWp3BaselineTests.G5FrameMembership' }
    'Public' { 'FullyQualifiedName~StraightenGateWp3BaselineTests.PublicNegativeMembership|FullyQualifiedName~StraightenGateWp3BaselineTests.PublicG5FrameMembership|FullyQualifiedName~HorizonDetectionFixtureTests.G4NoStructure' }
    'WP1' { 'FullyQualifiedName~HorizonDetectionSyntheticTests.G1AccuracyAndG2ClosedLoop|FullyQualifiedName~StraightenGateSyntheticTests.G1Before|FullyQualifiedName~StraightenGateSyntheticTests.G2Before' }
    'G3' { 'FullyQualifiedName~StraightenGateWp3BaselineTests.PublicG3Membership|FullyQualifiedName~HorizonDetectionFixtureTests.G3Accuracy' }
    'Report' { 'FullyQualifiedName~HorizonDetectionFixtureTests.ReportOnly' }
    'Owners' { 'FullyQualifiedName~StraightenGateWp3BaselineTests.OwnerLandscapeMembership|FullyQualifiedName~StraightenGateWp3BaselineTests.OwnerJudgement|FullyQualifiedName~StraightenGateWp3BaselineTests.OwnerNegativeMembership|FullyQualifiedName~StraightenGateWp3BaselineTests.OwnerReportSweep|FullyQualifiedName~StraightenGateWp3BaselineTests.OwnerG3Membership' }
    'Metadata' { 'FullyQualifiedName~StraightenGateWp3BaselineTests.OwnerLandscapeMetadata' }
    'Harness' { 'FullyQualifiedName~StraightenGateWp3HarnessTests' }
}
$clock = [Diagnostics.Stopwatch]::StartNew()
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

    $start.Environment['HAPPY_PHOTON_PERF'] = '0'
    $start.Environment['HAPPY_PHOTON_STRAIGHTEN_REPORT'] = $(if ($Gate -eq 'Report') { '1' } else { '0' })
    $start.Environment['HAPPY_PHOTON_STRAIGHTEN_DUMP'] = '0'
    $start.Environment['HAPPY_PHOTON_STRAIGHTEN_OWNER_DUMP'] = '0'
    $ownerDirectory = [Environment]::GetEnvironmentVariable('HAPPY_PHOTON_STRAIGHTEN_DIR')
    $process = [Diagnostics.Process]::Start($start)

    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()

        if (-not $process.WaitForExit([int]($limit * 1000))) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Deferred ${Gate}: process $run exceeded $limit seconds"
        }

        $resultText = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()

        if ($ownerDirectory) {
            $resultText = $resultText.Replace($ownerDirectory, '[owner directory]')
        }

        Write-Output $resultText
        Write-Output "WP3_PROCESS gate=$Gate run=$run exit=$($process.ExitCode) elapsed_total_seconds=$($clock.Elapsed.TotalSeconds)"
        if ($process.ExitCode -ne 0) { $failed = $true }
    }
    finally {
        $process.Dispose()
    }
}

if ($failed) { throw "$Gate reported a failure; review measurements before production changes" }
