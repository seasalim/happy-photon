param(
    [ValidateSet('G1Candidates','G1PerSpotDiagnostic','G2ExportParity','Registration','G3Tick','G4Contention','G5G6Refinement','G7G8Memory','Record40Mp','RepairCostDiagnostic','Wp3ExportDelta')]
    [string]$Gate = 'G3Tick',
    [ValidateSet('raw','standard')][string]$Fixture = 'raw',
    [ValidateSet('Membrane')][string]$Formulation = 'Membrane',
    [ValidateSet('Additive')][string]$Domain = 'Additive',
    [ValidateRange(0,3)][double]$AreaLimit = 0,
    [ValidateRange(5,64)][int[]]$AreaDiscCounts = @(),
    [ValidateSet("SCap6", "SArea64")][string]$Workload = "SCap6",
    [switch]$Diagnostic,
    [ValidateRange(1,600)][int]$TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
# Each count keeps the existing five-process G4 protocol. Failures are retained
# and the sweep continues; qualification and the largest passing limit are owned
# by the reviewer, not selected automatically from a sparse sweep.
if ($AreaDiscCounts.Count -gt 0) {
    if ($Gate -ne 'G4Contention' -or $AreaLimit -ne 0) {
        throw 'AreaDiscCounts requires G4Contention and cannot be combined with AreaLimit'
    }
    $sweep = foreach ($count in ($AreaDiscCounts | Sort-Object -Unique)) {
        $area = $count * ([Math]::PI * 0.10 * 0.10)
        $passed = $true
        try {
            & $PSCommandPath -Gate $Gate -Fixture $Fixture -Formulation $Formulation -Domain $Domain `
                -AreaLimit $area -Diagnostic:$Diagnostic -TimeoutSeconds $TimeoutSeconds | Out-Host
        }
        catch { $passed = $false; Write-Warning $_ }
        [pscustomobject]@{ discs = $count; area = $area; passed = $passed }
    }
    $sweep | ConvertTo-Json | Set-Content (Join-Path $repo "artifacts/heal/area-sweep-$Fixture-$Formulation-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff')).json")
    $sweep | Format-Table
    return
}
$env:HAPPY_PHOTON_PERF = '1'
$env:HAPPY_PHOTON_FULL_CPU = '1'
$env:HAPPY_PHOTON_HEAL_FIXTURE = $Fixture
$env:HAPPY_PHOTON_HEAL_WORKLOAD = $Workload
$env:HAPPY_PHOTON_HEAL_FORMULATION = $Formulation
$env:HAPPY_PHOTON_HEAL_DOMAIN = $Domain
$env:HAPPY_PHOTON_HEAL_AREA_LIMIT = if ($AreaLimit -gt 0) { $AreaLimit.ToString('R', [Globalization.CultureInfo]::InvariantCulture) } else { $null }
if ($Gate -in @('G2ExportParity','G5G6Refinement','G7G8Memory','Record40Mp') -and $Fixture -ne 'raw') {
    throw 'This gate requires the RAW fixture (Record40Mp chooses its own source)'
}
$processes = if ($Gate -in @('G3Tick','G4Contention','G5G6Refinement')) { 5 } elseif ($Gate -eq 'Record40Mp') { 2 } else { 1 }
$folder = Join-Path $repo 'artifacts/heal'
$null = New-Item -ItemType Directory -Path $folder -Force
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff')
$stamp += '-' + $Workload + '-area-' + $AreaLimit.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
$records = [Collections.Generic.List[object]]::new()
$failed = $false
for ($sample = 1; $sample -le $processes; $sample++) {
    $env:HAPPY_PHOTON_HEAL_40MP_ARM = if ($sample -eq 2) { 'memory' } else { 'latency' }
    $log = Join-Path $folder "$Gate-$Fixture-$Formulation-$Domain-$stamp-$sample.log"
    $err = "$log.err"
    $arguments = @('test','Tests/HappyPhoton.Tests.csproj','-c','Release','--no-build','--no-restore',
        '--filter',"FullyQualifiedName=HappyPhoton.Tests.HealGateTests.$Gate",'--logger','"console;verbosity=detailed"')
    $process = Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $repo -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $log -RedirectStandardError $err
    try {
        if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true); $process.WaitForExit()
            throw "$Gate timed out; evidence: $log"
        }
        $lines = Get-Content -LiteralPath $log
        $lines
        Get-Content -LiteralPath $err
        $found = 0
        foreach ($line in $lines) {
            if ($line -match '^\s*HEAL (\{.*\})\s*$') {
                $records.Add(($Matches[1] | ConvertFrom-Json)); $found++
            }
        }
        if ($found -eq 0) { throw 'No measurements; skipped tests are not evidence' }
        if ($process.ExitCode -ne 0) { $failed = $true }
    }
    finally {
        if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}
function Median($values) { $sorted = @($values | Sort-Object); return $sorted[[int][Math]::Floor($sorted.Count / 2)] }
$gateRecords = @($records | Where-Object { $_.gate -eq ($Gate -replace 'Tick|Contention|Refinement|Memory','') })
$summary = $null
if ($Gate -in @('G3Tick','G4Contention')) {
    $control = Median @($gateRecords | ForEach-Object { $_.values.control })
    $tick = Median @($gateRecords | ForEach-Object { $_.values.tick })
    $limits = $gateRecords[0].values
    $valid = $control -ge $limits.controlMin -and $control -le $limits.controlMax
    $pass = $valid -and $tick -le $limits.tickLimit
    $increment = $null
    if ($Gate -eq 'G3Tick') {
        $increment = Median @($gateRecords | ForEach-Object { $_.values.increment })
        $pass = $pass -and $increment -le 40
    }
    $summary = @{ control = $control; tick = $tick; increment = $increment; valid = $valid; pass = $pass; processes = $processes }
    if (!$pass) { $failed = $true }
}
if ($Gate -eq 'G5G6Refinement') {
    $control = Median @($gateRecords | ForEach-Object { $_.values.control })
    $warm = Median @($gateRecords | ForEach-Object { $_.values.warmMedian })
    $cold = Median @($gateRecords | ForEach-Object { $_.values.coldMedian })
    $valid = $control -ge 1.8 -and $control -le 3.6
    $summary = @{ control = $control; warm = $warm; cold = $cold; coldLimit = $control * 1.10;
        valid = $valid; G5 = ($valid -and $warm -le 1.5); G6 = ($valid -and $cold -le $control * 1.10) }
    if (!$summary.G5 -or !$summary.G6) { $failed = $true }
}
if ($Gate -eq 'G7G8Memory') {
    $v = $gateRecords[0].values
    $valid = $v.fitBytes -ge 150000000 -and $v.fitBytes -le 900000000
    $g7 = $valid -and $v.installedDelta -le $v.installedLimit
    $summary = @{ valid = $valid; G7 = $g7; G8 = ($g7 -and $v.peakDelta -le $v.peakLimit) }
    if (!$summary.G7 -or !$summary.G8) { $failed = $true }
}
$result = @{ gate = $Gate; fixture = $Fixture; formulation = $Formulation; domain = $Domain;
    diagnostic = ([bool]$Diagnostic -or $Gate -in @('RepairCostDiagnostic','G1PerSpotDiagnostic')); areaLimit = $AreaLimit; workload = $Workload; records = @($records.ToArray()); summary = $summary; failed = $failed }
$json = $result | ConvertTo-Json -Depth 15
$json | Set-Content (Join-Path $folder "$Gate-$Fixture-$Formulation-$Domain-$stamp.json")
Write-Output "HEAL_SUMMARY $($summary | ConvertTo-Json -Compress)"
if ($failed) { throw "$Gate did not qualify; all observations retained in $folder" }
