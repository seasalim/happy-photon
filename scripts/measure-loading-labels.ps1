[CmdletBinding()]
param(
    [ValidateRange(1, 100)][int] $Steps = 100,
    [ValidateRange(1, 600)][int] $TimeoutSeconds = 480,
    [string] $ResultsDirectory = "",
    [string] $LoupeProperty = "IsLoadingMessageVisible",
    [string] $DevelopProperty = "IsDevelopLoadingMessageVisible"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $ResultsDirectory) {
    $ResultsDirectory = Join-Path $root "Tests/TestResults/loading-label-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))-$([Guid]::NewGuid().ToString('N'))"
}
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
$null = New-Item -ItemType Directory -Path $ResultsDirectory
$settings = @{
    LOADING_LABEL_RESULTS = $ResultsDirectory
    LOADING_LABEL_STEPS = "$Steps"
    LOADING_LABEL_LOUPE_PROPERTY = $LoupeProperty
    LOADING_LABEL_DEVELOP_PROPERTY = $DevelopProperty
}
$previous = @{}
try {
    foreach ($key in $settings.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $settings[$key])
    }
    $arguments = @('test', 'Tests/HappyPhoton.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore',
        '--filter', 'FullyQualifiedName~LoadingLabelDwellTests.Measure',
        '--logger', 'console;verbosity=normal', '--results-directory', "`"$ResultsDirectory`"")
    $process = Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $root -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $ResultsDirectory 'stdout.txt') `
        -RedirectStandardError (Join-Path $ResultsDirectory 'stderr.txt')
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        throw "Loading-label measurement exceeded $TimeoutSeconds seconds; process tree stopped."
    }
    Get-Content (Join-Path $ResultsDirectory 'stdout.txt'), (Join-Path $ResultsDirectory 'stderr.txt')
    Write-Host "Loading-label results: $ResultsDirectory"
    if ($process.ExitCode -ne 0) { throw "Loading-label measurement failed ($($process.ExitCode))." }
} finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key]) }
}
