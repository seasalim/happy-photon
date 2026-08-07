Set-StrictMode -Version Latest

function New-PlatformRecord {
    param(
        [string]$Id,
        [string]$Label,
        [string]$Architecture,
        [string]$MinimumOs,
        [string]$Availability,
        [string]$Reason,
        [string]$Note,
        [string]$ActionLabel,
        [AllowNull()] [string]$Url,
        [AllowNull()] [string]$PackageType,
        [AllowNull()] [long]$Size,
        [string]$Channel = "stable",
        [string]$Detail = ""
    )

    [ordered]@{
        id = $Id
        label = $Label
        architecture = $Architecture
        minimumOs = $MinimumOs
        availability = $Availability
        reason = $Reason
        note = $Note
        actionLabel = $ActionLabel
        url = $Url
        packageType = $PackageType
        size = $Size
        channel = $Channel
        detail = $Detail
    }
}

function New-UnavailableSiteManifest {
    param(
        [Parameter(Mandatory)] [pscustomobject]$Config
    )

    $storeVerified = $Config.microsoftStoreStatus -eq "public" -and $Config.microsoftStoreUrl -match "^https://apps\.microsoft\.com/detail/[A-Z0-9]{12}$"
    [ordered]@{
        schemaVersion = 1
        profile = [string]$Config.downloadProfile
        dataSource = "Unavailable"
        advertising = $false
        selectedChannel = $null
        release = $null
        generatedAt = [DateTimeOffset]::UtcNow.ToString("o")
        platforms = [ordered]@{
            windows = New-PlatformRecord -Id "windows" -Label "Windows" -Architecture $Config.platforms.windows.architecture -MinimumOs $Config.platforms.windows.minimumOs -Availability $(if ($storeVerified) { "verified" } else { "unavailable" }) -Reason $(if ($storeVerified) { "The public Microsoft Store listing is verified; download advertising remains off until final launch review." } else { "The Microsoft Store release is completing certification." }) -Note $(if ($storeVerified) { "Microsoft Store · $($Config.microsoftStoreVersion)" } else { "Store link coming after public certification" }) -ActionLabel "Get Happy Photon for Windows" -Url $null -PackageType "Microsoft Store" -Size 0
            macos = New-PlatformRecord -Id "macos" -Label "macOS" -Architecture $Config.platforms.macos.architecture -MinimumOs $Config.platforms.macos.minimumOs -Availability "unavailable" -Reason "The macOS package still needs Developer ID signing and notarization." -Note "Signed public ZIP follows verification" -ActionLabel "Download for macOS — Apple Silicon" -Url $null -PackageType "ZIP" -Size 0
            linux = New-PlatformRecord -Id "linux" -Label "Linux" -Architecture $Config.platforms.linux.architecture -MinimumOs $Config.platforms.linux.minimumOs -Availability "unavailable" -Reason "The Linux archive will arrive with the first public GitHub release." -Note "Public tar.gz archive coming at launch" -ActionLabel "Download for Linux — x64" -Url $null -PackageType "tar.gz" -Size 0
        }
    }
}

function Select-SiteRelease {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]]$Releases,
        [ValidateSet("stable", "preview")] [string]$PreferredChannel = "stable"
    )

    $published = @($Releases | Where-Object { -not $_.draft -and $_.published_at })
    $stable = @($published | Where-Object { -not $_.prerelease } | Sort-Object { [DateTimeOffset]$_.published_at } -Descending)
    $preview = @($published | Where-Object { $_.prerelease } | Sort-Object { [DateTimeOffset]$_.published_at } -Descending)

    if ($PreferredChannel -eq "preview" -and $preview.Count -gt 0) {
        return [pscustomobject]@{ Channel = "preview"; Release = $preview[0] }
    }
    if ($stable.Count -gt 0) {
        return [pscustomobject]@{ Channel = "stable"; Release = $stable[0] }
    }
    if ($preview.Count -gt 0) {
        return [pscustomobject]@{ Channel = "preview"; Release = $preview[0] }
    }
    return $null
}

function Find-ReleaseAsset {
    param(
        [Parameter(Mandatory)] [object[]]$Assets,
        [Parameter(Mandatory)] [string]$ExpectedName
    )

    $matches = @($Assets | Where-Object { $_.name -ceq $ExpectedName })
    if ($matches.Count -ne 1) {
        return $null
    }
    $asset = $matches[0]
    if ($asset.browser_download_url -notmatch "^https://") {
        throw "Release asset '$ExpectedName' does not have a valid HTTPS URL."
    }
    return $asset
}

function New-ReleaseSiteManifest {
    param(
        [Parameter(Mandatory)] [pscustomobject]$Config,
        [Parameter(Mandatory)] [pscustomobject]$Repository,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]]$Releases,
        [ValidateSet("stable", "preview")] [string]$PreferredChannel = "stable",
        [switch]$AdvertiseDownloads,
        [switch]$ProvenanceVerified,
        [ValidateSet("Fixture", "GitHub")] [string]$DataSource = "Fixture"
    )

    if ([bool]$Repository.private -or [string]$Repository.visibility -ne "public") {
        throw "GitHub release data is usable only after repository visibility is confirmed public."
    }

    $selection = Select-SiteRelease -Releases $Releases -PreferredChannel $PreferredChannel
    if (-not $selection) {
        $manifest = New-UnavailableSiteManifest -Config $Config
        $manifest.dataSource = $DataSource
        return $manifest
    }

    $release = $selection.Release
    $version = ([string]$release.tag_name).TrimStart("v")
    if ($version -notmatch "^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$") {
        throw "Selected release tag is not a supported semantic version: $($release.tag_name)"
    }

    $macName = "happy-photon-$version-osx-arm64.zip"
    $linuxName = "happy-photon-$version-linux-x64.tar.gz"
    $checksumName = "SHA256SUMS.txt"
    $macAsset = Find-ReleaseAsset -Assets @($release.assets) -ExpectedName $macName
    $linuxAsset = Find-ReleaseAsset -Assets @($release.assets) -ExpectedName $linuxName
    $checksumAsset = Find-ReleaseAsset -Assets @($release.assets) -ExpectedName $checksumName
    if (-not $macAsset -or -not $linuxAsset -or -not $checksumAsset) {
        throw "Selected release is missing one or more required public assets."
    }

    $advertising = [bool]$AdvertiseDownloads
    if ($advertising -and -not $ProvenanceVerified) {
        throw "Live download advertising requires verified public build-provenance attestations."
    }
    $macVerified = $Config.macosPackageStatus -eq "verified"
    $storeAvailable = $Config.microsoftStoreStatus -eq "public" -and $Config.microsoftStoreUrl -match "^https://apps\.microsoft\.com/detail/[A-Z0-9]{12}$"
    $macAvailable = $advertising -and $macVerified
    $linuxAvailable = $advertising
    $channel = [string]$selection.Channel

    $windowsReason = if (-not $storeAvailable) { "The Microsoft Store listing has not yet been verified as public." } elseif (-not $advertising) { "The public Store listing is verified; download advertising remains off until final launch review." } else { "" }
    $macReason = if ($macAvailable) { "" } elseif (-not $macVerified) { "The selected macOS package has not yet passed signing and notarization verification." } else { "The signed macOS release is verified; download advertising remains off until final launch review." }
    $linuxReason = if ($linuxAvailable) { "" } else { "The public Linux release is verified; download advertising remains off until final launch review." }

    [ordered]@{
        schemaVersion = 1
        profile = [string]$Config.downloadProfile
        dataSource = $DataSource
        advertising = $advertising
        selectedChannel = $channel
        release = [ordered]@{
            version = $version
            tag = [string]$release.tag_name
            publishedAt = ([DateTimeOffset]$release.published_at).ToString("o")
            notesUrl = [string]$release.html_url
            checksumUrl = [string]$checksumAsset.browser_download_url
            provenanceUrl = if ($ProvenanceVerified) { "$($Config.repositoryUrl)/attestations" } else { $null }
            sourceReleaseId = [string]$release.id
        }
        generatedAt = [DateTimeOffset]::UtcNow.ToString("o")
        platforms = [ordered]@{
            windows = New-PlatformRecord -Id "windows" -Label "Windows" -Architecture $Config.platforms.windows.architecture -MinimumOs $Config.platforms.windows.minimumOs -Availability $(if ($storeAvailable) { if ($advertising) { "available" } else { "verified" } } else { "unavailable" }) -Reason $windowsReason -Note "Microsoft Store · $($Config.microsoftStoreVersion)" -ActionLabel "Get Happy Photon for Windows" -Url $(if ($storeAvailable -and $advertising) { $Config.microsoftStoreUrl } else { $null }) -PackageType "Microsoft Store" -Size 0 -Channel $channel -Detail "Install the Microsoft-signed x64 release from the Store."
            macos = New-PlatformRecord -Id "macos" -Label "macOS" -Architecture $Config.platforms.macos.architecture -MinimumOs $Config.platforms.macos.minimumOs -Availability $(if ($macVerified) { if ($advertising) { "available" } else { "verified" } } else { "unavailable" }) -Reason $macReason -Note "Signed and notarized ZIP · $version" -ActionLabel "Download for macOS — Apple Silicon" -Url $(if ($macAvailable) { $macAsset.browser_download_url } else { $null }) -PackageType "ZIP" -Size $macAsset.size -Channel $channel -Detail "For Apple Silicon Macs running macOS 14 or later. Intel Macs are not supported."
            linux = New-PlatformRecord -Id "linux" -Label "Linux" -Architecture $Config.platforms.linux.architecture -MinimumOs $Config.platforms.linux.minimumOs -Availability $(if ($advertising) { "available" } else { "verified" }) -Reason $linuxReason -Note "Portable tar.gz · $version" -ActionLabel "Download for Linux — x64" -Url $(if ($linuxAvailable) { $linuxAsset.browser_download_url } else { $null }) -PackageType "tar.gz" -Size $linuxAsset.size -Channel $channel -Detail "Portable x64 archive for Linux; desktop integration is not yet a native package."
        }
    }
}

Export-ModuleMember -Function New-UnavailableSiteManifest, Select-SiteRelease, Find-ReleaseAsset, New-ReleaseSiteManifest
