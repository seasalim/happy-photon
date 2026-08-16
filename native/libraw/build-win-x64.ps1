param(
    [Parameter(Mandatory)] [string] $VcpkgRoot,
    [Parameter(Mandatory)] [string] $PackageVersion,
    [string] $OutputRoot = "artifacts/libraw/win-x64",
    [string] $BuildRoot = "artifacts/libraw-work/win-x64")

. (Join-Path $PSScriptRoot "build-common.ps1")
Invoke-LibRawNativeBuild -Rid win-x64 -Triplet x64-windows-hplr `
    -VcpkgRoot $VcpkgRoot -OutputRoot $OutputRoot -BuildRoot $BuildRoot `
    -PackageVersion $PackageVersion `
    -Reentrant $true -Lcms $true -OpenMp $true
