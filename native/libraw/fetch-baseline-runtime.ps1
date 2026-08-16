[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("win-x64", "linux-x64", "osx-arm64")]
    [string]$RuntimeIdentifier,
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot "artifacts/libraw/baseline/$RuntimeIdentifier"
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null

if ($RuntimeIdentifier -eq "osx-arm64") {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "baseline/osx-arm64/libraw.23.dylib") `
        -Destination $output -Force
    Write-Output $output
    return
}

$package = if ($RuntimeIdentifier -eq "win-x64") {
    "sdcb.libraw.runtime.win64"
} else {
    "sdcb.libraw.runtime.linux64"
}
$expectedHash = if ($RuntimeIdentifier -eq "win-x64") {
    "92DC2418F4DD888AB4E0FB6CB7D8FDA98B9ECA94EBFC00EAE9F216883DA16EB8"
} else {
    "E1F771B685610FCE195FC591456FA4BBF567AE28E540E47BC0AC1A170F1367C9"
}
$temporary = Join-Path ([IO.Path]::GetTempPath()) "happy-photon-$([guid]::NewGuid().ToString('N')).nupkg"
try {
    $url = "https://api.nuget.org/v3-flatcontainer/$package/0.21.1/$package.0.21.1.nupkg"
    Invoke-WebRequest -Uri $url -OutFile $temporary
    $actualHash = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) {
        throw "Audited baseline package hash mismatch for $package."
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($temporary)
    try {
        $prefix = "runtimes/$RuntimeIdentifier/native/"
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.StartsWith($prefix) -and $entry.Name) {
                $target = Join-Path $output $entry.Name
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            }
        }
    } finally {
        $archive.Dispose()
    }
} finally {
    Remove-Item -LiteralPath $temporary -ErrorAction SilentlyContinue
}
Write-Output $output
