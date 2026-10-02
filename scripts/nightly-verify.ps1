param(
    [string] $Configuration = "Release",
    # Where the result summary goes; the lanes read latest.json.
    [string] $OutputDirectory = ""
)

# The one full verify of the day. Runs on the primary checkout's main, under the
# exclusive `measure` host lease so no lane build, suite or measurement overlaps
# it, and leaves a machine-readable verdict for the lanes: a red nightly blocks
# merges until it is fixed or the owner waives it (AGENTS.md, Verify).

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $repoRoot "artifacts/nightly" }
$null = New-Item -ItemType Directory -Path $outDir -Force
$startedAt = [DateTime]::UtcNow
$commit = (& git -C $repoRoot rev-parse --short HEAD).Trim()
$branch = (& git -C $repoRoot rev-parse --abbrev-ref HEAD).Trim()
$resultsDir = Join-Path $outDir ("run-" + $startedAt.ToString("yyyyMMdd-HHmmss"))
$log = Join-Path $resultsDir "verify.log"
$null = New-Item -ItemType Directory -Path $resultsDir -Force

$hostlock = Join-Path $env:USERPROFILE ".claude/skills/feature/scripts/hostlock.py"
$verify = Join-Path $PSScriptRoot "verify.ps1"
$inner = "pwsh -NoProfile -File `"$verify`" -Configuration $Configuration -ResultsDirectory `"$resultsDir`" -LogFilePrefix nightly"

Push-Location $repoRoot
try {
    if (Test-Path $hostlock) {
        & python $hostlock run --mode measure --owner "happy-photon/nightly-verify" -- pwsh -NoProfile -Command $inner *>&1 | Tee-Object -FilePath $log
    } else {
        & pwsh -NoProfile -Command $inner *>&1 | Tee-Object -FilePath $log
    }
    $exit = $LASTEXITCODE
} finally {
    Pop-Location
}

$failed = @()
Get-ChildItem -Path $resultsDir -Recurse -Filter *.trx -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        [xml] $trx = Get-Content $_.FullName
        $trx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq "Failed" } | ForEach-Object { $failed += $_.testName }
    } catch { }
}

$summary = [ordered]@{
    startedAt = $startedAt.ToString("o")
    finishedAt = [DateTime]::UtcNow.ToString("o")
    branch = $branch
    commit = $commit
    result = if ($exit -eq 0) { "pass" } else { "fail" }
    exitCode = $exit
    failedTests = @($failed | Sort-Object -Unique)
    resultsDirectory = $resultsDir
    log = $log
}
$json = $summary | ConvertTo-Json -Depth 4
Set-Content -Path (Join-Path $outDir "latest.json") -Value $json -Encoding utf8
Add-Content -Path (Join-Path $outDir "history.jsonl") -Value ($summary | ConvertTo-Json -Depth 4 -Compress) -Encoding utf8
Write-Host "Nightly verify: $($summary.result) on $branch $commit; $($summary.failedTests.Count) failed test(s); summary in $outDir\latest.json"
exit $exit
