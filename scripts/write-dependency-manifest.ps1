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

# Identify each bundled native by file name. The inventory is asymmetric:
# Windows and Linux bundle jpeg/lcms/zlib, while Apple Silicon links JPEG
# statically and uses system zlib, so it carries only the bridge and LibRaw.
function Get-NativeComponent {
    param([string]$FileName)

    switch -Regex ($FileName) {
        "^libhappyphoton_libraw_bridge" {
            return @{ name = "Happy Photon LibRaw bridge"; license = "GPL-3.0-or-later" }
        }
        "^happyphoton_libraw_bridge" {
            return @{ name = "Happy Photon LibRaw bridge"; license = "GPL-3.0-or-later" }
        }
        "^(lib)?raw" { return @{ name = "LibRaw"; license = "LGPL-2.1-only" } }
        "jpeg" { return @{ name = "libjpeg-turbo"; license = "IJG AND BSD-3-Clause" } }
        "lcms" { return @{ name = "Little CMS"; license = "MIT" } }
        "^(lib)?z" { return @{ name = "zlib"; license = "Zlib" } }
        default { throw "Unrecognized bundled native file: $FileName" }
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageFile = Get-ChildItem -Path (Join-Path $projectRoot "packages/native") `
    -Filter "HappyPhoton.LibRaw.Native.*.nupkg" -File |
    Sort-Object Name | Select-Object -Last 1
if ($null -eq $packageFile) {
    throw "No HappyPhoton.LibRaw.Native package found under packages/native."
}
$packageVersion = [regex]::Match(
    $packageFile.Name, "^HappyPhoton\.LibRaw\.Native\.(.+)\.nupkg$").Groups[1].Value

# Component versions come from the package's provenance rather than this
# script, so they cannot drift when the native package is rebuilt.
$componentVersions = @{}
$provenancePath = $packageFile.FullName -replace '\.nupkg$', '.provenance.json'
if (-not (Test-Path -LiteralPath $provenancePath)) {
    throw "Native package provenance is missing: $provenancePath"
}
$wanted = @{
    "libraw" = "LibRaw"
    "libjpeg-turbo" = "libjpeg-turbo"
    "lcms" = "Little CMS"
    "zlib" = "zlib"
}
$provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
$queue = [Collections.Generic.Queue[object]]::new()
$queue.Enqueue($provenance)
while ($queue.Count -gt 0) {
    $node = $queue.Dequeue()
    if ($node -is [Management.Automation.PSCustomObject]) {
        # Do NOT name these $name/$version: $Version is a script parameter and
        # PowerShell variable names are case-insensitive, so the loop would
        # silently overwrite the caller's argument.
        $nameProperty = $node.PSObject.Properties['name']
        $versionProperty = $node.PSObject.Properties['version']
        if ($nameProperty -and $versionProperty -and
            $wanted.ContainsKey([string]$nameProperty.Value)) {
            $componentName = $wanted[[string]$nameProperty.Value]
            if (-not $componentVersions.ContainsKey($componentName)) {
                # vcpkg versions can carry a "#<port>" suffix; keep the upstream part.
                $componentVersions[$componentName] =
                    ([string]$versionProperty.Value) -replace '#.*$', ''
            }
        }
        foreach ($property in $node.PSObject.Properties) { $queue.Enqueue($property.Value) }
    } elseif ($node -is [object[]]) {
        foreach ($item in $node) { $queue.Enqueue($item) }
    }
}

$archive = [IO.Compression.ZipFile]::OpenRead($packageFile.FullName)
try {
    $prefix = "runtimes/$RuntimeIdentifier/native/"
    $entries = @($archive.Entries | Where-Object {
        $_.FullName.StartsWith($prefix) -and $_.FullName.Length -gt $prefix.Length
    })
    if ($entries.Count -eq 0) {
        throw "The native package contains no entries for $RuntimeIdentifier."
    }

    $bundledNativeInventory = @(
        foreach ($entry in ($entries | Sort-Object FullName)) {
            $fileName = Split-Path -Leaf $entry.FullName
            $component = Get-NativeComponent $fileName
            $stream = $entry.Open()
            try {
                $hash = [Security.Cryptography.SHA256]::HashData($stream)
            } finally {
                $stream.Dispose()
            }
            [ordered]@{
                name = $component.name
                version = if ($componentVersions.ContainsKey($component.name)) {
                    $componentVersions[$component.name]
                } else {
                    $packageVersion
                }
                packageVersion = $packageVersion
                file = $fileName
                sha256 = [Convert]::ToHexString($hash)
                license = $component.license
            }
        }
    )
} finally {
    $archive.Dispose()
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
