[CmdletBinding()]
param(
    [string] $Baseline,
    [string] $Case,
    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CullPerf.psm1') -Force
$repo = Split-Path -Parent $PSScriptRoot
$gatePath = Join-Path $repo 'Tests/CullPerfGates.json'
$gate = Get-Content -LiteralPath $gatePath -Raw | ConvertFrom-Json
$sha = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE) { throw 'Cannot identify candidate commit.' }
$run = New-CullPerfAttempt (Join-Path $repo 'artifacts/cull-perf') $sha
Copy-Item -LiteralPath $gatePath -Destination (Join-Path $run "gates.json")
$clock = [Diagnostics.Stopwatch]::StartNew()
$checks = [Collections.Generic.List[object]]::new()
$saved = @{}
$variables = @('CULL_PERF_RUN', 'CULL_PERF_CASE', 'CULL_PERF_MACHINE', 'CULL_PERF_BASELINE', 'HAPPY_PHOTON_FULL_CPU', 'HAPPY_PHOTON_PERF')
foreach ($name in $variables) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
$result = [ordered]@{
    schemaVersion = 1
    gateId = $gate.id
    gateHash = (Get-FileHash -LiteralPath $gatePath -Algorithm SHA256).Hash.ToLowerInvariant()
    candidateCommit = $sha
    buildVerified = $false
    candidateDirty = [bool](& git -C $repo status --porcelain)
    stopwatchFrequency = [Diagnostics.Stopwatch]::Frequency
    processEnvironment = @{ DOTNET_PROCESSOR_COUNT = $env:DOTNET_PROCESSOR_COUNT; OMP_NUM_THREADS = $env:OMP_NUM_THREADS; OMP_THREAD_LIMIT = $env:OMP_THREAD_LIMIT }
    baselineCommit = $null
    baselineResult = $null
    machine = [ordered]@{
        name = [Environment]::MachineName
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        processor = $env:PROCESSOR_IDENTIFIER
        logicalProcessors = [Environment]::ProcessorCount
    }
    configuration = 'Release'
    runtime = @(& dotnet --info)
    osCacheWarmth = 'uncontrolled'
    startedUtc = [DateTime]::UtcNow.ToString('o')
    coverageGaps = @($gate.knownMissingEvidence)
    fragments = @()
    gates = @()
    checks = @()
    verdict = 'inconclusive'
    exitCode = 2
    elapsedSeconds = 0
    error = $null
}

function Invoke-Case([string] $Name, [string] $Project, [string] $Filter, [int] $Expected = 1) {
    $directory = Join-Path $run ('trx-' + $Name)
    New-Item -ItemType Directory -Path $directory | Out-Null
    $hangTimeout = if ($Name -eq "recording-overhead") { "5m" } else { "90s" }
    & dotnet test (Join-Path $repo $Project) -c Release --no-build --no-restore `
        --settings (Join-Path $repo 'HappyPhoton.FullCpu.runsettings') --filter $Filter `
        --logger 'trx;LogFileName=case.trx' --results-directory $directory `
        --blame-hang --blame-hang-timeout $hangTimeout *> (Join-Path $directory 'process.log')
    $status = Get-CullPerfTestEvidence $directory $LASTEXITCODE $Expected
    $checks.Add([ordered]@{ id = $Name; verdict = $status; filter = $Filter; expected = $Expected })
    Write-Host "$Name`: $status"
}

try {
    if ($Baseline) {
        $resolved = (Resolve-Path -LiteralPath $Baseline).Path
        $before = Get-Content -LiteralPath $resolved -Raw | ConvertFrom-Json
        $result.baselineCommit = $before.candidateCommit
        $result.baselineResult = [ordered]@{ path = $resolved; sha256 = (Get-FileHash -LiteralPath $resolved).Hash.ToLowerInvariant() }
        $env:CULL_PERF_BASELINE = $resolved
    } else { $env:CULL_PERF_BASELINE = $null }
    $env:CULL_PERF_RUN = $run
    $identity = $result.machine | ConvertTo-Json -Compress
    $env:CULL_PERF_MACHINE = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($identity))).ToLowerInvariant()
    $env:HAPPY_PHOTON_FULL_CPU = '1'
    $env:HAPPY_PHOTON_PERF = '1'
    if (-not $NoBuild) {
        & dotnet build (Join-Path $repo 'HappyPhoton.sln') -c Release *> (Join-Path $run 'build.log')
        if ($LASTEXITCODE) { throw 'Release build failed; see build.log.' }
        $result.buildVerified = $true
    }
    $checks.Add((Get-CullPerfBuildEvidence -Verified $result.buildVerified))
    Invoke-Case 'prepare' 'Tests/HappyPhoton.Tests.csproj' 'FullyQualifiedName=HappyPhoton.Tests.CullPerfWorkloadTests.PrepareFixtures'
    if ($checks[-1].verdict -ne 'pass') { throw 'Required fixture preparation failed.' }
    foreach ($workload in $gate.workloads) {
        if ($Case -and $workload.id -ne $Case) { continue }
        $env:CULL_PERF_CASE = $workload.id
        if ($workload.surface -eq 'window') {
            Invoke-Case $workload.id 'HeadlessTests/HappyPhoton.Headless.Tests.csproj' 'FullyQualifiedName=HappyPhoton.Tests.CullPerfWindowTests.QualifiedLoupeFrames'
        } elseif ($workload.surface -eq 'recorder') {
            Invoke-Case $workload.id 'Tests/HappyPhoton.Tests.csproj' 'FullyQualifiedName=HappyPhoton.Tests.CullPerfOverheadTests.QualifiedRecordingOverhead'
        } elseif ($workload.surface -eq 'migrated') {
            # The approved legacy measurement is one qualified case with both fixture groups.
            if ($workload.id -eq 'migrated-jpeg' -or $Case -eq 'migrated-raw') {
                Invoke-Case $workload.id 'Tests/HappyPhoton.Tests.csproj' 'FullyQualifiedName=HappyPhoton.Tests.AdjacentPreviewPerformanceTests.AdjacentSelectionGates_WhenEnabled'
            }
        } else {
            Invoke-Case $workload.id 'Tests/HappyPhoton.Tests.csproj' 'FullyQualifiedName=HappyPhoton.Tests.CullPerfWorkloadTests.QualifiedWorkload'
        }
    }
    foreach ($check in $gate.requiredChecks) {
        if ($Case) { $checks.Add(@{ id = $check.id; verdict = 'inconclusive'; reason = 'Diagnostic subset omitted required check.' }); continue }
        Invoke-Case $check.id $check.project $check.filter $check.expected
    }
    Invoke-Case 'evaluate' 'Tests/HappyPhoton.Tests.csproj' 'FullyQualifiedName=HappyPhoton.Tests.CullPerfWorkloadTests.EvaluateEvidence'
    $evidence = Get-Content -LiteralPath (Join-Path $run 'evaluation.json') -Raw | ConvertFrom-Json
    $result.fragments = $evidence.fragments
    $result.gates = $evidence.evaluation.gates
    $result.verdict = Get-CullPerfVerdict @($evidence.evaluation.verdict; $checks | ForEach-Object { $_.verdict })
    $result.exitCode = switch ($result.verdict) { 'pass' { 0 } 'fail' { 1 } default { 2 } }
} catch {
    $result.error = $_.Exception.ToString()
} finally {
    $result.checks = $checks.ToArray()
    $result.elapsedSeconds = $clock.Elapsed.TotalSeconds
    $path = Join-Path $run 'result.json'
    $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew)
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes(($result | ConvertTo-Json -Depth 100))
        $stream.Write($bytes, 0, $bytes.Length)
    } finally { $stream.Dispose() }
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Write-Host "Result: $path ($($result.verdict))"
}
exit $result.exitCode
