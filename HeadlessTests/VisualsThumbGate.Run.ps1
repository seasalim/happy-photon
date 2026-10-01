param(
    [Parameter(Mandatory)][ValidateSet('G1', 'G2', 'G3', 'G4', 'Verify')][string] $Gate,
    [ValidateRange(1, 3)][int] $Runs = 1
)

$filters = @{
    G1 = 'FullyQualifiedName~DualRangeTrackTests|FullyQualifiedName~LocalsViewModelTests.RangeEntryTypingOwnsWindowShortcuts|FullyQualifiedName~LocalsViewModelTests.RangeEntryEnterCommitsExactlyOneHistoryStep|FullyQualifiedName~LocalsViewModelTests.RangeEntryEscapeDiscardsDraftAcrossReopenAndFocusChange|FullyQualifiedName~CompactSliderInputGateTests|FullyQualifiedName~CompactSliderWheel|FullyQualifiedName~CompactSliderEntryTests|FullyQualifiedName~SliderFocusHandoffTests'
    G2 = 'FullyQualifiedName~SliderAndFooterMetricTests|FullyQualifiedName~DevelopHeaderBaselineTests|FullyQualifiedName~DevelopCollapseBaselineTests'
    G3 = 'FullyQualifiedName~VisualsThumbGateTests.MarkCentres'
    G4 = 'FullyQualifiedName~VisualsThumbGateTests.RangeGestures'
}

for ($run = 1; $run -le $Runs; $run++) {
    $process = [Diagnostics.Process]::new()
    $process.StartInfo.FileName = if ($Gate -eq 'Verify') { 'pwsh' } else { 'dotnet' }
    $process.StartInfo.WorkingDirectory = Split-Path -Parent $PSScriptRoot
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $arguments = if ($Gate -eq 'Verify') {
        @('-NoProfile', '-File', './scripts/verify.ps1', '-NoBuild', '-NoRestore',
            '-ResultsDirectory', 'HeadlessTests/TestResults')
    } else {
        @('test', 'HeadlessTests/HappyPhoton.Headless.Tests.csproj', '-c', 'Release',
            '--no-build', '--no-restore', '--filter', $filters[$Gate],
            '--logger', $(if ($Gate -in 'G3', 'G4') { 'console;verbosity=detailed' } else { 'console;verbosity=normal' }))
    }

    foreach ($argument in $arguments) {
        $process.StartInfo.ArgumentList.Add($argument)
    }

    $watch = [Diagnostics.Stopwatch]::StartNew()
    $null = $process.Start()
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()

    if (-not $process.WaitForExit(120000)) {
        & taskkill.exe /PID $process.Id /T /F

        if (-not $process.HasExited) {
            $process.Kill($true)
        }

        if ($stdout.Wait(2000)) { $stdout.Result }
        if ($stderr.Wait(2000)) { $stderr.Result }
        $process.Dispose()
        throw "$Gate sample $run exceeded the 120-second timeout."
    }

    $stdout.Result
    $stderr.Result
    Write-Output "$Gate sample=$run seconds=$($watch.Elapsed.TotalSeconds) exit=$($process.ExitCode)"
    $exitCode = $process.ExitCode
    $process.Dispose()

    if ($exitCode -ne 0) {
        exit $exitCode
    }
}
