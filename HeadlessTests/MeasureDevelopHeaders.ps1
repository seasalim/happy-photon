[CmdletBinding()]
param(
    [ValidateRange(1, 3)]
    [int] $Runs = 3,
    [ValidateRange(1, 120)]
    [int] $TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$baseline = $null
$durations = @()

for ($sample = 1; $sample -le $Runs; $sample++) {
    $process = [Diagnostics.Process]::new()
    $process.StartInfo.FileName = 'dotnet'
    $process.StartInfo.WorkingDirectory = $repo
    $process.StartInfo.Arguments = 'test HeadlessTests/HappyPhoton.Headless.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~DevelopHeaderBaselineTests --logger "console;verbosity=detailed"'
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.CreateNoWindow = $true
    $timer = [Diagnostics.Stopwatch]::StartNew()

    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()

        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            throw "Sample $sample exceeded its $TimeoutSeconds-second bound."
        }

        $elapsed = $timer.Elapsed.TotalSeconds
        $durations += $elapsed
        $result = $stdout.GetAwaiter().GetResult()
        $errors = $stderr.GetAwaiter().GetResult()
        Write-Output "SAMPLE $sample elapsed=$elapsed seconds exit=$($process.ExitCode)"
        Write-Output $result
        Write-Output $errors

        if ($process.ExitCode -ne 0) {
            throw "Sample $sample failed."
        }

        $metrics = (($result -split '\r?\n') | Where-Object { $_ -match '^ (SCENE|L[1-6]) ' }) -join "`n"

        if ($sample -eq 1) {
            $baseline = $metrics
        }
        elseif ($metrics -cne $baseline) {
            throw "Sample $sample differs from sample 1; report instability instead of a single baseline."
        }
    }
    finally {
        $process.Dispose()
    }
}

Write-Output "All $Runs samples have identical L1-L6 values; each reported value is its median."
Write-Output "Sample durations (seconds): $($durations -join ', ')"
