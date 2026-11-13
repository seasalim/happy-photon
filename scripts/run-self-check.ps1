[CmdletBinding()]
param(
    # The published HappyPhoton executable (a publish folder's, the unpacked MSIX payload's, or the
    # macOS bundle's Contents/MacOS/HappyPhoton).
    [Parameter(Mandatory)]
    [string] $Executable,

    # Instead of a pass, require exit 1 at this step (the control publish).
    [string] $ExpectFailStep,

    [int] $TimeoutSeconds = 300
)

# Runs the packaged-build qualification (`HappyPhoton --self-check <folder>`) against the CC0 RAW and
# JPEG fixtures and checks its one-line result. CI, the release workflow and local gates all use it.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Must match SelfCheck.StepCount.
$expectedSteps = 13
$repository = Split-Path -Parent $PSScriptRoot
$fixtures = @(
    (Join-Path $repository 'Tests/assets/nikon-d70-burst-1.nef'),
    (Join-Path $repository 'Tests/assets/srgb-exif-gps-orientation-6.jpg')
)
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ("happy-photon-self-check-" + [Guid]::NewGuid().ToString('N'))
$inputFolder = Join-Path $work 'fixtures'
$stdout = Join-Path $work 'stdout.txt'
$stderr = Join-Path $work 'stderr.txt'
New-Item -ItemType Directory -Force -Path $inputFolder | Out-Null

try {
    foreach ($fixture in $fixtures) {
        Copy-Item -LiteralPath $fixture -Destination $inputFolder
    }

    # The app is a GUI-subsystem executable on Windows, so wait on the process itself rather than
    # relying on the shell to wait or to set an exit code.
    $process = Start-Process -FilePath $executablePath `
        -ArgumentList @('--self-check', "`"$inputFolder`"") `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -NoNewWindow `
        -PassThru
    $null = $process.Handle

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "self-check did not finish within $TimeoutSeconds s: $executablePath"
    }

    $process.WaitForExit()
    $exitCode = $process.ExitCode
    $output = @(Get-Content -LiteralPath $stdout -ErrorAction SilentlyContinue)
    $errors = @(Get-Content -LiteralPath $stderr -ErrorAction SilentlyContinue)
    $result = @($output | Where-Object { $_ -like 'self-check:*' }) | Select-Object -Last 1

    $output | ForEach-Object { Write-Output $_ }
    $errors | ForEach-Object { Write-Output "stderr: $_" }

    if ($ExpectFailStep) {
        if ($exitCode -ne 1 -or $result -notlike "self-check: FAIL ${ExpectFailStep}:*") {
            throw "Expected exit 1 at step '$ExpectFailStep'; got exit $exitCode, '$result'."
        }

        Write-Output "CONTROL OK: failed at $ExpectFailStep as required."

        return
    }

    if ($exitCode -ne 0 -or $result -ne "self-check: ok ($expectedSteps steps)") {
        throw "self-check failed: exit $exitCode, '$result'."
    }
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
