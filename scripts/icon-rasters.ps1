# Shared by the icon generators: loads the app's Magick.NET package and rasterises
# Assets/happy-photon-icon.svg with transparent corners. Dot-source it.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $repositoryRoot 'Assets\happy-photon-icon.svg'

function Import-MagickNet {
    $projectPath = Join-Path $repositoryRoot 'HappyPhoton.csproj'
    $magickReference = (& dotnet msbuild $projectPath -p:RuntimeIdentifier=win-x64 `
        -getItem:PackageReference | Out-String | ConvertFrom-Json).Items.PackageReference |
        Where-Object { $_.Identity -like 'Magick.NET-Q16-*' }
    if ($LASTEXITCODE -ne 0 -or -not $magickReference) {
        throw 'Could not resolve the Magick.NET package for win-x64.'
    }

    $magickPackage = [string]$magickReference.Identity
    $magickVersion = [string]$magickReference.Version
    $globalPackagesLine = & dotnet nuget locals global-packages --list
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not resolve the NuGet global-packages directory.'
    }

    $globalPackages = ($globalPackagesLine -replace '^global-packages:\s*', '').Trim()
    $coreAssembly = Join-Path $globalPackages `
        "magick.net.core\$magickVersion\lib\net8.0\Magick.NET.Core.dll"
    $magickRoot = Join-Path $globalPackages "$($magickPackage.ToLowerInvariant())\$magickVersion"
    $magickAssembly = Join-Path $magickRoot "lib\net8.0\$magickPackage.dll"
    $nativeDirectory = Join-Path $magickRoot 'runtimes\win-x64\native'

    foreach ($requiredPath in @(
        $sourcePath,
        $coreAssembly,
        $magickAssembly,
        $nativeDirectory
    )) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "Required icon raster input was not found: $requiredPath"
        }
    }

    Add-Type -Path $coreAssembly
    Add-Type -Path $magickAssembly
    $env:PATH = "$nativeDirectory$([IO.Path]::PathSeparator)$env:PATH"
}

function Read-IconRaster {
    param(
        [Parameter(Mandatory)]
        [int] $Size
    )

    $settings = [ImageMagick.MagickReadSettings]::new()
    $settings.Width = $Size
    $settings.Height = $Size
    $settings.BackgroundColor = [ImageMagick.MagickColors]::Transparent

    $image = [ImageMagick.MagickImage]::new($sourcePath, $settings)
    $image.Format = [ImageMagick.MagickFormat]::Png32
    $image.Strip()
    return $image
}

function Assert-TransparentCorners {
    param(
        [Parameter(Mandatory)]
        [ImageMagick.IMagickImage] $Image,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $pixels = $Image.GetPixels()
    $lastX = $Image.Width - 1
    $lastY = $Image.Height - 1
    $cornerAlpha = @(
        $pixels.GetPixel(0, 0).ToColor().A
        $pixels.GetPixel($lastX, 0).ToColor().A
        $pixels.GetPixel(0, $lastY).ToColor().A
        $pixels.GetPixel($lastX, $lastY).ToColor().A
    )
    if ($cornerAlpha | Where-Object { $_ -ne 0 }) {
        throw "Generated icon has an opaque corner: $Description"
    }
}

function Write-TransparentIcon {
    param(
        [Parameter(Mandatory)]
        [string] $Destination,

        [Parameter(Mandatory)]
        [int] $Size
    )

    $image = Read-IconRaster -Size $Size
    try {
        $image.Write($Destination)
    }
    finally {
        $image.Dispose()
    }

    $verificationImage = [ImageMagick.MagickImage]::new($Destination)
    try {
        Assert-TransparentCorners -Image $verificationImage -Description $Destination
    }
    finally {
        $verificationImage.Dispose()
    }
}
