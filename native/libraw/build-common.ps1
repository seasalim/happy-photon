Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:VcpkgRevision = "c4d9956c0c10a4742840a5e7d93efa2e0015c865"
$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

function Invoke-Checked {
    param([string] $Command, [string[]] $Arguments)
    $commandOutput = @(& $Command @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $commandOutput | Out-Host
    if ($exitCode -ne 0) {
        $details = ($commandOutput | ForEach-Object { $_.ToString() }) -join "`n"
        throw "Command failed with exit code $exitCode`: $Command`n$details"
    }
}

function Invoke-Logged {
    param([string] $Command, [string[]] $Arguments, [string] $LogPath)
    $commandOutput = @(& $Command @Arguments 2>&1 | Tee-Object -FilePath $LogPath)
    $exitCode = $LASTEXITCODE
    $commandOutput | Out-Host
    if ($exitCode -ne 0) {
        $details = ($commandOutput | ForEach-Object { $_.ToString() }) -join "`n"
        throw "Command failed with exit code $exitCode`: $Command`n$details"
    }
}

function Resolve-RepoPath {
    param([string] $Path)
    if ([IO.Path]::IsPathFullyQualified($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    return [IO.Path]::GetFullPath((Join-Path $script:RepoRoot $Path))
}

function Find-SingleFile {
    param([string] $Root, [string] $Name)
    $matches = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter $Name |
        Where-Object { $_.Name -notmatch "_test" })
    if ($matches.Count -ne 1) {
        throw "Expected one $Name under $Root, found $($matches.Count)."
    }
    return $matches[0].FullName
}

function Get-PythonCommand {
    $names = if ($IsWindows) { @("python", "py") } else { @("python3", "python") }
    foreach ($name in $names) {
        if (Get-Command $name -ErrorAction SilentlyContinue) { return $name }
    }
    throw "Python 3 is required."
}

function Assert-VcpkgCheckout {
    param([string] $VcpkgRoot)
    if (-not (Test-Path -LiteralPath (Join-Path $VcpkgRoot ".git"))) {
        throw "VcpkgRoot must be the pinned vcpkg Git checkout."
    }
    $safeRoot = $VcpkgRoot.Replace('\', '/')
    $revision = (& git -c "safe.directory=$safeRoot" -C $VcpkgRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $revision -ne $script:VcpkgRevision) {
        throw "Expected vcpkg $script:VcpkgRevision, observed $revision."
    }
    $executable = if ($IsWindows) { "vcpkg.exe" } else { "vcpkg" }
    $vcpkg = Join-Path $VcpkgRoot $executable
    if (-not (Test-Path -LiteralPath $vcpkg)) {
        $bootstrap = if ($IsWindows) { "bootstrap-vcpkg.bat" } else { "bootstrap-vcpkg.sh" }
        Invoke-Checked (Join-Path $VcpkgRoot $bootstrap) @("-disableMetrics") | Out-Host
    }
    return $vcpkg
}

function Write-BuildOptions {
    param([string] $Path, [string] $Rid, [string] $Triplet,
        [bool] $Reentrant, [bool] $Lcms, [bool] $OpenMp, [string] $PackageVersion)
    $options = [ordered]@{
        rid = $Rid
        candidate = $PackageVersion -ne "0.22.2.0"
        configuration = "Release"
        triplet = $Triplet
        reentrant = $Reentrant
        lcms = $Lcms
        openmp = $OpenMp
        jasper = $false
        dng_lossy = $true
        zlib = if ($Rid -eq "osx-arm64") { "macOS SDK" } else { "vcpkg dynamic" }
        jpeg_linkage = if ($Rid -eq "osx-arm64") { "static" } else { "dynamic" }
        libraw_source = [ordered]@{
            url = "https://www.libraw.org/data/LibRaw-0.22.2.tar.gz"
            sha512 = "9333bc667c8e68a3572c336d3e2ecda82c5987e7feecb6ceb4e1df7dc7291747ffe66f6d3e01b121946ba4e2b1be95295c030d2754a5ae1cd638cffc8213141a"
        }
        libraw_cmake_revision = "eb98e4325aef2ce85d2eb031c2ff18640ca616d3"
        vcpkg_revision = $script:VcpkgRevision
    }
    $options | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Invoke-Probe {
    param([string] $Executable, [string] $Fixture, [switch] $Staged)
    # Assign the array directly: a one-element array returned from an if
    # expression unrolls to a scalar, and splatting a scalar string passes
    # one argument per character.
    [string[]] $arguments = @($Fixture)
    if ($Staged) { $arguments = @("--staged") + $arguments }
    $text = (& $Executable @arguments)
    if ($LASTEXITCODE -ne 0) { throw "Probe failed: $Executable" }
    return ($text -join "`n") | ConvertFrom-Json
}

function Assert-SanitizerInstrumentation {
    param([string] $BuildRoot)
    $testBinaries = @(Get-ChildItem -LiteralPath $BuildRoot -Recurse -File |
        Where-Object { $_.Name -eq "hplr_native_tests" })
    if ($testBinaries.Count -ne 1) {
        throw "Expected one hplr_native_tests binary under $BuildRoot, found $($testBinaries.Count)."
    }
    $nm = Get-Command "nm" -ErrorAction SilentlyContinue
    if ($null -eq $nm) { throw "nm is required to verify sanitizer instrumentation." }
    $symbols = @(& $nm.Source --undefined-only $testBinaries[0].FullName 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $details = ($symbols | ForEach-Object { $_.ToString() }) -join "`n"
        throw "Could not inspect sanitizer symbols in $($testBinaries[0].FullName):`n$details"
    }
    $symbolText = ($symbols | ForEach-Object { $_.ToString() }) -join "`n"
    if ($symbolText -notmatch "__asan") {
        throw "ASan instrumentation was not found in $($testBinaries[0].FullName)."
    }
    if ($symbolText -notmatch "__ubsan") {
        throw "UBSan instrumentation was not found in $($testBinaries[0].FullName)."
    }
    Write-Host "Verified ASan and UBSan instrumentation in $($testBinaries[0].FullName)."
}

function Invoke-LibRawNativeBuild {
    param(
        [Parameter(Mandatory)] [string] $Rid,
        [Parameter(Mandatory)] [string] $Triplet,
        [Parameter(Mandatory)] [string] $VcpkgRoot,
        [Parameter(Mandatory)] [string] $OutputRoot,
        [Parameter(Mandatory)] [string] $BuildRoot,
        [Parameter(Mandatory)] [string] $PackageVersion,
        [bool] $Reentrant,
        [bool] $Lcms,
        [bool] $OpenMp,
        [switch] $Sanitizers)

    $vcpkg = Assert-VcpkgCheckout $VcpkgRoot
    if ($PackageVersion -notmatch '^0\.22\.2\.(0|[1-9][0-9]*)$') {
        throw "PackageVersion must be 0.22.2.0 for developer output or a unique 0.22.2.N candidate."
    }
    $python = Get-PythonCommand
    $output = Resolve-RepoPath $OutputRoot
    $work = Resolve-RepoPath $BuildRoot
    if (Test-Path -LiteralPath $output) {
        throw "OutputRoot already exists; use a fresh directory: $output"
    }
    New-Item -ItemType Directory -Path $output | Out-Null
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $validation = New-Item -ItemType Directory -Path (Join-Path $output "validation")
    $installed = Join-Path $work "vcpkg-installed"
    $bridgeBuild = Join-Path $work "bridge"
    $features = if ($OpenMp) { "libraw[dng-lossy,openmp]:$Triplet" } else { "libraw[dng-lossy]:$Triplet" }
    $vcpkgArguments = @("install", $features,
        "--overlay-ports=$(Join-Path $PSScriptRoot 'ports')",
        "--overlay-triplets=$(Join-Path $PSScriptRoot 'triplets')",
        "--x-install-root=$installed", "--disable-metrics")
    Invoke-Logged $vcpkg $vcpkgArguments (Join-Path $validation "vcpkg.log")

    $cmakeArguments = @("-S", (Join-Path $PSScriptRoot "bridge"), "-B", $bridgeBuild,
        "-G", "Ninja", "-DCMAKE_BUILD_TYPE=Release",
        "-DCMAKE_TOOLCHAIN_FILE=$(Join-Path $VcpkgRoot 'scripts/buildsystems/vcpkg.cmake')",
        "-DVCPKG_TARGET_TRIPLET=$Triplet", "-DVCPKG_INSTALLED_DIR=$installed",
        "-DVCPKG_MANIFEST_MODE=OFF", "-DHPLR_BUILD_TESTS=ON",
        "-DHPLR_USE_REENTRANT=$(if ($Reentrant) {'ON'} else {'OFF'})",
        "-DHPLR_EXPECT_LCMS=$(if ($Lcms) {'ON'} else {'OFF'})",
        "-DHPLR_EXPECT_OPENMP=$(if ($OpenMp) {'ON'} else {'OFF'})")
    if ($Sanitizers) {
        $flags = "-fsanitize=address,undefined -fno-omit-frame-pointer"
        $cmakeArguments += "-DCMAKE_C_FLAGS=$flags", "-DCMAKE_CXX_FLAGS=$flags",
            "-DCMAKE_EXE_LINKER_FLAGS=-fsanitize=address,undefined",
            "-DCMAKE_SHARED_LINKER_FLAGS=-fsanitize=address,undefined"
    }
    Invoke-Logged "cmake" $cmakeArguments (Join-Path $validation "configure.log")
    Invoke-Logged "cmake" @("--build", $bridgeBuild, "--config", "Release") `
        (Join-Path $validation "build.log")
    if ($Sanitizers) { Assert-SanitizerInstrumentation $bridgeBuild }
    Invoke-Logged "ctest" @("--test-dir", $bridgeBuild, "-C", "Release",
        "--output-on-failure") (Join-Path $validation "ctest.log")
    if ($Sanitizers) { return }

    $baselineInstalled = Join-Path $work "baseline-vcpkg-installed"
    $baselineBuild = Join-Path $work "baseline-performance"
    $baselineFeatures = if ($OpenMp) {
        "libraw[dng-lossy,openmp]:$Triplet"
    } else {
        "libraw[dng-lossy]:$Triplet"
    }
    Invoke-Logged $vcpkg @("install", $baselineFeatures,
        "--overlay-ports=$(Join-Path $PSScriptRoot 'oracle/ports')",
        "--overlay-triplets=$(Join-Path $PSScriptRoot 'triplets')",
        "--x-install-root=$baselineInstalled", "--disable-metrics") `
        (Join-Path $validation "baseline-vcpkg.log")
    Invoke-Logged "cmake" @("-S", (Join-Path $PSScriptRoot "oracle"),
        "-B", $baselineBuild, "-G", "Ninja", "-DCMAKE_BUILD_TYPE=Release",
        "-DCMAKE_TOOLCHAIN_FILE=$(Join-Path $VcpkgRoot 'scripts/buildsystems/vcpkg.cmake')",
        "-DVCPKG_TARGET_TRIPLET=$Triplet", "-DVCPKG_INSTALLED_DIR=$baselineInstalled",
        "-DVCPKG_MANIFEST_MODE=OFF", "-DHPLR_BUILD_PARITY_ORACLE=OFF",
        "-DHPLR_USE_REENTRANT=$(if ($Reentrant) {'ON'} else {'OFF'})") `
        (Join-Path $validation "baseline-performance-configure.log")
    Invoke-Logged "cmake" @("--build", $baselineBuild, "--config", "Release") `
        (Join-Path $validation "baseline-performance-build.log")
    $extension = if ($IsWindows) { ".exe" } else { "" }
    $performanceTools = New-Item -ItemType Directory -Path `
        (Join-Path $validation "native-performance")
    $baselinePerformance = Join-Path $performanceTools "hplr_baseline_performance$extension"
    $candidatePerformance = Join-Path $performanceTools "hplr_candidate_performance$extension"
    Copy-Item -LiteralPath (Find-SingleFile $baselineBuild `
        "hplr_baseline_performance$extension") -Destination $baselinePerformance
    Copy-Item -LiteralPath (Find-SingleFile $bridgeBuild `
        "hplr_candidate_performance$extension") -Destination $candidatePerformance
    if ($IsMacOS) {
        $prepareMacExecutable = Join-Path $PSScriptRoot "prepare_macos_executable.py"
        Invoke-Checked $python @($prepareMacExecutable, "--canonical-libraw",
            "libraw.23.dylib", "--executable", $baselinePerformance)
        Invoke-Checked $python @($prepareMacExecutable, "--canonical-libraw",
            "libraw.25.dylib", "--executable", $candidatePerformance)
    }

    $runtime = Join-Path $output "runtime"
    Invoke-Checked $python @((Join-Path $PSScriptRoot "stage_runtime.py"),
        "--rid", $Rid, "--build-dir", $bridgeBuild, "--installed-dir",
        (Join-Path $installed $Triplet), "--output", $runtime)
    $fixture = Join-Path $script:RepoRoot "Tests/assets/canon-eos-350d.cr2"
    $candidate = Find-SingleFile $bridgeBuild "hplr_candidate_smoke$extension"
    $feature = Find-SingleFile $bridgeBuild "hplr_feature_probe$extension"
    $candidateCopy = Join-Path $runtime "hplr_candidate_smoke$extension"
    $featureCopy = Join-Path $runtime "hplr_feature_probe$extension"
    try {
        Copy-Item -LiteralPath $candidate -Destination $candidateCopy
        Copy-Item -LiteralPath $feature -Destination $featureCopy
        if ($IsMacOS) {
            Invoke-Checked $python @((Join-Path $PSScriptRoot "prepare_macos_executable.py"),
                "--canonical-libraw", "libraw.25.dylib",
                "--executable", $candidateCopy, "--executable", $featureCopy)
        }
        $runtimeProbe = Invoke-Probe $candidateCopy $fixture -Staged
        $runtimeProbe | ConvertTo-Json -Compress | Set-Content `
            (Join-Path $validation "runtime-probe.json") -Encoding utf8
        $featureProbe = Invoke-Probe $featureCopy $fixture
        $featureProbe | ConvertTo-Json -Compress | Set-Content `
            (Join-Path $validation "feature-probe.json") -Encoding utf8
        if ($OpenMp) {
            $oldThreads, $oldDynamic = $env:OMP_NUM_THREADS, $env:OMP_DYNAMIC
            try {
                $env:OMP_DYNAMIC = "FALSE"
                $env:OMP_NUM_THREADS = "1"
                $constrained = Invoke-Probe $featureCopy $fixture
                $env:OMP_NUM_THREADS = [Math]::Max(2, [Environment]::ProcessorCount).ToString()
                $parallel = Invoke-Probe $featureCopy $fixture
            } finally {
                $env:OMP_NUM_THREADS, $env:OMP_DYNAMIC = $oldThreads, $oldDynamic
            }
            $comparison = [ordered]@{ enabled = $true; constrained = $constrained; parallel = $parallel }
        } else {
            $comparison = [ordered]@{ enabled = $false; reason = "OpenMP is forbidden on macOS" }
        }
        $comparison | ConvertTo-Json -Depth 4 | Set-Content `
            (Join-Path $validation "thread-comparison.json") -Encoding utf8
    } finally {
        Remove-Item -LiteralPath $candidateCopy, $featureCopy -Force -ErrorAction SilentlyContinue
    }

    $buildOptions = Join-Path $output "build-options.json"
    Write-BuildOptions $buildOptions $Rid $Triplet $Reentrant $Lcms $OpenMp $PackageVersion
    Invoke-Checked $python @((Join-Path $PSScriptRoot "validate_runtime.py"),
        "--rid", $Rid, "--runtime-dir", $runtime,
        "--runtime-probe", (Join-Path $validation "runtime-probe.json"),
        "--feature-probe", (Join-Path $validation "feature-probe.json"),
        "--thread-comparison", (Join-Path $validation "thread-comparison.json"),
        "--build-options", $buildOptions,
        "--output", (Join-Path $validation "validation-report.json"))
    $sourceCommit = (& git -C $script:RepoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Could not resolve the source commit." }
    Invoke-Checked $python @((Join-Path $PSScriptRoot "collect_provenance.py"),
        "--rid", $Rid, "--package-version", $PackageVersion,
        "--source-commit", $sourceCommit, "--vcpkg-root", $VcpkgRoot,
        "--installed-dir", (Join-Path $installed $Triplet),
        "--artifact-dir", $output, "--output", (Join-Path $output "provenance.json"))
}
