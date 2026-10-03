[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'icon-rasters.ps1')

$outputDirectory = Join-Path $repositoryRoot 'packaging\windows\Assets'
$listingOutputDirectory = Join-Path `
    $repositoryRoot `
    'packaging\windows\StoreListing'

Import-MagickNet

[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
[IO.Directory]::CreateDirectory($listingOutputDirectory) | Out-Null

$assets = [ordered]@{
    'StoreLogo.png' = 50
    'StoreLogo.scale-100.png' = 50
    'StoreLogo.scale-125.png' = 63
    'StoreLogo.scale-150.png' = 75
    'StoreLogo.scale-200.png' = 100
    'StoreLogo.scale-400.png' = 200
    'Square44x44Logo.png' = 44
    'Square44x44Logo.scale-100.png' = 44
    'Square44x44Logo.scale-125.png' = 55
    'Square44x44Logo.scale-150.png' = 66
    'Square44x44Logo.scale-200.png' = 88
    'Square44x44Logo.scale-400.png' = 176
    'Square44x44Logo.targetsize-16.png' = 16
    'Square44x44Logo.targetsize-24.png' = 24
    'Square44x44Logo.targetsize-32.png' = 32
    'Square44x44Logo.targetsize-48.png' = 48
    'Square44x44Logo.targetsize-256.png' = 256
    'Square150x150Logo.png' = 150
    'Square150x150Logo.scale-100.png' = 150
    'Square150x150Logo.scale-125.png' = 188
    'Square150x150Logo.scale-150.png' = 225
    'Square150x150Logo.scale-200.png' = 300
    'Square150x150Logo.scale-400.png' = 600
}

foreach ($asset in $assets.GetEnumerator()) {
    Write-TransparentIcon `
        -Destination (Join-Path $outputDirectory $asset.Key) `
        -Size $asset.Value
}

$listingIconPath = Join-Path $listingOutputDirectory 'AppTileIcon.png'
Write-TransparentIcon -Destination $listingIconPath -Size 300

Write-Output "Generated $($assets.Count) MSIX assets in $outputDirectory"
Write-Output "Generated Store listing icon at $listingIconPath"
