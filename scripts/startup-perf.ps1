[CmdletBinding()]
param(
    [int] $Runs = 10,
    [string] $Label = 'shipping',
    [hashtable] $RuntimeEnvironment = @{},
    [switch] $Cold,
    [switch] $NoPublish,
    [string] $Output = 'artifacts/startup-perf'
)

# Launches the Release (win-x64-msix profile) publish against an isolated, seeded
# catalog and records first frame, startup-gate and first Browse population milestones
# from StartupTrace.
# -RuntimeEnvironment runs an A/B without republishing, e.g. @{ DOTNET_ReadyToRun = '0' }.
# -Cold launches each run from a fresh unbuffered (robocopy /J) copy of the publish.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$root = Join-Path $repo $Output
$publish = Join-Path $root 'publish'
$seed = Join-Path $root 'env'
$runDirectory = Join-Path $root ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + "-$Label" + $(if ($Cold) { '-cold' } else { '-warm' }))
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null

if (Get-Process -Name HappyPhoton -ErrorAction SilentlyContinue) {
    throw 'Close Happy Photon first: the single-instance guard would reject every launch.'
}
if (-not $NoPublish) {
    if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
    & dotnet publish (Join-Path $repo 'HappyPhoton.csproj') -p:PublishProfile=win-x64-msix --output $publish *> (Join-Path $runDirectory 'publish.log')
    if ($LASTEXITCODE) { throw 'Publish failed; see publish.log.' }
}
if (-not (Test-Path (Join-Path $seed 'catalog'))) {
    $env:HAPPY_PHOTON_STARTUP_PERF_ROOT = $seed
    try {
        & dotnet test (Join-Path $repo 'Tests/HappyPhoton.Tests.csproj') -c Release --no-build `
            --filter 'FullyQualifiedName=HappyPhoton.Tests.StartupPerfPreparationTests.PrepareIsolatedStartup' *> (Join-Path $runDirectory 'seed.log')
        if ($LASTEXITCODE) { throw 'Seeding failed; see seed.log.' }
    } finally { $env:HAPPY_PHOTON_STARTUP_PERF_ROOT = $null }
}

$variables = @{
    HAPPY_PHOTON_PERF = '1'
    HAPPY_PHOTON_CATALOG_ROOT = (Join-Path $seed 'catalog')
    HAPPY_PHOTON_CACHE_ROOT = (Join-Path $seed 'cache')
} + $RuntimeEnvironment
$saved = @{}
foreach ($name in @($variables.Keys) + 'HAPPY_PHOTON_STARTUP_TRACE') { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }

function Invoke-Launch([string] $Directory, [string] $Trace) {
    $env:HAPPY_PHOTON_STARTUP_TRACE = $Trace
    $process = Start-Process -FilePath (Join-Path $Directory 'HappyPhoton.exe') -PassThru
    $clock = [Diagnostics.Stopwatch]::StartNew()
    try {
        while ($clock.Elapsed.TotalSeconds -lt 60 -and -not $process.HasExited) {
            # The gate can lift before the first folder load lands; wait for both.
            if ((Test-Path $Trace) -and
                (Select-String -Path $Trace -Pattern '^gate-(ready|welcome|error|pointerrecovery),' -Quiet) -and
                (Select-String -Path $Trace -Pattern '^browse-populated,' -Quiet)) { break }
            Start-Sleep -Milliseconds 50
        }
    } finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
    }
    $milestones = [ordered]@{}
    if (Test-Path $Trace) {
        foreach ($line in Get-Content $Trace) { $name, $ms = $line -split ','; $milestones[$name] = [double] $ms }
    }
    return $milestones
}

$results = [Collections.Generic.List[object]]::new()
try {
    foreach ($name in $variables.Keys) { [Environment]::SetEnvironmentVariable($name, $variables[$name]) }
    if (-not $Cold) { Invoke-Launch $publish (Join-Path $runDirectory 'warmup.trace') | Out-Null }
    for ($run = 0; $run -lt $Runs; $run++) {
        $directory = $publish
        if ($Cold) {
            $directory = Join-Path $root "cold-$run"
            & robocopy $publish $directory /E /J /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "Unbuffered copy failed ($LASTEXITCODE)." }
        }
        try {
            $results.Add((Invoke-Launch $directory (Join-Path $runDirectory "run-$run.trace")))
        } finally {
            if ($Cold) { Remove-Item -Recurse -Force $directory }
        }
    }
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}

$summary = [ordered]@{
    label = $Label
    cache = if ($Cold) { 'cold-unbuffered-copy' } else { 'warm' }
    runtimeEnvironment = $RuntimeEnvironment
    runs = $results
    medians = [ordered]@{}
}
foreach ($run in $results) {
    if ($run.Contains('gate-ready') -and $run.Contains('browse-populated')) {
        $run['gate-to-grid'] = $run['browse-populated'] - $run['gate-ready']
    }
}
foreach ($milestone in @('show', 'first-frame', 'gate-ready', 'browse-populated', 'gate-to-grid')) {
    $values = @($results | Where-Object { $_.Contains($milestone) } | ForEach-Object { $_[$milestone] } | Sort-Object)
    $summary.medians[$milestone] = if ($values.Count) { $values[[int][Math]::Floor($values.Count / 2)] } else { $null }
    Write-Host ("{0,-12} median={1} ms  n={2}" -f $milestone, $summary.medians[$milestone], $values.Count)
}
# Progress visibility is optional; only completed gate states end a launch.
$shown = @($results | Where-Object { $_.Contains('gate-shown') }).Count
$summary.gateShownCount = $shown
Write-Host ("gate-shown   launches={0} of {1}" -f $shown, $results.Count)

# A launch that misses a milestone would drop out of its median, so it voids the set.
$required = @('show', 'first-frame', 'gate-ready', 'browse-populated')
$incomplete = @(for ($run = 0; $run -lt $results.Count; $run++) {
    if (@($required | Where-Object { -not $results[$run].Contains($_) }).Count) { $run }
})
$summary.incompleteRuns = $incomplete
$summary | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $runDirectory 'result.json')
Write-Host "Result: $(Join-Path $runDirectory 'result.json')"
if ($incomplete.Count) {
    Write-Host "Incomplete runs (missing a milestone): $($incomplete -join ', ')"
    exit 1
}

exit 0
