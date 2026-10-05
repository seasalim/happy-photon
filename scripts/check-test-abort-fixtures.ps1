[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$reporter = Join-Path $PSScriptRoot "report-test-abort.ps1"
$fixtures = Join-Path $PSScriptRoot "fixtures/test-abort"
$transcript = Join-Path $fixtures "37335031242.txt"
$sequences = Join-Path $fixtures "captured"
$report = & $reporter -OutputPath $transcript -ResultsDirectory $sequences 6>&1 | Out-String

foreach ($expected in @(
        'Test host abort detected before stable-results reconciliation.',
        'Aborted host: HappyPhoton.Headless.Tests.dll; test running: HappyPhoton.Tests.SyncTransferParityViewModelTests.G1_RawProfile;',
        'Aborted host: HappyPhoton.Tests.dll; test running: HappyPhoton.Tests.SyncTransferBatchBaselineTests.G1_UndoRestoresFiveHundredDocumentsAndHistories(sync: False);')) {
    if (-not $report.Contains($expected)) { throw "Missing abort report: $expected" }
}

if ([regex]::Matches($report, 'Aborted host:').Count -ne 2) {
    throw "Abort reporting must name only unfinished tests, once per host/test: $report"
}

$fallback = & $reporter -OutputPath $transcript -ResultsDirectory (Join-Path $fixtures "no-sequence") 6>&1 | Out-String

if ($fallback -notmatch 'host/test unavailable' -or $fallback -notmatch 'SyncTransferParityViewModelTests.G1_RawProfile') {
    throw "Missing-sequence fallback lost the captured abort test: $fallback"
}

$malformed = & $reporter -OutputPath $transcript -ResultsDirectory (Join-Path $fixtures "malformed") 3>&1 6>&1 | Out-String

if ($malformed -notmatch 'Could not read abort sequence' -or $malformed -notmatch 'host/test unavailable') {
    throw "Malformed sequence hid the original abort: $malformed"
}

$quiet = & $reporter -OutputPath (Join-Path $fixtures "no-sequence/passed.txt") -ResultsDirectory $sequences 6>&1 | Out-String
if ($quiet.Trim()) { throw "Successful output must not report an abort: $quiet" }

Write-Host "Abort reporting fixtures passed (both captured hosts, missing/malformed sequences and successful output)."
