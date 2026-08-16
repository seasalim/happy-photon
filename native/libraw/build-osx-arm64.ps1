param(
    [Parameter(Mandatory)] [string] $VcpkgRoot,
    [Parameter(Mandatory)] [string] $PackageVersion,
    [string] $OutputRoot = "artifacts/libraw/osx-arm64",
    [string] $BuildRoot = "artifacts/libraw-work/osx-arm64")

. (Join-Path $PSScriptRoot "build-common.ps1")
Invoke-LibRawNativeBuild -Rid osx-arm64 -Triplet arm64-osx-hplr `
    -VcpkgRoot $VcpkgRoot -OutputRoot $OutputRoot -BuildRoot $BuildRoot `
    -PackageVersion $PackageVersion `
    -Reentrant $false -Lcms $false -OpenMp $false
