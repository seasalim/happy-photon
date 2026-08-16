param(
    [ValidateSet("win-x64", "linux-x64")]
    [string] $RuntimeIdentifier = $(if ($IsWindows) { "win-x64" } else { "linux-x64" })
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$expectedRid = if ($IsWindows) { "win-x64" } elseif ($IsLinux) { "linux-x64" } else { "" }
if ($RuntimeIdentifier -ne $expectedRid) {
    throw "Run the $RuntimeIdentifier smoke on a matching host."
}

$project = Join-Path $PSScriptRoot "libraw-single-file-smoke/LibRawSingleFileSmoke.csproj"
$publishDirectory = Join-Path $repoRoot "artifacts/libraw/single-file-smoke/$RuntimeIdentifier"
$fixture = Join-Path $repoRoot "Tests/assets/canon-eos-350d.cr2"

dotnet restore $project --locked-mode
if ($LASTEXITCODE -ne 0) { throw "Single-file smoke restore failed." }
dotnet publish $project --configuration Release --runtime $RuntimeIdentifier `
    --self-contained true --no-restore --output $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "Single-file smoke publish failed." }

$executable = Join-Path $publishDirectory $(if ($IsWindows) {
    "LibRawSingleFileSmoke.exe"
} else {
    "LibRawSingleFileSmoke"
})
& $executable $fixture
if ($LASTEXITCODE -ne 0) { throw "Single-file smoke decode failed." }
