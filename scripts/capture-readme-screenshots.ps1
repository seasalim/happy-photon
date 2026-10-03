[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$previousOptIn = $env:HAPPY_PHOTON_README_SHOTS

Push-Location $repositoryRoot
try {
    & dotnet build HappyPhoton.sln -c Release
    if ($LASTEXITCODE -ne 0) {
        throw 'Screenshot build failed.'
    }

    # The Demo folder adds four CC0 compatibility fixtures; fetch verifies or downloads them.
    & dotnet run --file scripts/fetch-compatibility-fixtures.cs -- `
        canon-r5m2-raw-apsc fuji-xt50-compressed panasonic-s9-standard sony-a9m3-lossy
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not fetch the README demo fixtures.'
    }

    $env:HAPPY_PHOTON_README_SHOTS = '1'
    & dotnet test HeadlessTests/HappyPhoton.Headless.Tests.csproj -c Release --no-build --no-restore `
        --filter 'FullyQualifiedName~ReadmeScreenshotTests' --blame-hang-timeout 90s
    if ($LASTEXITCODE -ne 0) {
        throw 'README screenshot capture failed.'
    }

    $shots = [ordered]@{
        'readme-browse' = 'Screenshot_Browse.png'
        'readme-develop' = 'Screenshot_Develop.png'
        'readme-develop-midgray-assess' = 'Screenshot_Develop_MidGray_Assess.png'
    }

    foreach ($shot in $shots.GetEnumerator()) {
        Copy-Item -LiteralPath "artifacts/shots/$($shot.Key).png" `
            -Destination "docs/screenshots/$($shot.Value)"
    }
}
finally {
    $env:HAPPY_PHOTON_README_SHOTS = $previousOptIn
    Pop-Location
}
