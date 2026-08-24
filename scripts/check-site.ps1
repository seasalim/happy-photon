[CmdletBinding()]
param(
    [string]$SiteRoot,
    [switch]$AllowFixture,
    [switch]$AllowReviewAdvertising
)

$ErrorActionPreference = "Stop"
$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if (-not $SiteRoot) {
    $SiteRoot = Join-Path $projectRoot "artifacts/site"
}
$sitePath = [System.IO.Path]::GetFullPath($SiteRoot)

foreach ($requiredFile in @(
    "index.html",
    "download/index.html",
    "pro-editing/index.html",
    "photo-editor-windows/index.html",
    "photo-editor-linux/index.html",
    "photo-editor-macos/index.html",
    "import-from-lightroom/index.html",
    "404.html",
    "site-config.json",
    "downloads.json",
    "robots.txt"
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $sitePath $requiredFile) -PathType Leaf)) {
        throw "Missing staged site file: $requiredFile"
    }
}

$htmlFiles = @(Get-ChildItem -LiteralPath $sitePath -Filter "*.html" -Recurse)
$allFiles = @(Get-ChildItem -LiteralPath $sitePath -File -Recurse)
$stagedPaths = @{}
foreach ($file in $allFiles) {
    $relativePath = [System.IO.Path]::GetRelativePath($sitePath, $file.FullName).Replace("\", "/")
    $stagedPaths[$relativePath] = $file.FullName
}
$htmlByPath = @{}
foreach ($htmlFile in $htmlFiles) {
    $content = Get-Content -Raw -LiteralPath $htmlFile.FullName
    $htmlRelativePath = [System.IO.Path]::GetRelativePath($sitePath, $htmlFile.FullName).Replace("\", "/")
    $htmlByPath[$htmlRelativePath] = $content
    foreach ($requiredPattern in @("<main", "<header", "<footer", "Skip to content", "<title>")) {
        if ($content -notmatch [regex]::Escape($requiredPattern)) {
            throw "$($htmlFile.Name) is missing required markup: $requiredPattern"
        }
    }
    foreach ($forbiddenPattern in @('href="#"', "http://", "cdn.", "fonts.googleapis.com", "\{\{")) {
        if ($content -match $forbiddenPattern) {
            throw "$($htmlFile.Name) contains forbidden content matching: $forbiddenPattern"
        }
    }

    $ids = @([regex]::Matches($content, '\bid="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    $duplicateIds = @($ids | Group-Object | Where-Object Count -gt 1)
    if ($duplicateIds) {
        throw "$($htmlFile.Name) contains duplicate IDs: $($duplicateIds.Name -join ', ')"
    }

    $localUrls = @([regex]::Matches($content, '(?:href|src)="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    $srcsetUrls = @([regex]::Matches($content, 'srcset="([^"]+)"') | ForEach-Object {
        $_.Groups[1].Value.Split(",") | ForEach-Object { $_.Trim().Split(" ")[0] }
    })
    foreach ($url in @($localUrls + $srcsetUrls)) {
        if ($url -match '^(?:https://|mailto:|ms-windows-store:)' -or $url.StartsWith("#")) {
            continue
        }
        $pathOnly = $url.Split("#")[0].Split("?")[0]
        if (-not $pathOnly) {
            continue
        }
        $decodedPath = [Uri]::UnescapeDataString($pathOnly)
        if ($decodedPath.StartsWith("/happy-photon/")) {
            $decodedPath = $decodedPath.Substring("/happy-photon/".Length)
        }
        elseif ($decodedPath.StartsWith("/")) {
            $decodedPath = $decodedPath.Substring(1)
        }
        else {
            $decodedPath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $htmlFile.FullName) $decodedPath))
            if (-not $decodedPath.StartsWith($sitePath + [System.IO.Path]::DirectorySeparatorChar)) {
                throw "$htmlRelativePath contains a local URL outside the staged site: $url"
            }
            $decodedPath = [System.IO.Path]::GetRelativePath($sitePath, $decodedPath).Replace("\", "/")
        }
        if (-not $decodedPath -or $decodedPath.EndsWith("/")) {
            $decodedPath += "index.html"
        }
        if (-not $stagedPaths.ContainsKey($decodedPath)) {
            throw "$htmlRelativePath references a missing or incorrectly cased local file: $url"
        }
    }
}

$stylesheetFiles = @(Get-ChildItem -LiteralPath (Join-Path $sitePath "assets/css") -Filter "site.*.css")
if ($stylesheetFiles.Count -ne 1) {
    throw "Expected exactly one hashed stylesheet, found $($stylesheetFiles.Count)."
}
$stylesheetRelativePath = [System.IO.Path]::GetRelativePath($sitePath, $stylesheetFiles[0].FullName).Replace("\", "/")
foreach ($htmlEntry in $htmlByPath.GetEnumerator()) {
    if ($htmlEntry.Value -notmatch [regex]::Escape($stylesheetRelativePath)) {
        throw "$($htmlEntry.Key) does not reference the current hashed stylesheet."
    }
    if ($htmlEntry.Value -match 'assets/css/(?:tokens|base|layout|components|pages)\.css') {
        throw "$($htmlEntry.Key) references an unhashed source stylesheet."
    }
    foreach ($scriptReference in @([regex]::Matches($htmlEntry.Value, 'src="[^"?]+\.js([^\"]*)"'))) {
        if ($scriptReference.Groups[1].Value -notmatch '^\?v=[0-9a-f]{12}$') {
            throw "$($htmlEntry.Key) references an unversioned JavaScript module."
        }
    }
}

$downloadsScript = Get-Content -Raw -LiteralPath (Join-Path $sitePath "assets/js/downloads.js")
foreach ($versionedDependency in @('platform\.js\?v=[0-9a-f]{12}', 'downloads\.json\?v=[0-9a-f]{12}')) {
    if ($downloadsScript -notmatch $versionedDependency) {
        throw "The staged download module contains an unversioned dependency."
    }
}

$config = Get-Content -Raw -LiteralPath (Join-Path $sitePath "site-config.json") | ConvertFrom-Json
$statePair = "$($config.pagesDeployment)+$($config.downloadProfile)"
$validStatePairs = @("disabled+predownload", "disabled+verify", "project+verify", "project+live", "custom+live")
if ($statePair -notin $validStatePairs) {
    throw "Unsupported staged deployment state: $statePair"
}
if ($config.microsoftStoreProductId -notmatch "^[A-Z0-9]{12}$") {
    throw "The staged Microsoft Store product ID is malformed."
}
if ($config.microsoftStoreVersion -notmatch "^\d+\.\d+\.\d+$") {
    throw "The staged Microsoft Store version is malformed."
}
$expectedStoreCandidate = "https://apps.microsoft.com/detail/$($config.microsoftStoreProductId)"
if ($config.microsoftStorePublicUrlCandidate -ne $expectedStoreCandidate) {
    throw "The staged Microsoft Store candidate URL does not match its product ID."
}
if ($config.microsoftStoreUrl -ne $expectedStoreCandidate -or $config.microsoftStoreStatus -ne "public") {
    throw "The verified public Microsoft Store configuration is inconsistent."
}
$expectedStoreDeepLink = "ms-windows-store://pdp/?productid=$($config.microsoftStoreProductId)"
if ($config.microsoftStoreDeepLink -ne $expectedStoreDeepLink) {
    throw "The Microsoft Store deep link does not match its product ID."
}
if ($config.macosPackageStatus -notin @("pending-signing", "verified")) {
    throw "The macOS package status is invalid."
}
if ($config.releaseChannel -ne "stable") {
    throw "The launch release channel must remain stable."
}
if ((Get-Content -Raw -LiteralPath (Join-Path $sitePath "site-config.json")) -match "/detail/restricted/") {
    throw "Restricted Microsoft Store links must not be staged for the public site."
}
$configJson = Get-Content -Raw -LiteralPath (Join-Path $sitePath "site-config.json")
if ($configJson -match '"[^"\r\n]*(?:token|secret|password|credential|api[-_]?key)[^"\r\n]*"\s*:') {
    throw "Public site configuration contains a credential-like key."
}

$manifest = Get-Content -Raw -LiteralPath (Join-Path $sitePath "downloads.json") | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or -not $manifest.platforms) {
    throw "The staged download manifest has an unsupported schema."
}
if ($manifest.profile -ne $config.downloadProfile) {
    throw "The staged download manifest does not match the committed profile."
}
$isReviewAdvertising = [bool]$AllowReviewAdvertising -and $config.pagesDeployment -eq "disabled" -and $manifest.dataSource -eq "GitHub" -and [bool]$manifest.advertising
if (-not ($AllowFixture -and $manifest.dataSource -eq "Fixture") -and -not $isReviewAdvertising) {
    $expectedDataSource = if ($config.downloadProfile -eq "predownload") { "Unavailable" } else { "GitHub" }
    $expectedAdvertising = $config.downloadProfile -eq "live"
    if ($manifest.dataSource -ne $expectedDataSource -or [bool]$manifest.advertising -ne $expectedAdvertising) {
        throw "The staged download manifest does not match the committed release-data state."
    }
}
foreach ($platformId in @("windows", "macos", "linux")) {
    $platform = $manifest.platforms.$platformId
    if (-not $platform -or $platform.id -ne $platformId) {
        throw "The staged download manifest is missing platform '$platformId'."
    }
    if ($platform.availability -eq "available" -and $platform.url -notmatch "^https://") {
        throw "Available platform '$platformId' does not have a valid HTTPS destination."
    }
}

$isLive = $config.pagesDeployment -eq "custom" -and $config.downloadProfile -eq "live"
$requiresLiveDestinations = ($config.downloadProfile -eq "live" -and [bool]$manifest.advertising) -or $isReviewAdvertising
$indexHtml = Get-Content -Raw -LiteralPath (Join-Path $sitePath "index.html")
$robots = Get-Content -Raw -LiteralPath (Join-Path $sitePath "robots.txt")
if ($isLive) {
    if (-not (Test-Path -LiteralPath (Join-Path $sitePath "sitemap.xml"))) {
        throw "Live site is missing sitemap.xml."
    }
    foreach ($requiredMetadata in @('rel="canonical"', 'property="og:image"', 'name="twitter:card"', 'application/ld+json')) {
        if ($indexHtml -notmatch [regex]::Escape($requiredMetadata)) {
            throw "Live home page is missing production metadata: $requiredMetadata"
        }
    }
    if ($robots -notmatch "Allow: /") {
        throw "Live robots.txt does not allow indexing."
    }
    $sitemap = Get-Content -Raw -LiteralPath (Join-Path $sitePath "sitemap.xml")
    foreach ($guideRoute in @("pro-editing/", "photo-editor-windows/", "photo-editor-linux/", "photo-editor-macos/", "import-from-lightroom/")) {
        $guidePath = $guideRoute + "index.html"
        $guideHtml = $htmlByPath[$guidePath]
        foreach ($requiredMetadata in @('rel="canonical"', 'property="og:title"', 'name="twitter:description"')) {
            if ($guideHtml -notmatch [regex]::Escape($requiredMetadata)) {
                throw "$guidePath is missing production metadata: $requiredMetadata"
            }
        }
        $titleMatch = [regex]::Match($guideHtml, '<title>\s*([^<]+?)\s*</title>', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        $descriptionMatch = [regex]::Match($guideHtml, '<meta\s+name="description"\s+content="([^"]+)"', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        $canonicalMatch = [regex]::Match($guideHtml, '<link\s+rel="canonical"\s+href="([^"]+)"', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if (-not $titleMatch.Success -or [string]::IsNullOrWhiteSpace($titleMatch.Groups[1].Value)) {
            throw "$guidePath has an empty page title."
        }
        if (-not $descriptionMatch.Success -or [string]::IsNullOrWhiteSpace($descriptionMatch.Groups[1].Value)) {
            throw "$guidePath has an empty meta description."
        }
        if (-not $canonicalMatch.Success -or $canonicalMatch.Groups[1].Value -notmatch ([regex]::Escape("/$guideRoute") + '$')) {
            throw "$guidePath canonical URL does not match its route."
        }
        if ($sitemap -notmatch [regex]::Escape("/$guideRoute</loc>")) {
            throw "Live sitemap is missing guide route: $guideRoute"
        }
    }
}
else {
    if (Test-Path -LiteralPath (Join-Path $sitePath "sitemap.xml")) {
        throw "Non-live site must not contain sitemap.xml."
    }
    if ($indexHtml -match 'rel="canonical"' -or $indexHtml -notmatch 'content="noindex, nofollow"') {
        throw "Non-live site indexing metadata is unsafe."
    }
}

if ($requiresLiveDestinations) {
    if (-not $manifest.release -or $manifest.release.notesUrl -notmatch "^https://" -or $manifest.release.checksumUrl -notmatch "^https://" -or $manifest.release.provenanceUrl -notmatch "^https://") {
        throw "Download advertising is missing verified release notes, checksums, or provenance metadata."
    }
    foreach ($platformId in @("windows", "macos", "linux")) {
        if ($manifest.platforms.$platformId.availability -ne "available") {
            throw "Download advertising cannot continue while platform '$platformId' is unavailable."
        }
    }
}

Write-Host "Validated $($htmlFiles.Count) pages, local assets, metadata, and the committed launch configuration."
