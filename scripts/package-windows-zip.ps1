[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDirectory,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

# Writes the Windows release ZIP at maximum deflate (SmallestSize), with the
# publish directory's contents at the archive root.
$ErrorActionPreference = 'Stop'

$source = (Resolve-Path -LiteralPath $PublishDirectory).Path
$destination = [IO.Path]::GetFullPath($OutputPath, (Get-Location).Path)
if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory(
    $source,
    $destination,
    [IO.Compression.CompressionLevel]::SmallestSize,
    $false)
