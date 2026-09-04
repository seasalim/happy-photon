Describe "Happy Photon release selection" {
    BeforeAll {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        Import-Module (Join-Path $projectRoot "scripts/SiteBuild.psm1") -Force
        function Read-Fixture {
            param([string]$Name)
            Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot "fixtures/releases/$Name.json") | ConvertFrom-Json
        }
    }

    It "selects the newest stable release by default" {
        $fixture = Read-Fixture "stable-public"
        $selection = Select-SiteRelease -Releases @($fixture.releases) -PreferredChannel stable
        $selection.Channel | Should -Be "stable"
        $selection.Release.tag_name | Should -Be "v0.1.0"
    }

    It "labels a preview when no stable release exists" {
        $fixture = Read-Fixture "preview-only"
        $selection = Select-SiteRelease -Releases @($fixture.releases) -PreferredChannel stable
        $selection.Channel | Should -Be "preview"
        $selection.Release.tag_name | Should -Be "v0.2.0-beta.1"
    }

    It "returns no selection when there is no published release" {
        $fixture = Read-Fixture "no-release"
        $selection = Select-SiteRelease -Releases @($fixture.releases) -PreferredChannel stable
        $selection | Should -BeNullOrEmpty
    }
}

Describe "Happy Photon download manifests" {
    BeforeAll {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        Import-Module (Join-Path $projectRoot "scripts/SiteBuild.psm1") -Force
        function Read-Fixture {
            param([string]$Name)
            Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot "fixtures/releases/$Name.json") | ConvertFrom-Json
        }
        function Read-SiteConfig {
            Get-Content -Raw -LiteralPath (Join-Path $projectRoot "site/site-config.json") | ConvertFrom-Json
        }
    }

    It "keeps verified Store metadata non-actionable without release data" {
        $manifest = New-UnavailableSiteManifest -Config (Read-SiteConfig)
        $manifest.selectedChannel | Should -BeNullOrEmpty
        $manifest.platforms.windows.availability | Should -Be "verified"
        $manifest.platforms.windows.note | Should -Be "Microsoft Store · 0.2.4"
        $manifest.platforms.windows.url | Should -BeNullOrEmpty
        $manifest.platforms.macos.availability | Should -Be "unavailable"
        $manifest.platforms.linux.availability | Should -Be "unavailable"
    }

    It "normalizes verified stable release metadata without advertising links" {
        $fixture = Read-Fixture "stable-public"
        $manifest = New-ReleaseSiteManifest -Config (Read-SiteConfig) -Repository $fixture.repository -Releases @($fixture.releases) -PreferredChannel stable -DataSource Fixture
        $manifest.selectedChannel | Should -Be "stable"
        $manifest.release.version | Should -Be "0.1.0"
        $manifest.release.checksumUrl | Should -Match "^https://"
        $manifest.advertising | Should -Be $false
        $manifest.platforms.windows.availability | Should -Be "verified"
        $manifest.platforms.linux.availability | Should -Be "verified"
        $manifest.platforms.macos.url | Should -BeNullOrEmpty
        $manifest.platforms.linux.url | Should -BeNullOrEmpty
    }

    It "keeps the Microsoft Store version independent from the GitHub release" {
        $fixture = Read-Fixture "stable-public"
        $config = Read-SiteConfig
        $config.microsoftStoreVersion = "0.0.9"
        $manifest = New-ReleaseSiteManifest -Config $config -Repository $fixture.repository -Releases @($fixture.releases) -PreferredChannel stable -DataSource Fixture
        $manifest.release.version | Should -Be "0.1.0"
        $manifest.platforms.windows.note | Should -Be "Microsoft Store · 0.0.9"
        $manifest.platforms.macos.note | Should -Be "Signed and notarized ZIP · 0.1.0"
    }

    It "fails closed for a private repository" {
        $fixture = Read-Fixture "private-repository"
        $didThrow = $false
        try {
            New-ReleaseSiteManifest -Config (Read-SiteConfig) -Repository $fixture.repository -Releases @($fixture.releases) -PreferredChannel stable -DataSource Fixture
        }
        catch {
            $didThrow = $true
        }
        $didThrow | Should -Be $true
    }

    It "allows live stable destinations only after every explicit gate" {
        $fixture = Read-Fixture "stable-public"
        $config = Read-SiteConfig
        $config.microsoftStoreUrl = $config.microsoftStorePublicUrlCandidate
        $config.microsoftStoreStatus = "public"
        $config.macosPackageStatus = "verified"
        $manifest = New-ReleaseSiteManifest -Config $config -Repository $fixture.repository -Releases @($fixture.releases) -PreferredChannel stable -AdvertiseDownloads -ProvenanceVerified -DataSource GitHub
        $manifest.platforms.windows.availability | Should -Be "available"
        $manifest.platforms.macos.availability | Should -Be "available"
        $manifest.platforms.linux.availability | Should -Be "available"
        $manifest.platforms.windows.url | Should -Be $config.microsoftStorePublicUrlCandidate
    }

    It "refuses live advertising without public provenance" {
        $fixture = Read-Fixture "stable-public"
        $config = Read-SiteConfig
        $config.microsoftStoreStatus = "public"
        $config.macosPackageStatus = "verified"
        $didThrow = $false
        try {
            New-ReleaseSiteManifest -Config $config -Repository $fixture.repository -Releases @($fixture.releases) -PreferredChannel stable -AdvertiseDownloads -DataSource GitHub
        }
        catch {
            $didThrow = $true
        }
        $didThrow | Should -Be $true
    }
}
