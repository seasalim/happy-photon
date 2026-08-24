[CmdletBinding()]
param(
    [string]$SourceRoot,
    [string]$Destination,
    [string]$Origin,
    [string]$BasePath = "/",
    [ValidateSet("Unavailable", "Fixture", "GitHub")] [string]$ReleaseDataSource = "Unavailable",
    [string]$ReleaseFixturePath,
    [ValidateSet("stable", "preview")] [string]$ReleaseChannel = "stable",
    [switch]$Production,
    [switch]$AdvertiseDownloads
)

$ErrorActionPreference = "Stop"
$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))

if (-not $SourceRoot) {
    $SourceRoot = Join-Path $projectRoot "site"
}
if (-not $Destination) {
    $Destination = Join-Path $projectRoot "artifacts/site"
}

$sourcePath = [System.IO.Path]::GetFullPath($SourceRoot)
$destinationPath = [System.IO.Path]::GetFullPath($Destination)
$expectedDestination = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts/site"))

if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
    throw "Site source does not exist: $sourcePath"
}
if ($destinationPath -ne $expectedDestination) {
    throw "The foundation build writes only to $expectedDestination."
}
if (-not $destinationPath.StartsWith($projectRoot + [System.IO.Path]::DirectorySeparatorChar)) {
    throw "Destination must remain beneath the workspace."
}

$normalizedBasePath = "/" + $BasePath.Trim("/")
if ($normalizedBasePath -eq "/") {
    $normalizedBasePath = "/"
}
else {
    $normalizedBasePath += "/"
}

if (Test-Path -LiteralPath $destinationPath) {
    Remove-Item -LiteralPath $destinationPath -Recurse -Force
}
New-Item -ItemType Directory -Path $destinationPath | Out-Null

$configPath = Join-Path $sourcePath "site-config.json"
$config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json
$statePair = "$($config.pagesDeployment)+$($config.downloadProfile)"
$validStatePairs = @("disabled+predownload", "disabled+verify", "project+verify", "project+live", "custom+live")
if ($statePair -notin $validStatePairs) {
    throw "Unsupported Pages deployment and download profile pair: $statePair"
}
if ($AdvertiseDownloads -and $ReleaseDataSource -ne "GitHub") {
    throw "Download advertising requires verified GitHub release data."
}
if ($Production -and $ReleaseDataSource -eq "Fixture") {
    throw "Fixture release data cannot be used in a production build."
}
if ($ReleaseChannel -ne $config.releaseChannel) {
    throw "Requested release channel does not match committed site configuration."
}
if ($Production) {
    $expectedReleaseDataSource = if ($config.downloadProfile -eq "predownload") { "Unavailable" } else { "GitHub" }
    $expectedAdvertising = $config.downloadProfile -eq "live"
    if ($ReleaseDataSource -ne $expectedReleaseDataSource -or [bool]$AdvertiseDownloads -ne $expectedAdvertising) {
        throw "Production release-data parameters do not match the committed download profile."
    }
    if ($config.pagesDeployment -eq "project" -and $normalizedBasePath -ne "/happy-photon/") {
        throw "Project deployment requires the /happy-photon/ base path."
    }
    if ($config.pagesDeployment -eq "custom" -and $normalizedBasePath -ne "/") {
        throw "Custom-domain deployment requires the root base path."
    }
}

Import-Module (Join-Path $PSScriptRoot "SiteBuild.psm1") -Force
$manifest = switch ($ReleaseDataSource) {
    "Unavailable" {
        New-UnavailableSiteManifest -Config $config
    }
    "Fixture" {
        if (-not $ReleaseFixturePath) {
            throw "Fixture release data requires -ReleaseFixturePath."
        }
        $fixturePath = [System.IO.Path]::GetFullPath($ReleaseFixturePath)
        if (-not $fixturePath.StartsWith($projectRoot + [System.IO.Path]::DirectorySeparatorChar)) {
            throw "Release fixture must remain beneath the workspace."
        }
        $fixture = Get-Content -Raw -LiteralPath $fixturePath | ConvertFrom-Json
        New-ReleaseSiteManifest -Config $config -Repository $fixture.repository -Releases @($fixture.releases) -PreferredChannel $ReleaseChannel -DataSource "Fixture"
    }
    "GitHub" {
        if ($config.repositoryUrl -notmatch "^https://github\.com/([^/]+)/([^/]+?)/?$") {
            throw "Repository URL is not a supported GitHub repository URL."
        }
        $repositoryName = "$($Matches[1])/$($Matches[2])"
        $headers = @{ Accept = "application/vnd.github+json"; "User-Agent" = "Happy-Photon-Site-Build" }
        if ($env:GITHUB_TOKEN) {
            $headers.Authorization = "Bearer $($env:GITHUB_TOKEN)"
        }
        $repository = Invoke-RestMethod -Uri "https://api.github.com/repos/$repositoryName" -Headers $headers
        if ($repository.private -eq $true -or [string]$repository.visibility -ne "public") {
            throw "GitHub release builds require confirmed public repository visibility."
        }
        $releaseResponse = Invoke-RestMethod -Uri "https://api.github.com/repos/$repositoryName/releases?per_page=30" -Headers $headers
        $releases = [object[]]$releaseResponse
        $provenanceVerified = $false
        $selection = Select-SiteRelease -Releases $releases -PreferredChannel $ReleaseChannel
        if ($selection) {
            $selectedRelease = $selection.Release
            $version = ([string]$selectedRelease.tag_name).TrimStart("v")
            $checksumAsset = Find-ReleaseAsset -Assets @($selectedRelease.assets) -ExpectedName "SHA256SUMS.txt"
            if (-not $checksumAsset) {
                if ($AdvertiseDownloads) {
                    throw "Live advertising requires a public checksum asset."
                }
            }
            if ($checksumAsset) {
                $checksumResponse = Invoke-WebRequest -UseBasicParsing -Uri $checksumAsset.browser_download_url -Headers $headers
                $checksumText = if ($checksumResponse.Content -is [byte[]]) {
                    [Text.Encoding]::UTF8.GetString($checksumResponse.Content)
                }
                else {
                    [string]$checksumResponse.Content
                }
                $provenanceVerified = $true
                foreach ($assetName in @("happy-photon-$version-osx-arm64.zip", "happy-photon-$version-linux-x64.tar.gz")) {
                    $checksumMatch = [regex]::Match($checksumText, "(?im)^([0-9a-f]{64})\s+\*?" + [regex]::Escape($assetName) + "\s*$")
                    if (-not $checksumMatch.Success) {
                        $provenanceVerified = $false
                        break
                    }
                    $digest = "sha256:$($checksumMatch.Groups[1].Value.ToLowerInvariant())"
                    try {
                        $attestations = Invoke-RestMethod -Uri "https://api.github.com/repos/$repositoryName/attestations/$digest" -Headers $headers
                        if (@($attestations.attestations).Count -eq 0) {
                            $provenanceVerified = $false
                            break
                        }
                    }
                    catch {
                        $provenanceVerified = $false
                        break
                    }
                }
            }
        }
        if ($AdvertiseDownloads -and -not $selection) {
            throw "Live advertising requires a published release."
        }
        if ($AdvertiseDownloads -and -not $provenanceVerified) {
            throw "Live advertising requires public provenance attestations for every advertised GitHub package."
        }
        New-ReleaseSiteManifest -Config $config -Repository $repository -Releases $releases -PreferredChannel $ReleaseChannel -AdvertiseDownloads:$AdvertiseDownloads -ProvenanceVerified:$provenanceVerified -DataSource "GitHub"
    }
}

$manifestJson = $manifest | ConvertTo-Json -Depth 8
$scriptSources = @(Get-ChildItem -LiteralPath (Join-Path $sourcePath "assets/js") -Filter "*.js" | Sort-Object Name)
$scriptFingerprintContent = (($scriptSources | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n") + $manifestJson
$scriptBytes = [Text.Encoding]::UTF8.GetBytes($scriptFingerprintContent)
$scriptVersion = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($scriptBytes)).Substring(0, 12).ToLowerInvariant()

$cssFiles = @(
    "tokens.css",
    "base.css",
    "layout.css",
    "components.css",
    "pages.css"
)
$cssContent = foreach ($cssFile in $cssFiles) {
    Get-Content -Raw -LiteralPath (Join-Path $sourcePath "assets/css/$cssFile")
}
$combinedCss = ($cssContent -join "`n").Trim() + "`n"
$cssBytes = [System.Text.Encoding]::UTF8.GetBytes($combinedCss)
$cssHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($cssBytes)).Substring(0, 12).ToLowerInvariant()
$cssRelativePath = "assets/css/site.$cssHash.css"
$cssDestination = Join-Path $destinationPath $cssRelativePath
New-Item -ItemType Directory -Path (Split-Path -Parent $cssDestination) -Force | Out-Null
[System.IO.File]::WriteAllText($cssDestination, $combinedCss, [System.Text.UTF8Encoding]::new($false))

foreach ($assetDirectory in @("images", "fonts", "js")) {
    $assetSource = Join-Path $sourcePath "assets/$assetDirectory"
    if (Test-Path -LiteralPath $assetSource) {
        Copy-Item -LiteralPath $assetSource -Destination (Join-Path $destinationPath "assets/$assetDirectory") -Recurse
    }
}
$stagedDownloadsScript = Join-Path $destinationPath "assets/js/downloads.js"
$downloadsScriptContent = Get-Content -Raw -LiteralPath $stagedDownloadsScript
$downloadsScriptContent = $downloadsScriptContent.Replace('"./platform.js"', '"./platform.js?v=' + $scriptVersion + '"')
$downloadsScriptContent = $downloadsScriptContent.Replace('localPath("downloads.json")', 'localPath("downloads.json?v=' + $scriptVersion + '")')
[IO.File]::WriteAllText($stagedDownloadsScript, $downloadsScriptContent, [Text.UTF8Encoding]::new($false))

$partialRoot = [System.IO.Path]::GetFullPath((Join-Path $sourcePath "partials"))
function Expand-SitePartials {
    param(
        [Parameter(Mandatory)] [string]$Content,
        [string[]]$Stack = @(),
        [int]$Depth = 0
    )

    if ($Depth -gt 12) {
        throw "Partial expansion exceeded the maximum depth."
    }

    $pattern = "\{\{>\s*([a-zA-Z0-9_/-]+)\s*\}\}"
    $match = [regex]::Match($Content, $pattern)
    while ($match.Success) {
        $partialName = $match.Groups[1].Value
        if ($partialName -in $Stack) {
            throw "Partial cycle detected: $($Stack + $partialName -join ' -> ')"
        }

        $partialPath = [System.IO.Path]::GetFullPath((Join-Path $partialRoot ($partialName + ".html")))
        if (-not $partialPath.StartsWith($partialRoot + [System.IO.Path]::DirectorySeparatorChar)) {
            throw "Partial path escapes the partial root: $partialName"
        }
        if (-not (Test-Path -LiteralPath $partialPath -PathType Leaf)) {
            throw "Missing site partial: $partialName"
        }

        $partialContent = Get-Content -Raw -LiteralPath $partialPath
        $expandedPartial = Expand-SitePartials -Content $partialContent -Stack ($Stack + $partialName) -Depth ($Depth + 1)
        $Content = $Content.Substring(0, $match.Index) + $expandedPartial + $Content.Substring($match.Index + $match.Length)
        $match = [regex]::Match($Content, $pattern)
    }
    return $Content
}

function Get-PlatformPresentation {
    param(
        [Parameter(Mandatory)] $Platform,
        [Parameter(Mandatory)] [string]$UnavailableStatus,
        [Parameter(Mandatory)] [string]$UnavailableClass
    )

    $available = $Platform.availability -eq "available" -and $Platform.url -match "^https://"
    $verified = $Platform.availability -eq "verified"
    [ordered]@{
        Status = if ($available) { if ($Platform.channel -eq "preview") { "PREVIEW AVAILABLE" } else { "AVAILABLE" } } elseif ($verified) { "VERIFIED" } else { $UnavailableStatus }
        StatusClass = if ($available -or $verified) { "status-available" } else { $UnavailableClass }
        Detail = if ($available) { [string]$Platform.detail } else { [string]$Platform.reason }
        Note = [string]$Platform.note
        ActionLabel = [string]$Platform.actionLabel
        ActionUrl = if ($available) { [string]$Platform.url } else { $normalizedBasePath + "download/#" + $Platform.id }
        ActionState = if ($available) { "" } else { "hidden" }
    }
}

$windowsPresentation = Get-PlatformPresentation -Platform $manifest.platforms.windows -UnavailableStatus "IN CERTIFICATION" -UnavailableClass "status-progress"
$macosPresentation = Get-PlatformPresentation -Platform $manifest.platforms.macos -UnavailableStatus "SIGNING PENDING" -UnavailableClass "status-pending"
$linuxPresentation = Get-PlatformPresentation -Platform $manifest.platforms.linux -UnavailableStatus "REPOSITORY LAUNCH" -UnavailableClass "status-pending"
$hasAdvertisedRelease = [bool]$manifest.advertising -and $manifest.release -and $manifest.release.version
$hasRelease = $manifest.release -and $manifest.release.version
$releaseStatus = if ($manifest.release -and $manifest.release.version) {
    "$(if ($manifest.selectedChannel -eq 'preview') { 'Preview' } else { 'Stable' }) $($manifest.release.version)"
}
else {
    "First public release is almost here"
}
$launchStatusTitle = if ($hasAdvertisedRelease) { "Stable release $($manifest.release.version)" } else { "Launch checks in progress" }
$launchStatusDetail = if ($hasAdvertisedRelease) { "Public destinations and release metadata verified" } else { "Downloads remain unavailable until every public-release gate passes" }
$releaseDate = if ($hasRelease) {
    ([DateTimeOffset]$manifest.release.publishedAt).ToString("MMMM d, yyyy", [Globalization.CultureInfo]::InvariantCulture)
}
else {
    "Pending"
}
$isIndexable = [bool]$Production -and $config.pagesDeployment -eq "custom" -and $config.downloadProfile -eq "live"
if ($isIndexable -and $Origin -notmatch "^https://") {
    throw "An indexable production build requires an HTTPS origin."
}
$metadataOrigin = if ($Origin) { $Origin.TrimEnd("/") } else { "https://happyphoton.app" }
$canonicalBase = $metadataOrigin + $normalizedBasePath
$socialCardUrl = $canonicalBase + "assets/images/social-card.png"
$softwareApplication = [ordered]@{
    "@context" = "https://schema.org"
    "@type" = "SoftwareApplication"
    name = "Happy Photon"
    applicationCategory = "MultimediaApplication"
    operatingSystem = @("Windows x64", "macOS 14+ on Apple Silicon", "Linux x64")
    license = "$($config.repositoryUrl)/blob/main/LICENSE"
    codeRepository = [string]$config.repositoryUrl
    url = $canonicalBase
}
if ($manifest.release -and $manifest.release.version) {
    $softwareApplication["softwareVersion"] = [string]$manifest.release.version
}
$softwareApplicationJson = $softwareApplication | ConvertTo-Json -Compress -Depth 5

$tokens = [ordered]@{
    "BASE_PATH" = $normalizedBasePath
    "STYLESHEET_PATH" = $normalizedBasePath + $cssRelativePath
    "SCRIPT_VERSION" = $scriptVersion
    "REPOSITORY_URL" = [string]$config.repositoryUrl
    "ISSUES_URL" = [string]$config.issuesUrl
    "DOCUMENTATION_URL" = [string]$config.documentationUrl
    "RELEASES_URL" = [string]$config.releasesUrl
    "YEAR" = [DateTime]::UtcNow.Year.ToString()
    "RELEASE_STATUS" = $releaseStatus
    "RELEASE_DETAILS_STATE" = if ($hasRelease) { "" } else { "hidden" }
    "RELEASE_VERSION" = if ($hasRelease) { [string]$manifest.release.version } else { "Pending" }
    "RELEASE_CHANNEL" = if ($hasRelease) { if ($manifest.selectedChannel -eq "preview") { "Preview" } else { "Stable" } } else { "Pending" }
    "RELEASE_DATE" = $releaseDate
    "RELEASE_NOTES_URL" = if ($hasRelease) { [string]$manifest.release.notesUrl } else { [string]$config.releasesUrl }
    "CHECKSUM_URL" = if ($hasRelease) { [string]$manifest.release.checksumUrl } else { [string]$config.releasesUrl }
    "PROVENANCE_URL" = if ($hasRelease -and $manifest.release.provenanceUrl) { [string]$manifest.release.provenanceUrl } else { [string]$config.releasesUrl }
    "PROVENANCE_STATE" = if ($hasRelease -and $manifest.release.provenanceUrl) { "" } else { "hidden" }
    "LAUNCH_STATUS_TITLE" = $launchStatusTitle
    "LAUNCH_STATUS_DETAIL" = $launchStatusDetail
    "WINDOWS_STATUS" = $windowsPresentation.Status
    "WINDOWS_STATUS_CLASS" = $windowsPresentation.StatusClass
    "WINDOWS_DETAIL" = $windowsPresentation.Detail
    "WINDOWS_NOTE" = $windowsPresentation.Note
    "WINDOWS_ACTION_LABEL" = $windowsPresentation.ActionLabel
    "WINDOWS_ACTION_URL" = $windowsPresentation.ActionUrl
    "WINDOWS_ACTION_STATE" = $windowsPresentation.ActionState
    "MACOS_STATUS" = $macosPresentation.Status
    "MACOS_STATUS_CLASS" = $macosPresentation.StatusClass
    "MACOS_DETAIL" = $macosPresentation.Detail
    "MACOS_NOTE" = $macosPresentation.Note
    "MACOS_ACTION_LABEL" = $macosPresentation.ActionLabel
    "MACOS_ACTION_URL" = $macosPresentation.ActionUrl
    "MACOS_ACTION_STATE" = $macosPresentation.ActionState
    "LINUX_STATUS" = $linuxPresentation.Status
    "LINUX_STATUS_CLASS" = $linuxPresentation.StatusClass
    "LINUX_DETAIL" = $linuxPresentation.Detail
    "LINUX_NOTE" = $linuxPresentation.Note
    "LINUX_ACTION_LABEL" = $linuxPresentation.ActionLabel
    "LINUX_ACTION_URL" = $linuxPresentation.ActionUrl
    "LINUX_ACTION_STATE" = $linuxPresentation.ActionState
    "ROBOTS_DIRECTIVE" = if ($isIndexable) { "index, follow" } else { "noindex, nofollow" }
    "SOCIAL_CARD_URL" = $socialCardUrl
    "SOFTWARE_APPLICATION_JSON" = $softwareApplicationJson
}

$routes = [ordered]@{
    "index.html" = "index.html"
    "download.html" = "download/index.html"
    "pro-editing.html" = "pro-editing/index.html"
    "photo-editor-windows.html" = "photo-editor-windows/index.html"
    "photo-editor-linux.html" = "photo-editor-linux/index.html"
    "photo-editor-macos.html" = "photo-editor-macos/index.html"
    "import-from-lightroom.html" = "import-from-lightroom/index.html"
    "404.html" = "404.html"
}

foreach ($route in $routes.GetEnumerator()) {
    $pagePath = Join-Path $sourcePath ("pages/" + $route.Key)
    $content = Expand-SitePartials -Content (Get-Content -Raw -LiteralPath $pagePath)
    foreach ($token in $tokens.GetEnumerator()) {
        $content = $content.Replace("{{" + $token.Key + "}}", $token.Value)
    }
    $pageMetadata = switch ($route.Key) {
        "index.html" {
            [ordered]@{
                Title = "Happy Photon — Photo Editing, Simplified"
                Description = "A focused, open-source desktop workflow for browsing, non-destructively editing, and exporting JPEG and RAW photographs."
                CanonicalUrl = $canonicalBase
            }
        }
        "download.html" {
            [ordered]@{
                Title = "Download — Happy Photon"
                Description = "Download Happy Photon for Windows, macOS, or Linux and review platform requirements."
                CanonicalUrl = $canonicalBase + "download/"
            }
        }
        "pro-editing.html" {
            [ordered]@{
                Title = "Professional RAW Photo Editing — Happy Photon"
                Description = "Explore Happy Photon's 16-bit wide-gamut RAW pipeline, perceptual color tools, professional scopes, finishing controls, and color-managed export."
                CanonicalUrl = $canonicalBase + "pro-editing/"
            }
        }
        "photo-editor-windows.html" {
            [ordered]@{
                Title = "Simple RAW and JPEG Photo Editor for Windows — Happy Photon"
                Description = "Browse, non-destructively edit, and export local RAW and JPEG shoots with Happy Photon for Windows x64. Originals stay untouched."
                CanonicalUrl = $canonicalBase + "photo-editor-windows/"
            }
        }
        "photo-editor-linux.html" {
            [ordered]@{
                Title = "Focused RAW and JPEG Photo Editor for Linux — Happy Photon"
                Description = "Use a local, open-source workflow to browse, non-destructively edit, and export RAW and JPEG shoots on Linux x64."
                CanonicalUrl = $canonicalBase + "photo-editor-linux/"
            }
        }
        "photo-editor-macos.html" {
            [ordered]@{
                Title = "RAW and JPEG Photo Editor for Mac — Happy Photon"
                Description = "Browse, non-destructively edit, and export local RAW and JPEG shoots with Happy Photon for Apple Silicon Macs running macOS 14+."
                CanonicalUrl = $canonicalBase + "photo-editor-macos/"
            }
        }
        "import-from-lightroom.html" {
            [ordered]@{
                Title = "Import Ratings and Flags from Lightroom Classic — Happy Photon"
                Description = "Import ratings, pick and reject flags, and color labels from Lightroom Classic into Happy Photon, with optional standard Adobe XMP sidecars."
                CanonicalUrl = $canonicalBase + "import-from-lightroom/"
            }
        }
        default {
            [ordered]@{ Title = ""; Description = ""; CanonicalUrl = "" }
        }
    }
    $content = $content.Replace("{{PAGE_TITLE}}", $pageMetadata.Title)
    $content = $content.Replace("{{PAGE_DESCRIPTION}}", $pageMetadata.Description)
    $content = $content.Replace("{{PAGE_CANONICAL_URL}}", $pageMetadata.CanonicalUrl)
    if ($isIndexable) {
        $content = $content.Replace(" data-production-only", "")
    }
    else {
        $content = [regex]::Replace($content, "(?m)^.*data-production-only.*(?:\r?\n)?", "")
    }
    if ($content -match "\{\{[^}]+\}\}") {
        throw "Unresolved template token in $($route.Key): $($Matches[0])"
    }

    $outputPath = Join-Path $destinationPath $route.Value
    New-Item -ItemType Directory -Path (Split-Path -Parent $outputPath) -Force | Out-Null
    [System.IO.File]::WriteAllText($outputPath, $content.Trim() + "`n", [System.Text.UTF8Encoding]::new($false))
}

Copy-Item -LiteralPath $configPath -Destination (Join-Path $destinationPath "site-config.json")
[System.IO.File]::WriteAllText((Join-Path $destinationPath "downloads.json"), $manifestJson + "`n", [System.Text.UTF8Encoding]::new($false))
if ($isIndexable) {
    $robots = "User-agent: *`nAllow: /`nSitemap: $($canonicalBase)sitemap.xml`n"
    $sitemapEntries = foreach ($outputRoute in $routes.Values) {
        if ($outputRoute -eq "404.html") {
            continue
        }
        $routeSuffix = if ($outputRoute -eq "index.html") { "" } else { $outputRoute.Replace("index.html", "") }
        "  <url><loc>$canonicalBase$routeSuffix</loc></url>"
    }
    $sitemap = @"
<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
$($sitemapEntries -join "`n")
</urlset>
"@
    [System.IO.File]::WriteAllText((Join-Path $destinationPath "sitemap.xml"), $sitemap.Trim() + "`n", [System.Text.UTF8Encoding]::new($false))
}
else {
    $robots = "User-agent: *`nDisallow: /`n"
}
[System.IO.File]::WriteAllText((Join-Path $destinationPath "robots.txt"), $robots, [System.Text.UTF8Encoding]::new($false))

Write-Host "Built Happy Photon site"
Write-Host "  Output: $destinationPath"
Write-Host "  Base path: $normalizedBasePath"
Write-Host "  Origin: $(if ($Origin) { $Origin } else { '(local review)' })"
Write-Host "  Profile: $($config.downloadProfile) / $($config.pagesDeployment)"
Write-Host "  Release data: $ReleaseDataSource / advertising $([bool]$AdvertiseDownloads)"
