[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [switch] $NoBuild,
    [switch] $NoRestore,
    [switch] $PolicyOnly,
    [switch] $SkipPolicy,
    [switch] $SkipQuarantine,
    [switch] $BlameHang,
    [switch] $SerialProjects,
    # Full runs partition headless classes; filtered and serial runs keep one host.
    [ValidateRange(1, [int]::MaxValue)]
    [int] $HeadlessShards = 4,
    # Override the runsettings processor cap for unsharded test hosts.
    [ValidateRange(1, [int]::MaxValue)]
    [int] $ProcessorCount,
    [string] $BlameHangTimeout = "90s",
    [string] $LogFilePrefix = "",
    [string] $ResultsDirectory = "",
    # A `dotnet test --filter` expression. With it, only the matching tests run
    # (the targeted verification a change needs); the quarantine reconciliation
    # needs the whole suite and is skipped. The full suite runs nightly.
    [string] $Filter = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($PolicyOnly -and $SkipPolicy) {
    throw "PolicyOnly and SkipPolicy cannot be used together."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$useShards = -not $Filter -and -not $SerialProjects -and $HeadlessShards -gt 1
. (Join-Path $PSScriptRoot 'test-processor-budget.ps1')

if (-not $PolicyOnly) {
    $cap = if ($ProcessorCount) { $ProcessorCount } else { 8 }

    if ($useShards) {
        Assert-TestProcessorBudget (@($cap, $cap) + @(2) * $HeadlessShards)
    } else {
        Assert-TestProcessorBudget @($cap, $cap, $cap) -Serial:$SerialProjects
    }

    if ($ProcessorCount) { Assert-TestProcessorBudget @($ProcessorCount) }
}

$clock = [Diagnostics.Stopwatch]::StartNew()
$buildSeconds = 0.0
$policySeconds = 0.0
$testSeconds = 0.0
$phase = "build"
try {
    if (-not $PolicyOnly -and -not $NoBuild) {
        $buildArguments = @("build", (Join-Path $repoRoot "HappyPhoton.sln"), "--configuration", $Configuration)
        if ($NoRestore) { $buildArguments += "--no-restore" }
        & dotnet @buildArguments
        if ($LASTEXITCODE -ne 0) { throw "Solution build failed with exit code $LASTEXITCODE." }
    }
    $buildSeconds = $clock.Elapsed.TotalSeconds
    $clock.Restart()
    $phase = "policy"
    if (-not $SkipPolicy) {
        & (Join-Path $PSScriptRoot "check-source-lines.ps1")
        & (Join-Path $PSScriptRoot "check-test-waits.ps1")
        & (Join-Path $PSScriptRoot "check-test-teardown.ps1")
        & (Join-Path $PSScriptRoot "check-test-teardown-fixtures.ps1")
        & (Join-Path $PSScriptRoot "check-test-sqlite-pools.ps1")
        & (Join-Path $PSScriptRoot "check-test-quarantine-fixtures.ps1")
        & (Join-Path $PSScriptRoot "check-test-ledger.ps1") -Mode Policy
        & (Join-Path $PSScriptRoot "check-test-ledger-fixtures.ps1")
        & (Join-Path $PSScriptRoot "check-test-abort-fixtures.ps1")
        & (Join-Path $PSScriptRoot "check-headless-shards-fixtures.ps1")
        & (Join-Path $PSScriptRoot "check-test-cpu-fixtures.ps1")
    }
    if ($PolicyOnly) { return }

    $resultsRoot = if ($ResultsDirectory) { $ResultsDirectory } else { "artifacts/test-results" }
    if (-not [IO.Path]::IsPathRooted($resultsRoot)) { $resultsRoot = Join-Path $repoRoot $resultsRoot }
    $runDirectory = Join-Path $resultsRoot "verify-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))-$([Guid]::NewGuid().ToString('N'))"
    $null = New-Item -ItemType Directory -Path $runDirectory
    Write-Host "Current-run test evidence: $runDirectory"
    $checker = Join-Path $PSScriptRoot "check-test-quarantine.ps1"

    # Every unfiltered run needs discovery evidence for the pruning ledger.
    if (-not $SkipQuarantine -or -not $Filter) {
        & $checker -Configuration $Configuration -ResultsDirectory $runDirectory
    }

    if (-not $Filter) {
        $interopProject = "Interop/HappyPhoton.LibRaw.Interop.Tests/HappyPhoton.LibRaw.Interop.Tests.csproj"
        $interopAssembly = [IO.Path]::GetFileNameWithoutExtension($interopProject)
        [xml]$interopDefinition = Get-Content -Raw -LiteralPath (Join-Path $repoRoot $interopProject)
        $framework = [string]$interopDefinition.Project.PropertyGroup.TargetFramework
        $binary = Join-Path (Split-Path (Join-Path $repoRoot $interopProject)) "bin/$Configuration/$framework/$interopAssembly.dll"
        $listing = & dotnet $binary -list full/json -noColor 2>&1

        if ($LASTEXITCODE -ne 0) { throw "Interop test discovery failed: $($listing -join "`n")" }

        $listingErrors = @($listing | Where-Object { $_ -is [Management.Automation.ErrorRecord] })

        if ($listingErrors.Count -gt 0) { Write-Warning ($listingErrors -join "`n") }

        $json = ($listing | Where-Object { $_ -isnot [Management.Automation.ErrorRecord] }) -join "`n"
        @{ schemaVersion = 1; project = $interopProject; tests = @($json | ConvertFrom-Json) } |
            ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $runDirectory "manifests/$interopAssembly.manifest.json")
    }

    if ($useShards) {
        . (Join-Path $PSScriptRoot "test-headless-shards.ps1")
        $manifestPath = Join-Path $runDirectory "manifests/HappyPhoton.Headless.Tests.manifest.json"
        $shards = @(Get-HeadlessTestShards $manifestPath $HeadlessShards)
    }

    $policySeconds = $clock.Elapsed.TotalSeconds
    $clock.Restart()
    $phase = "tests"
    $prefix = if ($LogFilePrefix) { $LogFilePrefix } else { "verify" }
    $testArguments = @(
        "--configuration", $Configuration, "--no-build", "--no-restore",
        "--results-directory", $runDirectory)

    if ($BlameHang) {
        $testArguments += @("--blame-hang", "--blame-hang-timeout", $BlameHangTimeout)
    }

    if ($Filter) {
        $testArguments += @("--filter", $Filter)
    }

    if ($useShards) {
        $testExitCode = & (Join-Path $PSScriptRoot "run-test-shards.ps1") -Shards $shards `
            -TestArguments $testArguments -RunDirectory $runDirectory -LogFilePrefix $prefix -ProcessorCount $ProcessorCount
    } else {
        $testArguments += @("--logger", "trx;LogFilePrefix=$prefix")

        if ($PSBoundParameters.ContainsKey("ProcessorCount")) {
            $testArguments += @("--", "RunConfiguration.EnvironmentVariables.DOTNET_PROCESSOR_COUNT=$ProcessorCount")
        }

        $targets = if ($SerialProjects) {
            @("Tests/HappyPhoton.Tests.csproj", "HeadlessTests/HappyPhoton.Headless.Tests.csproj",
                "Interop/HappyPhoton.LibRaw.Interop.Tests/HappyPhoton.LibRaw.Interop.Tests.csproj")
        } else {
            @("HappyPhoton.sln")
        }
        $testExitCode = 0
        $testLog = Join-Path $runDirectory "dotnet-test.log"

        foreach ($target in $targets) {
            Write-Host "Running tests: $target"
            & dotnet test (Join-Path $repoRoot $target) @testArguments 2>&1 | Tee-Object -FilePath $testLog -Append

            if ($LASTEXITCODE -ne 0) { $testExitCode = $LASTEXITCODE }
        }

        & (Join-Path $PSScriptRoot "report-test-abort.ps1") -OutputPath $testLog -ResultsDirectory $runDirectory
    }

    if ((-not $SkipQuarantine -or $useShards) -and -not $Filter) {
        & $checker -Mode StableResults -ResultsDirectory $runDirectory
    }

    if (-not $Filter) {
        & (Join-Path $PSScriptRoot "check-test-ledger.ps1") -Mode Results -ResultsDirectory $runDirectory
    }

    if ($testExitCode -ne 0) { throw "Solution tests failed with exit code $testExitCode." }
} finally {
    switch ($phase) {
        "build" { $buildSeconds = $clock.Elapsed.TotalSeconds }
        "policy" { $policySeconds = $clock.Elapsed.TotalSeconds }
        "tests" { $testSeconds = $clock.Elapsed.TotalSeconds }
    }
    Write-Host ("Verification phases: build {0:F2}s; policy+discovery {1:F2}s; tests+reconciliation {2:F2}s; total {3:F2}s." -f `
        $buildSeconds, $policySeconds, $testSeconds, ($buildSeconds + $policySeconds + $testSeconds))
}
