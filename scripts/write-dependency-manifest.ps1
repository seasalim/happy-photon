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
    $libRawPath = Join-Path $projectRoot "runtimes/osx-arm64/native/libraw.23.dylib"
    $bundledNativeInventory = @([ordered]@{
        name = "LibRaw"
        version = "0.21.1"
        file = "libraw.23.dylib"
        sha256 = (Get-FileHash $libRawPath -Algorithm SHA256).Hash
        license = "LGPL-2.1-only"
    })
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
