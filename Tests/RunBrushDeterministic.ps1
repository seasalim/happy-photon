param(
    [ValidateSet('Agreement','Catalog','Persistence')][string]$Gate = 'Agreement',
    [ValidateRange(1,10)][int]$Runs = 3,
    [ValidateRange(1,120)][int]$TimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
$method = switch ($Gate) {
    'Agreement' { 'LocalsContractPrototypeTests.BrushGridAgreesWithIndependentOracle' }
    'Catalog' { 'LocalsBrushCatalogGrowthTests.BrushHistoryAtCap' }
    'Persistence' { 'LocalsContractPrototypeTests.BrushPersistence' }
}
$budget = [Diagnostics.Stopwatch]::StartNew()
for ($run = 1; $run -le $Runs; $run++) {
    $remaining = [Math]::Min($TimeoutSeconds, 600 - $budget.Elapsed.TotalSeconds)
    if ($remaining -le 0) { throw "DEFERRED: $Gate exhausted its 10-minute budget" }
    $info = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $info.WorkingDirectory = Split-Path -Parent $PSScriptRoot
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in @('test', 'Tests/HappyPhoton.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore',
        '--filter', "FullyQualifiedName~HappyPhoton.Tests.$method", '--logger', 'console;verbosity=detailed')) {
        $info.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $finished = $process.WaitForExit([int]($remaining * 1000))
        if (!$finished) { $process.Kill($true); $process.WaitForExit() }
        Write-Output "gate=$Gate run=$run/$Runs"
        Write-Output $stdout.GetAwaiter().GetResult()
        Write-Output $stderr.GetAwaiter().GetResult()
        if (!$finished) { throw "DEFERRED: $Gate timed out after $remaining seconds" }
        if ($process.ExitCode -ne 0) { throw "Measurement failed: $($process.ExitCode)" }
    } finally { $process.Dispose() }
}
