# The only download step: restores the pinned PSScriptAnalyzer and Prettier into the tool root and
# confirms the pinned .NET SDK is installed. Check and apply never download or install anything.
[CmdletBinding()]
param([string]$ToolRoot)

$version = $PSVersionTable.PSVersion

if ($version.Major -lt 7 -or ($version.Major -eq 7 -and $version.Minor -lt 4)) {
    Write-Output 'VERSION: PowerShell 7.4 or later is required'
    exit 2
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'FormatTools.psm1') -Force

if (-not $ToolRoot) {
    $ToolRoot = Get-DefaultToolRoot (Join-Path $PSScriptRoot '../..')
}

$ToolRoot = [IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ToolRoot))
$pins = Get-FormatPins
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('hp-format-install-' + [guid]::NewGuid().ToString('N'))
$exitCode = 2

try {
    [void][IO.Directory]::CreateDirectory($scratch)
    [void][IO.Directory]::CreateDirectory($ToolRoot)
    $environment = Get-ChildEnvironment $scratch
    $sdks = Invoke-FormatProcess -Program dotnet -Arguments @('--list-sdks') -WorkingDirectory $scratch `
        -Environment $environment -TimeoutSeconds 120

    if (-not @($sdks.Output -split "`n" | Where-Object { $_.StartsWith("$($pins.sdk) ") }).Count) {
        throw "VERSION: install .NET SDK $($pins.sdk) first; this script does not download SDKs"
    }

    $module = Join-Path $ToolRoot "PSScriptAnalyzer/$($pins.pssa)"
    $manifest = Join-Path $module 'PSScriptAnalyzer.psd1'
    $installed = (Test-Path -LiteralPath $manifest -PathType Leaf) -and
    (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -eq $pins.pssaManifestSha256

    if (-not $installed) {
        $package = Join-Path $scratch 'PSScriptAnalyzer.zip'
        $uri = "https://www.powershellgallery.com/api/v2/package/PSScriptAnalyzer/$($pins.pssa)"
        Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $package -TimeoutSec 300

        if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $pins.pssaPackageSha256) {
            throw 'VERSION: the downloaded PSScriptAnalyzer package does not match its pinned SHA-256'
        }

        # Expand beside the destination so the final move stays on one volume.
        $staging = Join-Path $ToolRoot ('.staging-' + [guid]::NewGuid().ToString('N'))

        try {
            Expand-Archive -LiteralPath $package -DestinationPath $staging
            $stagedManifest = Join-Path $staging 'PSScriptAnalyzer.psd1'

            if ((Get-FileHash -LiteralPath $stagedManifest -Algorithm SHA256).Hash -ne $pins.pssaManifestSha256) {
                throw 'VERSION: the PSScriptAnalyzer manifest does not match its pinned SHA-256'
            }

            if (Test-Path -LiteralPath $module) {
                Remove-Item -LiteralPath $module -Recurse -Force
            }

            [void][IO.Directory]::CreateDirectory((Split-Path $module))
            Move-Item -LiteralPath $staging -Destination $module
        } finally {
            if (Test-Path -LiteralPath $staging) {
                Remove-Item -LiteralPath $staging -Recurse -Force
            }
        }
    }

    # npm ci verifies Prettier against the committed lockfile; the npm cache stays in the deleted scratch folder.
    foreach ($name in @('package.json', 'package-lock.json')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "config/$name") -Destination $ToolRoot -Force
    }

    $environment['npm_config_cache'] = Join-Path $scratch 'npm-cache'
    $arguments = @('ci', '--ignore-scripts', '--no-audit', '--no-fund', '--no-update-notifier')
    [void](Invoke-FormatProcess -Program npm -Arguments $arguments -WorkingDirectory $ToolRoot `
            -Environment $environment -TimeoutSeconds 600)
    Write-Output "Installed PSScriptAnalyzer $($pins.pssa) and Prettier $($pins.prettier) in $ToolRoot; .NET SDK $($pins.sdk) is present."
    $exitCode = 0
} catch {
    Write-Output $_.Exception.Message
} finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}

exit $exitCode
