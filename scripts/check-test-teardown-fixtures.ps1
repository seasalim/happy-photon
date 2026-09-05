[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$checker = Join-Path $PSScriptRoot "check-test-teardown.ps1"

$positive = (& $checker -IncludePatterns @(
    "scripts/fixtures/test-teardown/approved.cs.txt", "Tests/TestUiScope.cs") 6>&1 |
    Out-String)
if ($positive -notmatch 'Raw matches: 10; approved exceptions: 10; unresolved violations: 0') {
    throw "Approved teardown shapes were not recognized: $positive"
}

$rejected = $false
$negative = & {
    try {
        & $checker -IncludePatterns "scripts/fixtures/test-teardown/unprotected.cs.txt"
    }
    catch {
        if ($_.Exception.Message -notlike 'Test teardown requires failure-safe cleanup*') {
            throw
        }
        $script:rejected = $true
    }
} 6>&1 | Out-String
if (-not $rejected -or
    $negative -notmatch 'Raw matches: 8; approved exceptions: 0; unresolved violations: 8') {
    throw "Unprotected teardown fixture was not rejected: $negative"
}
Write-Host "Teardown policy fixtures passed (approved shapes and eight rejected sites)."
