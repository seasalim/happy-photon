param(
    [Parameter(Mandatory)] [string] $RuntimeDirectory,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [string] $Configuration = "Release")

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$runtime = (Resolve-Path $RuntimeDirectory).Path
$output = if ([IO.Path]::IsPathFullyQualified($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$baseline = Join-Path $output "baseline-performance.json"
$candidate = Join-Path $output "candidate-performance.json"
$oldPerf, $oldIsolated = $env:HAPPY_PHOTON_NATIVE_PERF, $env:HAPPY_PHOTON_NATIVE_PERF_ISOLATED
$oldOutput, $oldRuntime = $env:HAPPY_PHOTON_NATIVE_PERF_OUTPUT, $env:HAPPY_PHOTON_LIBRAW_BRIDGE_DIR
try {
    $env:HAPPY_PHOTON_NATIVE_PERF = "1"
    $env:HAPPY_PHOTON_NATIVE_PERF_ISOLATED = "1"
    $env:HAPPY_PHOTON_NATIVE_PERF_OUTPUT = $baseline
    dotnet test (Join-Path $repoRoot "Tests/HappyPhoton.Tests.csproj") `
        --configuration $Configuration `
        -p:UsedAvaloniaProducts= `
        --filter "FullyQualifiedName=HappyPhoton.Tests.LibRawNativePerformanceBaselineTests.CurrentRid_WritesPairedHarnessMeasurements"
    if ($LASTEXITCODE -ne 0) { throw "Baseline performance harness failed." }
    $env:HAPPY_PHOTON_LIBRAW_BRIDGE_DIR = $runtime
    $env:HAPPY_PHOTON_NATIVE_PERF_OUTPUT = $candidate
    dotnet test (Join-Path $repoRoot "Interop/HappyPhoton.LibRaw.Interop.Tests/HappyPhoton.LibRaw.Interop.Tests.csproj") `
        --configuration $Configuration `
        -p:UsedAvaloniaProducts= `
        --filter "FullyQualifiedName=HappyPhoton.LibRaw.Interop.Tests.LibRawNativePerformanceCandidateTests.CurrentRid_WritesPairedHarnessMeasurements"
    if ($LASTEXITCODE -ne 0) { throw "Candidate performance harness failed." }
} finally {
    $env:HAPPY_PHOTON_NATIVE_PERF, $env:HAPPY_PHOTON_NATIVE_PERF_ISOLATED = $oldPerf, $oldIsolated
    $env:HAPPY_PHOTON_NATIVE_PERF_OUTPUT, $env:HAPPY_PHOTON_LIBRAW_BRIDGE_DIR = $oldOutput, $oldRuntime
}
$python = if ($IsWindows) { "python" } elseif (Get-Command python3 -ErrorAction SilentlyContinue) {
    "python3"
} else { "python" }
$baselineData = Get-Content -Raw -LiteralPath $baseline | ConvertFrom-Json
$rid = $baselineData.Rid
$baselineRuntime = if ($rid -eq "osx-arm64") {
    Join-Path $repoRoot "runtimes/osx-arm64/native"
} else {
    Join-Path $repoRoot "Tests/bin/$Configuration/net10.0/runtimes/$rid/native"
}
$extension = if ($rid -eq "win-x64") { ".exe" } else { "" }
$tools = Join-Path (Split-Path $runtime -Parent) "validation/native-performance"
$baselineProbe = Join-Path $tools "hplr_baseline_performance$extension"
$candidateProbe = Join-Path $tools "hplr_candidate_performance$extension"
foreach ($path in @($baselineRuntime, $baselineProbe, $candidateProbe)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Native performance input is missing: $path. Rebuild the RID candidate."
    }
}
& $python (Join-Path $PSScriptRoot "run_native_performance.py") `
    --rid $rid --fixture (Join-Path $repoRoot "Tests/assets/canon-eos-350d.cr2") `
    --baseline-runtime $baselineRuntime --candidate-runtime $runtime `
    --baseline-probe $baselineProbe --candidate-probe $candidateProbe `
    --baseline-report $baseline --candidate-report $candidate
if ($LASTEXITCODE -ne 0) { throw "Native peak-memory harness failed." }
& $python (Join-Path $PSScriptRoot "compare_performance.py") `
    --baseline $baseline --candidate $candidate `
    --output (Join-Path $output "performance-comparison.json")
if ($LASTEXITCODE -ne 0) { throw "Native performance comparison requires investigation." }
