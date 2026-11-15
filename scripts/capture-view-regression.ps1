[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $OutDir,
    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$previousOptIn = $env:HAPPY_PHOTON_VIEW_CAPTURES
$scenes = @(
    'view-browse',
    'view-develop',
    'view-develop-locals',
    'view-develop-crop',
    'view-export',
    '29-confirmation-dark-wp9',
    '29-confirmation-midgray-wp9',
    'wp9-text-input',
    'wp9-text-input-midgray'
)

Push-Location $repositoryRoot
try {
    if (-not $NoBuild) {
        & dotnet build HappyPhoton.sln -c Release
        if ($LASTEXITCODE -ne 0) {
            throw 'Capture build failed.'
        }
    }

    foreach ($scene in $scenes) {
        Remove-Item -LiteralPath "artifacts/shots/$scene.png" -ErrorAction SilentlyContinue
    }

    $env:HAPPY_PHOTON_VIEW_CAPTURES = '1'
    $filter = 'FullyQualifiedName~ViewRegressionCaptureTests|' +
        'FullyQualifiedName~DialogChromeShowcaseTests.DestructiveConfirmation|' +
        'FullyQualifiedName~DialogChromeShowcaseTests.TextInput'
    & dotnet test HeadlessTests/HappyPhoton.Headless.Tests.csproj -c Release --no-build --no-restore `
        --filter $filter --blame-hang-timeout 120s
    if ($LASTEXITCODE -ne 0) {
        throw 'View capture failed.'
    }

    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $missing = @()
    foreach ($scene in $scenes) {
        $shot = "artifacts/shots/$scene.png"
        if (-not (Test-Path -LiteralPath $shot)) {
            $missing += $scene
            continue
        }

        Copy-Item -LiteralPath $shot -Destination (Join-Path $OutDir "$scene.png") -Force
    }

    if ($missing.Count -gt 0) {
        throw "Captures missing (skipped or failed): $($missing -join ', ')"
    }

    Write-Output "CAPTURED $($scenes.Count) scenes to $OutDir"
}
finally {
    $env:HAPPY_PHOTON_VIEW_CAPTURES = $previousOptIn
    Pop-Location
}
