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

    $env:HAPPY_PHOTON_README_SHOTS = '1'
    & dotnet test HeadlessTests/HappyPhoton.Headless.Tests.csproj -c Release --no-build --no-restore `
        --filter 'FullyQualifiedName~ReadmeScreenshotTests' --blame-hang-timeout 90s
    if ($LASTEXITCODE -ne 0) {
        throw 'README screenshot capture failed.'
    }

    $shots = [ordered]@{
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
