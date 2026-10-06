[CmdletBinding()]
param()

# Rasterises Assets/happy-photon-icon.svg into the app's own icon files: the 256 px
# PNG (Help, the AppImage and the macOS .icns) and the Windows .ico, each frame
# rasterised at its own size. Also rasterises the title bar's monochrome marks, one per theme. MSIX and Store assets come from
# generate-windows-msix-assets.ps1.

. (Join-Path $PSScriptRoot 'icon-rasters.ps1')

Import-MagickNet

$assetsDirectory = Join-Path $repositoryRoot 'Assets'
$pngPath = Join-Path $assetsDirectory 'happy-photon-icon.png'
$monoNames = @('happy-photon-icon-mono', 'happy-photon-icon-mono-midgray')
$icoPath = Join-Path $assetsDirectory 'happy-photon-icon.ico'
$icoSizes = @(16, 24, 32, 48, 64, 128, 256)

Write-TransparentIcon -Destination $pngPath -Size 256
foreach ($monoName in $monoNames) {
    Write-TransparentIcon -Destination (Join-Path $assetsDirectory "$monoName.png") -Size 256 `
        -Source (Join-Path $assetsDirectory "$monoName.svg")
}

# Magick.NET's ICO writer stores uncompressed bitmaps; each frame is packed as PNG
# instead, which Windows reads at every size and keeps the .ico (and the exe) small.
$framePngs = foreach ($size in $icoSizes) {
    $image = Read-IconRaster -Size $size
    try {
        , $image.ToByteArray()
    }
    finally {
        $image.Dispose()
    }
}

$stream = [IO.File]::Create($icoPath)
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$icoSizes.Count)

    $offset = 6 + 16 * $icoSizes.Count
    for ($index = 0; $index -lt $icoSizes.Count; $index++) {
        $dimension = [byte]($icoSizes[$index] % 256)
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$framePngs[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $framePngs[$index].Length
    }

    foreach ($png in $framePngs) {
        $writer.Write($png)
    }
}
finally {
    $writer.Dispose()
}

$written = [ImageMagick.MagickImageCollection]::new($icoPath)
try {
    $writtenSizes = @($written | ForEach-Object { [int]$_.Width } | Sort-Object)
    if (($writtenSizes -join ',') -ne ($icoSizes -join ',')) {
        throw "The .ico holds frames $($writtenSizes -join ', '), not $($icoSizes -join ', ')."
    }

    foreach ($frame in $written) {
        Assert-TransparentCorners -Image $frame -Description "$icoPath ($($frame.Width) px)"
    }
}
finally {
    $written.Dispose()
}

Write-Output "Generated $pngPath"
foreach ($monoName in $monoNames) {
    Write-Output "Generated $(Join-Path $assetsDirectory "$monoName.png")"
}
Write-Output "Generated $icoPath ($($icoSizes -join ', ') px)"
