param([ValidateSet('Build', 'Probe')][string]$Action = 'Probe', [int]$TimeoutSeconds = 120)
$ErrorActionPreference = 'Stop'
$start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
$start.WorkingDirectory = Split-Path $PSScriptRoot
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['HP_EXPORT_BASELINE'] = '1'
$start.Arguments = if ($Action -eq 'Build') {
    'build HappyPhoton.sln -c Release'
} else {
    'test HeadlessTests/HappyPhoton.Headless.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~ExportWorkspaceBaselineMeasurements --logger "console;verbosity=detailed"'
}
$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $start
$watch = [System.Diagnostics.Stopwatch]::StartNew()
try {
    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        Write-Output $stdout.GetAwaiter().GetResult()
        Write-Output $stderr.GetAwaiter().GetResult()
        throw "$Action timed out after $TimeoutSeconds seconds; process tree terminated."
    }
    Write-Output $stdout.GetAwaiter().GetResult()
    Write-Output $stderr.GetAwaiter().GetResult()
    Write-Output "$Action elapsed: $($watch.Elapsed.TotalSeconds) seconds"
    if ($process.ExitCode -ne 0) { throw "dotnet exited $($process.ExitCode)" }
} finally { $process.Dispose() }
