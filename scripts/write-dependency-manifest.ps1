[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputPath,

    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidateSet("win-x64", "linux-x64", "osx-arm64")]
    [string]$RuntimeIdentifier
)

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot "HappyPhoton.csproj"
$arguments = @(
    "package",
    "list",
    "--project",
    $projectPath,
    "--no-restore",
    "--include-transitive",
    "--format",
    "json",
    "--output-version",
    "1"
)

$packageReportText = (& dotnet @arguments) -join [Environment]::NewLine
if ($LASTEXITCODE -ne 0) {
    throw "Could not generate the dependency inventory."
}

$packageReport = $packageReportText | ConvertFrom-Json
foreach ($project in $packageReport.projects) {
    $project.path = Split-Path -Leaf $project.path
}

$bundledNativeInventory = @()
if ($RuntimeIdentifier -eq "osx-arm64") {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $packagePath = Join-Path $projectRoot "packages/native/HappyPhoton.LibRaw.Native.0.22.2.7.nupkg"
    $archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $bundledNativeInventory = @(
            foreach ($name in @("libhappyphoton_libraw_bridge.dylib", "libraw.25.dylib")) {
                $entry = $archive.GetEntry("runtimes/osx-arm64/native/$name")
                if ($null -eq $entry) { throw "Packaged native entry is missing: $name" }
                $stream = $entry.Open()
                try {
                    $hash = [Security.Cryptography.SHA256]::HashData($stream)
                } finally {
                    $stream.Dispose()
                }
                [ordered]@{
                    name = if ($name.StartsWith("libhappyphoton")) { "Happy Photon LibRaw bridge" } else { "LibRaw" }
                    version = "0.22.2.7"
                    file = $name
                    sha256 = [Convert]::ToHexString($hash)
                    license = if ($name.StartsWith("libhappyphoton")) { "GPL-3.0-or-later" } else { "LGPL-2.1-only" }
                }
            }
        )
    } finally {
        $archive.Dispose()
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    product = "Happy Photon"
    version = $Version
    runtimeIdentifier = $RuntimeIdentifier
    generatedFrom = @("packages.lock.json")
    licenseNotices = @(
        "LICENSE",
        "THIRD_PARTY_NOTICES.md",
        "TRADEMARKS.md",
        "licenses/"
    )
    packageInventory = $packageReport
    bundledNativeInventory = $bundledNativeInventory
}

$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutputPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$json = $manifest | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText(
    $resolvedOutputPath,
    $json + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Wrote dependency manifest to $resolvedOutputPath"
