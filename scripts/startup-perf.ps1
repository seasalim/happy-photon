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
# catalog and records first frame and startup-gate milestones from StartupTrace.
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
            if ((Test-Path $Trace) -and (Select-String -Path $Trace -Pattern '^gate-' -Quiet)) { break }
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
foreach ($milestone in @('show', 'first-frame', 'gate-ready')) {
    $values = @($results | Where-Object { $_.Contains($milestone) } | ForEach-Object { $_[$milestone] } | Sort-Object)
    $summary.medians[$milestone] = if ($values.Count) { $values[[int][Math]::Floor($values.Count / 2)] } else { $null }
    Write-Host ("{0,-12} median={1} ms  n={2}" -f $milestone, $summary.medians[$milestone], $values.Count)
}
$summary | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $runDirectory 'result.json')
Write-Host "Result: $(Join-Path $runDirectory 'result.json')"
exit 0
