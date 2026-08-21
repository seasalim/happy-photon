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
$candidate = Join-Path $output "candidate-performance.json"
$oldPerf, $oldIsolated = $env:HAPPY_PHOTON_NATIVE_PERF, $env:HAPPY_PHOTON_NATIVE_PERF_ISOLATED
$oldOutput, $oldRuntime = $env:HAPPY_PHOTON_NATIVE_PERF_OUTPUT, $env:HAPPY_PHOTON_LIBRAW_BRIDGE_DIR
try {
    $env:HAPPY_PHOTON_NATIVE_PERF = "1"
    $env:HAPPY_PHOTON_NATIVE_PERF_ISOLATED = "1"
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
$candidateData = Get-Content -Raw -LiteralPath $candidate | ConvertFrom-Json
$rid = $candidateData.Rid
$extension = if ($rid -eq "win-x64") { ".exe" } else { "" }
$tools = Join-Path (Split-Path $runtime -Parent) "validation/native-performance"
$candidateProbe = Join-Path $tools "hplr_candidate_performance$extension"
if (-not (Test-Path -LiteralPath $candidateProbe)) {
    throw "Native performance input is missing: $candidateProbe. Rebuild the RID candidate."
}
& $python (Join-Path $PSScriptRoot "run_native_performance.py") `
    --rid $rid --fixture (Join-Path $repoRoot "Tests/assets/canon-eos-350d.cr2") `
    --candidate-runtime $runtime --candidate-probe $candidateProbe `
    --candidate-report $candidate
if ($LASTEXITCODE -ne 0) { throw "Native peak-memory harness failed." }
