[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'HappyPhoton.csproj'
$sourcePath = Join-Path $repositoryRoot 'Assets\happy-photon-icon.svg'
$outputDirectory = Join-Path $repositoryRoot 'packaging\windows\Assets'
$listingOutputDirectory = Join-Path `
    $repositoryRoot `
    'packaging\windows\StoreListing'

[xml]$project = Get-Content -Raw -LiteralPath $projectPath
$magickReference = $project.SelectSingleNode(
    "//PackageReference[@Include='Magick.NET-Q16-AnyCPU']")
if (-not $magickReference) {
    throw 'HappyPhoton.csproj does not reference Magick.NET-Q16-AnyCPU.'
}

$magickVersion = [string]$magickReference.Version
$globalPackagesLine = & dotnet nuget locals global-packages --list
if ($LASTEXITCODE -ne 0) {
    throw 'Could not resolve the NuGet global-packages directory.'
}

$globalPackages = ($globalPackagesLine -replace '^global-packages:\s*', '').Trim()
$coreAssembly = Join-Path $globalPackages `
    "magick.net.core\$magickVersion\lib\net8.0\Magick.NET.Core.dll"
$magickRoot = Join-Path $globalPackages "magick.net-q16-anycpu\$magickVersion"
$magickAssembly = Join-Path $magickRoot 'lib\net8.0\Magick.NET-Q16-AnyCPU.dll'
$nativeDirectory = Join-Path $magickRoot 'runtimes\win-x64\native'

foreach ($requiredPath in @(
    $sourcePath,
    $coreAssembly,
    $magickAssembly,
    $nativeDirectory
)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required MSIX asset input was not found: $requiredPath"
    }
}

Add-Type -Path $coreAssembly
Add-Type -Path $magickAssembly
$env:PATH = "$nativeDirectory$([IO.Path]::PathSeparator)$env:PATH"

[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
[IO.Directory]::CreateDirectory($listingOutputDirectory) | Out-Null

function Write-TransparentIcon {
    param(
        [Parameter(Mandatory)]
        [string] $Destination,

        [Parameter(Mandatory)]
        [int] $Size
    )

    $settings = [ImageMagick.MagickReadSettings]::new()
    $settings.Width = $Size
    $settings.Height = $Size
    $settings.BackgroundColor = [ImageMagick.MagickColors]::Transparent

    $image = [ImageMagick.MagickImage]::new($sourcePath, $settings)
    try {
        $image.Format = [ImageMagick.MagickFormat]::Png32
        $image.Strip()
        $image.Write($Destination)
    }
    finally {
        $image.Dispose()
    }

    $verificationImage = [ImageMagick.MagickImage]::new($Destination)
    try {
        $pixels = $verificationImage.GetPixels()
        $lastX = $verificationImage.Width - 1
        $lastY = $verificationImage.Height - 1
        $cornerAlpha = @(
            $pixels.GetPixel(0, 0).ToColor().A
            $pixels.GetPixel($lastX, 0).ToColor().A
            $pixels.GetPixel(0, $lastY).ToColor().A
            $pixels.GetPixel($lastX, $lastY).ToColor().A
        )
        if ($cornerAlpha | Where-Object { $_ -ne 0 }) {
            throw "Generated icon has an opaque corner: $Destination"
        }
    }
    finally {
        $verificationImage.Dispose()
    }
}

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
