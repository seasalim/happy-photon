param(
    [ValidateSet('Ticks','Export','Parity','Sheets','Compatibility','Diagnostics')][string]$Gate = 'Ticks',
    [ValidateSet('raw','standard')][string]$Fixture = 'raw'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $repo 'artifacts/ops-wp2'
$null = New-Item -ItemType Directory -Path $folder -Force
$env:HAPPY_PHOTON_PERF = '1'
$env:HAPPY_PHOTON_FULL_CPU = '1'
$env:HAPPY_PHOTON_OPS_FIXTURE = $Fixture
$method = switch ($Gate) {
    'Diagnostics' { 'WhitesBlacksGateTests.R1' }
    'Ticks' { 'WhitesBlacksGateTests.G1G2Ticks' }
    'Export' { 'WhitesBlacksGateTests.G3Export' }
    'Parity' { 'WhitesBlacksGateTests.G4Parity' }
    'Sheets' { 'WhitesBlacksGateTests.ProductionReviewSheets' }
    'Compatibility' { 'WhitesBlacksCompatibilityTests' }
}
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$records = [Collections.Generic.List[object]]::new()
$failed = $false
$count = if ($Gate -eq 'Ticks') { 5 } else { 1 }
for ($sample = 1; $sample -le $count; $sample++) {
    $log = Join-Path $folder "$Gate-$Fixture-$stamp-$sample.log"
    & dotnet test (Join-Path $PSScriptRoot 'HappyPhoton.Tests.csproj') -c Release --no-build --filter "FullyQualifiedName~$method" --logger 'console;verbosity=detailed' *> $log
    if ($LASTEXITCODE -ne 0) { $failed = $true }
    foreach ($line in Get-Content -LiteralPath $log) {
        if ($line -match '^\s*OPS_WP2 (\{.*\})\s*$') { $records.Add(($Matches[1] | ConvertFrom-Json)) }
    }
    Write-Output "Evidence: $log"
}
function Median($values) { $sorted = @($values | Sort-Object); $sorted[[int][Math]::Floor($sorted.Count / 2)] }
$summary = @()
if ($Gate -eq 'Ticks') {
    foreach ($amount in @(60, -60, 0)) {
        $rows = @($records | Where-Object { $_.values.amount -eq $amount })
        if ($rows.Count -ne 5 -or @($rows.pid | Sort-Object -Unique).Count -ne 5) { throw 'Missing five independent process pairs' }
        $control = Median @($rows | ForEach-Object { $_.values.control })
        $active = Median @($rows | ForEach-Object { $_.values.active })
        $increment = Median @($rows | ForEach-Object { $_.values.increment })
        $range = if ($amount -eq 0) { if ($Fixture -eq 'raw') { @(38,60) } else { @(34,53) } }
                 else { if ($Fixture -eq 'raw') { @(22,40) } else { @(15,30) } }
        $valid = $control -ge $range[0] -and $control -le $range[1]
        $pass = $valid -and $(if ($amount -eq 0) { $increment -le 10 } else { $increment -le 20 -and $active -le 150 })
        $summary += @{ amount = $amount; control = $control; active = $active; increment = $increment; valid = $valid; pass = $pass }
        if (!$pass) { $failed = $true }
    }
}
@{ gate = $Gate; fixture = $Fixture; records = @($records.ToArray()); summary = $summary; failed = $failed } |
    ConvertTo-Json -Depth 10 | Set-Content (Join-Path $folder "$Gate-$Fixture-$stamp.json")
$summary | ConvertTo-Json -Depth 5
if ($failed) { throw "$Gate did not qualify; retain the evidence for owner review" }
