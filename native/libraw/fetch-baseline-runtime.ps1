[CmdletBinding()]
param(
    [string]$OutputDirectory
)

# Restores the audited LibRaw 0.21.1 Windows runtime for the manual ABI-23
# parity oracle (see oracle/README.md). Nothing in the automated builds uses it.
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot "artifacts/libraw/baseline/win-x64"
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null

$package = "sdcb.libraw.runtime.win64"
$expectedHash = "92DC2418F4DD888AB4E0FB6CB7D8FDA98B9ECA94EBFC00EAE9F216883DA16EB8"
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
        $prefix = "runtimes/win-x64/native/"
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
