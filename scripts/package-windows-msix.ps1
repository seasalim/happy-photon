[CmdletBinding()]
param(
    [Parameter()]
    [string]$Version = '0.1.0',

    [Parameter()]
    [string]$PackageIdentityName = 'seasalim.HappyPhoton',

    [Parameter()]
    [string]$PackagePublisher = 'CN=45869051-E95D-4253-A058-DAB16BAF89B7',

    [Parameter()]
    [string]$PublisherDisplayName = 'seasalim',

    [Parameter()]
    [string]$SourceRevision = '',

    [Parameter()]
    [string]$BuildTimestampUtc = '',

    [Parameter()]
    [switch]$ContinuousIntegrationBuild,

    [Parameter()]
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\windows-msix'))
$publishDirectory = Join-Path $artifactRoot 'publish'
$stagingDirectory = Join-Path $artifactRoot 'staging'
$inspectionDirectory = Join-Path $artifactRoot 'inspection'
$packageDirectory = Join-Path $artifactRoot 'package'
$packagePath = Join-Path $packageDirectory "happy-photon-$Version-win-x64.msix"
$manifestTemplate = Join-Path $repositoryRoot 'packaging\windows\AppxManifest.xml'
$assetDirectory = Join-Path $repositoryRoot 'packaging\windows\Assets'
$projectPath = Join-Path $repositoryRoot 'HappyPhoton.csproj'

function Remove-BuildDirectory([string]$Path) {
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $artifactPrefix = "$artifactRoot$([IO.Path]::DirectorySeparatorChar)"
    if (-not $resolvedPath.StartsWith(
        $artifactPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear a path outside the MSIX artifact root: $resolvedPath"
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

function Resolve-MsixVersion([string]$SemVer) {
    $match = [regex]::Match(
        $SemVer,
        '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')
    if (-not $match.Success) {
        throw 'MSIX Store packages require a final SemVer version such as 0.1.0.'
    }

    $parts = 1..3 | ForEach-Object { [int]$match.Groups[$_].Value }
    if ($parts | Where-Object { $_ -gt 65535 }) {
        throw 'Each MSIX version component must be between 0 and 65535.'
    }

    return "$($parts[0]).$($parts[1]).$($parts[2]).0"
}

function Find-MakeAppx {
    $kitsDirectory = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    $makeAppx = Get-ChildItem $kitsDirectory -Recurse -Filter makeappx.exe |
        Where-Object FullName -Match '\\x64\\makeappx\.exe$' |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $makeAppx) {
        throw 'The x64 Windows SDK MakeAppx.exe tool was not found.'
    }

    return $makeAppx.FullName
}

$msixVersion = Resolve-MsixVersion $Version
$makeAppxPath = Find-MakeAppx

foreach ($requiredPath in @($manifestTemplate, $assetDirectory, $projectPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required MSIX packaging input was not found: $requiredPath"
    }
}

$registeredFromStaging = Get-AppxPackage -Name $PackageIdentityName |
    Where-Object {
        -not [string]::IsNullOrWhiteSpace($_.InstallLocation) -and
        [IO.Path]::GetFullPath($_.InstallLocation) -eq
            [IO.Path]::GetFullPath($stagingDirectory)
    } |
    Select-Object -First 1
if ($registeredFromStaging) {
    throw "The loose MSIX package is registered from $stagingDirectory. " +
        "Close Happy Photon and remove package " +
        "'$($registeredFromStaging.PackageFullName)' before rebuilding."
}

foreach ($directory in @(
    $publishDirectory,
    $stagingDirectory,
    $inspectionDirectory,
    $packageDirectory
)) {
    Remove-BuildDirectory $directory
    [IO.Directory]::CreateDirectory($directory) | Out-Null
}

if (-not $NoRestore) {
    & dotnet restore $projectPath `
        --locked-mode `
        -p:RuntimeIdentifier=win-x64 `
        -p:PublishReadyToRun=true
    if ($LASTEXITCODE -ne 0) {
        throw 'Locked restore for the Windows MSIX publish failed.'
    }
}

$publishArguments = @(
    'publish'
    $projectPath
    '-p:PublishProfile=win-x64-msix'
    "-p:Version=$Version"
    '--no-restore'
    '--output'
    $publishDirectory
)
if (-not [string]::IsNullOrWhiteSpace($SourceRevision)) {
    $publishArguments += "-p:SourceRevisionId=$SourceRevision"
    $publishArguments += "-p:SourceRevision=$SourceRevision"
}
if (-not [string]::IsNullOrWhiteSpace($BuildTimestampUtc)) {
    $publishArguments += "-p:BuildTimestampUtc=$BuildTimestampUtc"
}
if ($ContinuousIntegrationBuild) {
    $publishArguments += '-p:ContinuousIntegrationBuild=true'
}

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw 'Windows MSIX publish failed.'
}

& (Join-Path $PSScriptRoot 'write-dependency-manifest.ps1') `
    -OutputPath (Join-Path $publishDirectory 'DEPENDENCIES.json') `
    -Version $Version `
    -RuntimeIdentifier win-x64
if ($LASTEXITCODE -ne 0) {
    throw 'Dependency manifest generation failed.'
}

Get-ChildItem -LiteralPath $publishDirectory -Force |
    Copy-Item -Destination $stagingDirectory -Recurse -Force
Copy-Item -LiteralPath $assetDirectory `
    -Destination (Join-Path $stagingDirectory 'Assets') `
    -Recurse `
    -Force

[xml]$manifest = Get-Content -Raw -LiteralPath $manifestTemplate
$manifest.Package.Identity.SetAttribute('Name', $PackageIdentityName)
$manifest.Package.Identity.SetAttribute('Publisher', $PackagePublisher)
$manifest.Package.Identity.SetAttribute('Version', $msixVersion)
$namespaceManager = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
$namespaceManager.AddNamespace(
    'foundation',
    'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$publisherDisplayNameNode = $manifest.SelectSingleNode(
    '/foundation:Package/foundation:Properties/foundation:PublisherDisplayName',
    $namespaceManager)
if (-not $publisherDisplayNameNode) {
    throw 'The manifest template is missing PublisherDisplayName.'
}
$publisherDisplayNameNode.InnerText = $PublisherDisplayName

$manifestPath = Join-Path $stagingDirectory 'AppxManifest.xml'
$writerSettings = [Xml.XmlWriterSettings]::new()
$writerSettings.Encoding = [Text.UTF8Encoding]::new($false)
$writerSettings.Indent = $true
$writer = [Xml.XmlWriter]::Create($manifestPath, $writerSettings)
try {
    $manifest.Save($writer)
}
finally {
    $writer.Dispose()
}

$packOutput = & $makeAppxPath pack `
    /o `
    /h SHA256 `
    /d $stagingDirectory `
    /p $packagePath 2>&1
if ($LASTEXITCODE -ne 0) {
    $packOutput | Write-Error
    throw 'MakeAppx failed to create the Windows MSIX package.'
}
$packOutput | Select-Object -Last 3 | Write-Output

$unpackOutput = & $makeAppxPath unpack `
    /o `
    /p $packagePath `
    /d $inspectionDirectory 2>&1
if ($LASTEXITCODE -ne 0) {
    $unpackOutput | Write-Error
    throw 'MakeAppx failed to validate and unpack the Windows MSIX package.'
}
$unpackOutput | Select-Object -Last 3 | Write-Output

$requiredPackageFiles = @(
    'AppxManifest.xml',
    'HappyPhoton.exe',
    'LICENSE',
    'TRADEMARKS.md',
    'THIRD_PARTY_NOTICES.md',
    'DEPENDENCIES.json'
)
foreach ($relativePath in $requiredPackageFiles) {
    $inspectionPath = Join-Path $inspectionDirectory $relativePath
    if (-not (Test-Path -LiteralPath $inspectionPath -PathType Leaf)) {
        throw "The MSIX package is missing required file: $relativePath"
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $inspectionDirectory 'licenses'))) {
    throw 'The MSIX package is missing the required licenses directory.'
}

$package = Get-Item -LiteralPath $packagePath
Write-Output "MSIX package: $($package.FullName)"
Write-Output "MSIX version: $msixVersion"
Write-Output "MSIX size: $($package.Length) bytes"
Write-Output "Loose package: $stagingDirectory"
