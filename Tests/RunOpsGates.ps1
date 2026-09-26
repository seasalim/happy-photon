param(
    [ValidateSet('Qualification','G1Parity','G1SharedAnalysis','HazeConstruction','CostAttribution','G2Export','G3Tick','G4Memory','G5Halo','G6Haze','G7Determinism','ReviewSheets')]
    [string]$Gate = 'Qualification',
    [ValidateSet('raw','standard')][string]$Fixture = 'raw',
    [ValidateSet('Gaussian','Guided')][string]$Clarity = 'Guided',
    [switch]$Refine,
    [ValidateRange(1,1800)][int]$TimeoutSeconds = 600
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$assembly = Get-Item (Join-Path $PSScriptRoot 'bin/Release/net10.0/HappyPhoton.Tests.dll')
if (Get-ChildItem (Join-Path $PSScriptRoot 'Ops*.cs') | Where-Object LastWriteTimeUtc -gt $assembly.LastWriteTimeUtc) {
    throw 'OPS sources are newer than the Release assembly; build before collecting evidence'
}
$env:HAPPY_PHOTON_PERF = '1'
$env:HAPPY_PHOTON_FULL_CPU = '1'
$env:HAPPY_PHOTON_OPS_FIXTURE = $Fixture
$env:HAPPY_PHOTON_OPS_CLARITY = $Clarity
$env:HAPPY_PHOTON_OPS_REFINE = if ($Refine) { '1' } else { '0' }
$folder = Join-Path $repo 'artifacts/ops'
$null = New-Item -ItemType Directory -Path $folder -Force
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff')
$records = [Collections.Generic.List[object]]::new()
$failed = $false
function RunTest([string]$filter, [string]$name) {
    $log = Join-Path $folder "$name-$Fixture-$Clarity-refine-$([bool]$Refine)-$stamp.log"
    $arguments = @('test','Tests/HappyPhoton.Tests.csproj','-c','Release','--no-build','--no-restore',
        '--filter',$filter,'--logger','"console;verbosity=detailed"','--logger',"trx;LogFileName=$name-$stamp.trx",'--results-directory',$folder)
    $process = Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $repo -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    try {
        if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true); $process.WaitForExit(); throw "$name timed out; evidence: $log"
        }
        $lines = Get-Content -LiteralPath $log
        $lines | Where-Object { $_ -match 'OPS |Failed|Passed|Error|Total tests' }
        Get-Content -LiteralPath "$log.err"
        $found = 0
        foreach ($line in $lines) {
            if ($line -match '^\s*OPS (\{.*\})\s*$') { $records.Add(($Matches[1] | ConvertFrom-Json)); $found++ }
        }
        if ($found -eq 0) { throw 'No measurements; skipped tests are not evidence' }
        if ($process.ExitCode -ne 0) { $script:failed = $true }
    }
    finally {
        if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}
# No gate render counts before both the independent oracle tests and all real-base
# operators-off comparisons pass in this invocation of the runner.
RunTest 'FullyQualifiedName~OpsKernelTests|FullyQualifiedName~OpsContractTests|FullyQualifiedName~OpsDehazeRegressionTests|FullyQualifiedName=HappyPhoton.Tests.OpsGateTests.Qualification' 'Qualification'
if ($failed) { throw 'OPS harness is unqualified; do not interpret gate renders' }
$processes = if ($Gate -eq 'G3Tick') { 5 } else { 1 }
if ($Gate -ne 'Qualification') {
    for ($sample = 1; $sample -le $processes; $sample++) {
        RunTest "FullyQualifiedName=HappyPhoton.Tests.OpsGateTests.$Gate" "$Gate-$sample"
    }
}
if ($Gate -in @('G2Export','G3Tick')) {
    RunTest 'FullyQualifiedName=HappyPhoton.Tests.OpsGateTests.CostAttribution' 'CostAttribution'
}
function Median($values) { $sorted = @($values | Sort-Object); return $sorted[[int][Math]::Floor($sorted.Count / 2)] }
$summaries = @()
if ($Gate -eq 'G3Tick') {
    $groups = $records | Where-Object { $_.gate -eq 'G3' } | Group-Object { $_.values.arm }
    if (@($groups).Count -ne 7) { throw 'Missing G3 workload records' }
    foreach ($group in $groups) {
        if ($group.Count -ne 5 -or @($group.Group.pid | Sort-Object -Unique).Count -ne 5) { throw 'G3 requires five fresh processes' }
        $control = Median @($group.Group | ForEach-Object { $_.values.control })
        $increment = Median @($group.Group | ForEach-Object { $_.values.increment })
        $limits = $group.Group[0].values
        $valid = $control -ge $limits.controlMin -and $control -le $limits.controlMax
        $pass = $valid -and $increment -le $limits.limit
        $summaries += @{ arm = $group.Name; control = $control; increment = $increment; limit = $limits.limit; valid = $valid; pass = $pass }
        if (!$pass) { $failed = $true }
    }
}
$sourceHashes = @{}
Get-ChildItem (Join-Path $PSScriptRoot 'Ops*.cs') | ForEach-Object { $sourceHashes[$_.Name] = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
$result = @{ assemblySha256 = (Get-FileHash $assembly.FullName -Algorithm SHA256).Hash; sourceSha256 = $sourceHashes; gate = $Gate; fixture = $Fixture; clarity = $Clarity; refine = [bool]$Refine; processes = $processes;
    records = @($records.ToArray()); summary = $summaries; failed = $failed }
$result | ConvertTo-Json -Depth 15 | Set-Content (Join-Path $folder "$Gate-$Fixture-$Clarity-refine-$([bool]$Refine)-$stamp.json")
Write-Output "OPS_SUMMARY $($summaries | ConvertTo-Json -Compress)"
if ($failed) { throw "$Gate did not qualify; all observations retained in $folder" }




