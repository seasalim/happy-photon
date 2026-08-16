param(
    [Parameter(Mandatory)] [string] $VcpkgRoot,
    [Parameter(Mandatory)] [string] $PackageVersion,
    [string] $OutputRoot = "artifacts/libraw/linux-x64",
    [string] $BuildRoot = "artifacts/libraw-work/linux-x64",
    [switch] $Sanitizers)

. (Join-Path $PSScriptRoot "build-common.ps1")
Invoke-LibRawNativeBuild -Rid linux-x64 -Triplet x64-linux-hplr `
    -VcpkgRoot $VcpkgRoot -OutputRoot $OutputRoot -BuildRoot $BuildRoot `
    -PackageVersion $PackageVersion `
    -Reentrant $true -Lcms $true -OpenMp $true -Sanitizers:$Sanitizers
