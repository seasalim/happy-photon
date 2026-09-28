[CmdletBinding()]
param([ValidateRange(1,5)][int] $Processes = 5)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $repo "artifacts/heal/wp5-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))"
$null = New-Item -ItemType Directory -Path $folder
$env:HAPPY_PHOTON_PERF = '1'
$env:HAPPY_PHOTON_FULL_CPU = '1'
$records = [Collections.Generic.List[object]]::new()
$arms = @(
    @{ name = 'latency'; test = 'ProductionLatencyAndMemory'; count = $Processes },
    @{ name = 'contention'; test = 'ProductionContention'; count = $Processes },
    @{ name = 'memory'; test = 'ProductionLatencyAndMemory'; count = 1 },
    @{ name = 'parity'; test = 'ProductionPreEncodeParity'; count = 1 }
)
foreach ($arm in $arms) {
    $env:HAPPY_PHOTON_WP5_ARM = $arm.name
    for ($sample = 1; $sample -le $arm.count; $sample++) {
        $log = Join-Path $folder "$($arm.name)-$sample.log"
        & dotnet test (Join-Path $repo 'HeadlessTests/HappyPhoton.Headless.Tests.csproj') `
            -c Release --no-build --no-restore --filter "FullyQualifiedName=HappyPhoton.Tests.FullResolutionGateTests.$($arm.test)" `
            --logger 'console;verbosity=detailed' *> $log
        if ($LASTEXITCODE -ne 0) { throw "Gate harness failed: $log" }
        $found = 0
        foreach ($line in Get-Content -LiteralPath $log) {
            if ($line -match '^\s*WP5 (\{.*\})\s*$') {
                $records.Add(($Matches[1] | ConvertFrom-Json))
                $found++
            }
        }
        if ($found -eq 0) { throw "No gate observations: $log" }
        Write-Host "$($arm.name) process $sample complete: $log"
    }
}
$records | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $folder 'measurements.json')
Write-Host "Qualification observations: $folder"
