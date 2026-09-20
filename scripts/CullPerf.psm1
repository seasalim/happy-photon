Set-StrictMode -Version Latest

function Get-CullPerfTestEvidence {
    param([string] $Directory, [int] $ExitCode, [int] $Expected = 1)
    $files = @(Get-ChildItem -LiteralPath $Directory -Filter *.trx -ErrorAction SilentlyContinue)
    if ($files.Count -ne 1) { return 'inconclusive' }
    [xml] $trx = Get-Content -LiteralPath $files[0].FullName -Raw
    $results = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
    if ($results.Count -ne $Expected) { return 'inconclusive' }
    if (@($results | Where-Object { $_.outcome -notin @('Passed', 'Failed') }).Count) { return 'inconclusive' }
    if ($ExitCode -ne 0 -or @($results | Where-Object outcome -eq 'Failed').Count) { return 'fail' }
    return 'pass'
}

function Get-CullPerfVerdict {
    param([string[]] $Verdicts)
    if (-not $Verdicts.Count -or $Verdicts -contains 'inconclusive') { return 'inconclusive' }
    if ($Verdicts -contains 'fail') { return 'fail' }
    return 'pass'
}

function Get-CullPerfBuildEvidence {
    param([bool] $Verified)
    return @{
        id = 'build'
        verdict = if ($Verified) { 'pass' } else { 'inconclusive' }
        reason = if ($Verified) { 'Release build completed for this attempt.' } else {
            'Build unverified: -NoBuild is diagnostic-only; binaries may differ from candidateCommit.'
        }
    }
}

function New-CullPerfAttempt {
    param([string] $Root, [string] $Commit)
    $name = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ') + '-' + $Commit.Substring(0, 7) + '-' + [Guid]::NewGuid().ToString('N')
    return (New-Item -ItemType Directory -Path (Join-Path $Root $name) -ErrorAction Stop).FullName
}

Export-ModuleMember -Function Get-CullPerfTestEvidence, Get-CullPerfVerdict, Get-CullPerfBuildEvidence, New-CullPerfAttempt
