# Caller holds measure for qualifying G1/G2, or heavy for deterministic gates and sheets.
param(
    [ValidateSet('G1Tick','G2Export','G3Parity','G4Clipping','ReviewSheets','FollowOnSheets')]
    [string]$Gate = 'G3Parity',
    [ValidateSet('raw','standard')][string]$Fixture = 'raw',
    [string]$Candidate = '',
    [ValidateRange(1,3600)][int]$TimeoutSeconds = 1800
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$assembly = Get-Item (Join-Path $PSScriptRoot 'bin/Release/net10.0/HappyPhoton.Tests.dll')
if (Get-ChildItem (Join-Path $PSScriptRoot 'Finishing*.cs') | Where-Object LastWriteTimeUtc -gt $assembly.LastWriteTimeUtc) {
    throw 'Finishing sources are newer than the Release assembly; build first'
}
$requested = @($Candidate -split ',' | Where-Object { $_ } | ForEach-Object { $_.Trim() })
$candidateFiles = @(Get-ChildItem (Join-Path $repo 'Assets/Looks/*.preset.json')) +
    @(Get-ChildItem (Join-Path $PSScriptRoot 'assets/finishing-looks/*.preset.json'))
$candidates = @($candidateFiles |
    ForEach-Object { Get-Content -Raw $_.FullName | ConvertFrom-Json } |
    Where-Object { !$Candidate -or $_.id -in $requested })
if ($candidates.Count -eq 0 -or ($Candidate -and $candidates.Count -ne $requested.Count)) { throw 'Unknown or duplicate candidate' }
$env:HAPPY_PHOTON_FINISHING_GATES = '1'
$env:HAPPY_PHOTON_PERF = '1'
$env:HAPPY_PHOTON_FULL_CPU = '1'
$env:HAPPY_PHOTON_OPS_FIXTURE = $Fixture
$env:HAPPY_PHOTON_FINISHING_CANDIDATE = $Candidate
$folder = Join-Path $repo 'artifacts/finishing'
$null = New-Item -ItemType Directory -Path $folder -Force
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff')
$records = [Collections.Generic.List[object]]::new()
$failed = $false
$processes = if ($Gate -eq 'G1Tick') { 5 } else { 1 }

for ($sample = 1; $sample -le $processes; $sample++) {
    $log = Join-Path $folder "$Gate-$Fixture-$stamp-$sample.log"
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.WorkingDirectory = $repo
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $filter = "FullyQualifiedName=HappyPhoton.Tests.FinishingGateTests.$Gate"
    foreach ($argument in @('test','Tests/HappyPhoton.Tests.csproj','-c','Release','--no-build',
        '--no-restore','--filter',$filter,'--logger','console;verbosity=detailed')) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    try {
        if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            $failed = $true
        }
        $lines = $stdout.GetAwaiter().GetResult()
        $lines | Set-Content -LiteralPath $log
        $stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath "$log.err"
        $lines -split "`n" | Where-Object { $_ -match 'FINISHING |Failed|Passed|Error|Total tests' }
        foreach ($line in ($lines -split "`n")) {
            if ($line -match '^\s*FINISHING (\{.*\})\s*$') {
                $records.Add(($Matches[1] | ConvertFrom-Json))
            }
        }
        if ($process.ExitCode -ne 0) { $failed = $true }
    }
    finally {
        if (!$process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $process.Dispose()
    }
}

. (Join-Path $PSScriptRoot "FinishingReceipts.ps1")

if ($Gate -in @('ReviewSheets','FollowOnSheets')) {
    if ($records.Count -eq 0 -or $failed) { throw "$Gate did not complete; see $folder" }
    return
}
$gateId = $Gate.Substring(0, 2)
$fixtureId = if ($Gate -eq 'G4Clipping') { 'all' } else { $Fixture }
$receipts = Join-Path $folder 'receipts'
$null = New-Item -ItemType Directory -Path $receipts -Force
foreach ($candidateEntry in $candidates) {
    $candidatePath = ($candidateFiles | Where-Object Name -eq "$($candidateEntry.id).preset.json").FullName
    $receipt = New-FinishingReceipt -Records $records.ToArray() -GateId $gateId -FixtureId $fixtureId `
        -CandidateId $candidateEntry.id -CandidatePath $candidatePath -AssemblyPath $assembly.FullName `
        -ProductionPath (Join-Path $PSScriptRoot 'bin/Release/net10.0/HappyPhoton.dll') -LogStamp $stamp
    $json = $receipt | ConvertTo-Json -Depth 20
    $json | Set-Content (Join-Path $receipts "$gateId-$fixtureId-$($candidateEntry.id).json")
    $json | Set-Content (Join-Path $folder "$gateId-$fixtureId-$($candidateEntry.id)-$stamp.json")
    Write-Output "FINISHING_SUMMARY $($receipt | ConvertTo-Json -Depth 20 -Compress)"
    if (!$receipt.pass) { $failed = $true }
}
if ($failed) { throw "$Gate did not qualify all candidates; observations retained in $folder" }
