[CmdletBinding()]
param(
    [ValidateSet('Build', 'Deterministic', 'Cost', 'Policy')]
    [string] $Probe = 'Deterministic',
    [ValidateRange(1, 5)]
    [int] $Runs = 1,
    [ValidateRange(1, 120)]
    [int] $TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$baseline = $null
$costs = @()
$durations = @()

for ($sample = 1; $sample -le $Runs; $sample++) {
    $process = [Diagnostics.Process]::new()
    $process.StartInfo.FileName = 'dotnet'
    $process.StartInfo.WorkingDirectory = $repo
    $method = if ($Probe -eq 'Cost') { 'MeasureUpdateCanResetContext' } else { 'MeasureDeterministicBaseline' }
    $process.StartInfo.Arguments = "test HeadlessTests/HappyPhoton.Headless.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~DevelopEditDotBaselineTests.$method --logger `"console;verbosity=detailed`""

    if ($Probe -eq 'Build') {
        $process.StartInfo.Arguments = 'build HappyPhoton.sln -c Release --no-restore'
    }

    if ($Probe -eq 'Policy') {
        $process.StartInfo.FileName = 'pwsh'
        $process.StartInfo.Arguments = '-NoProfile -File scripts/verify.ps1 -PolicyOnly'
    }

    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $timer = [Diagnostics.Stopwatch]::StartNew()

    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()

        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            throw "Sample $sample exceeded its $TimeoutSeconds-second bound."
        }

        $durations += $timer.Elapsed.TotalSeconds
        $result = $stdout.GetAwaiter().GetResult()
        Write-Output "SAMPLE $sample elapsed=$($timer.Elapsed.TotalSeconds) seconds exit=$($process.ExitCode)"
        Write-Output $result
        Write-Output $stderr.GetAwaiter().GetResult()

        if ($process.ExitCode -ne 0) {
            throw "Sample $sample failed."
        }

        if ($Probe -eq 'Deterministic') {
            $metrics = (($result -split '\r?\n') | Where-Object { $_ -match '^ (D[2456]|L[1-6]|SCENE|HEADER-FILL|CHEVRON) ' }) -join "`n"

            if ([string]::IsNullOrWhiteSpace($metrics)) {
                throw 'No deterministic measurements were emitted.'
            }

            if ($sample -eq 1) {
                $baseline = $metrics
            }
            elseif ($metrics -cne $baseline) {
                throw "Sample $sample differs from sample 1; report instability."
            }
        }

        if ($Probe -eq 'Cost') {
            if ($result -notmatch 'D7 medianUs=([0-9.]+); bytesPerCall=([0-9.]+)') {
                throw 'No cost measurement was emitted.'
            }

            $costs += [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
        }
    }
    finally {
        $process.Dispose()
    }
}

if ($Probe -eq 'Deterministic') {
    Write-Output "IDENTICAL: all $Runs deterministic samples; reported values are medians."
}

if ($Probe -eq 'Cost') {
    $sorted = @($costs | Sort-Object)
    $middle = [int][Math]::Floor($Runs / 2)
    $median = if ($Runs % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
    Write-Output "D7 process medians (microseconds): $($costs -join ', '); median=$median"
}

Write-Output "Sample durations (seconds): $($durations -join ', ')"
