# Run under the workflow host's exclusive measure lock. Build Release first.
param(
    [ValidateSet('Ticks','Contention','Export','Memory','Parity','Proof','Sheets')][string]$Gate = 'Ticks',
    [ValidateSet('raw','standard')][string]$Fixture = 'raw'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $repo 'artifacts/ops-wp3'
$null = New-Item -ItemType Directory -Path $folder -Force
$env:HAPPY_PHOTON_PERF = '1'
$env:HAPPY_PHOTON_FULL_CPU = '1'
$env:HAPPY_PHOTON_OPS_FIXTURE = $Fixture
$method = switch ($Gate) {
    'Ticks' { 'PresenceGateTests.G1Ticks' }
    'Contention' { 'PresenceGateTests.G2Contention' }
    'Export' { 'PresenceGateTests.G3Export' }
    'Memory' { 'PresenceGateTests.G4Memory' }
    'Parity' { 'PresenceGateTests.G5Parity' }
    'Proof' { 'PresenceGateTests.G6ProofExport' }
    'Sheets' { 'PresenceGateTests.ProductionReviewSheets' }
}
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$records = [Collections.Generic.List[object]]::new()
$failed = $false
$count = if ($Gate -in @('Ticks', 'Contention')) { 5 } else { 1 }
for ($sample = 1; $sample -le $count; $sample++) {
    $log = Join-Path $folder "$Gate-$Fixture-$stamp-$sample.log"
    & dotnet test (Join-Path $PSScriptRoot 'HappyPhoton.Tests.csproj') -c Release --no-build --filter "FullyQualifiedName~$method" --logger 'console;verbosity=detailed' *> $log
    if ($LASTEXITCODE -ne 0) { $failed = $true }
    foreach ($line in Get-Content -LiteralPath $log) {
        if ($line -match '^\s*OPS_WP3 (\{.*\})\s*$') { $records.Add(($Matches[1] | ConvertFrom-Json)) }
    }
    Write-Output "Evidence: $log"
}
$expectedRecords = switch ($Gate) {
    'Ticks' { 25 }
    'Contention' { 5 }
    'Export' { 5 }
    'Memory' { 1 }
    'Parity' { 8 }
    'Proof' { 2 }
    'Sheets' { 2 }
}
if ($records.Count -ne $expectedRecords) { throw "Missing $Gate evidence: expected $expectedRecords, got $($records.Count)" }
function Median($values) { $sorted = @($values | Sort-Object); $sorted[[int][Math]::Floor($sorted.Count / 2)] }
$summary = @()
if ($Gate -in @('Ticks', 'Contention')) {
    $arms = if ($Gate -eq 'Ticks') { @('TX+', 'TX-', 'CL+', 'CL-', 'both') } else { @('both') }
    foreach ($arm in $arms) {
        $rows = @($records | Where-Object { $_.values.arm -eq $arm })
        if ($rows.Count -ne 5 -or @($rows.pid | Sort-Object -Unique).Count -ne 5) { throw 'Missing five independent process pairs' }
        $control = Median @($rows | ForEach-Object { $_.values.control })
        $active = Median @($rows | ForEach-Object { $_.values.active })
        $increment = if ($Gate -eq 'Ticks') { Median @($rows | ForEach-Object { $_.values.increment }) } else { $active - $control }
        $range = if ($Gate -eq 'Contention') { @(75,125) }
                 elseif ($Fixture -eq 'raw') { @(22,40) } else { @(15,30) }
        $valid = $control -ge $range[0] -and $control -le $range[1]
        $limit = if ($arm -eq 'both') { 40 } else { 25 }
        $pass = $valid -and $(if ($Gate -eq 'Contention') { $active -le 175 } else { $increment -le $limit -and $active -le 150 })
        $summary += @{ arm = $arm; control = $control; active = $active; increment = $increment; valid = $valid; pass = $pass }
        if (!$pass) { $failed = $true }
    }
}
@{ gate = $Gate; fixture = $Fixture; records = @($records.ToArray()); summary = $summary; failed = $failed } |
    ConvertTo-Json -Depth 10 | Set-Content (Join-Path $folder "$Gate-$Fixture-$stamp.json")
$summary | ConvertTo-Json -Depth 5
if ($failed) { throw "$Gate did not qualify; retain the evidence for owner review" }
