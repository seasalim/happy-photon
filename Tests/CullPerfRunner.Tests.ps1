BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../scripts/CullPerf.psm1') -Force
}

Describe 'Culling qualification evidence' {
    It 'never passes a missing test result' {
        Get-CullPerfTestEvidence $TestDrive 0 | Should -Be 'inconclusive'
    }
    It 'does not count a skipped test as executed' {
        '<TestRun><Results><UnitTestResult outcome="NotExecuted" /></Results></TestRun>' | Set-Content (Join-Path $TestDrive 'case.trx')
        Get-CullPerfTestEvidence $TestDrive 0 | Should -Be 'inconclusive'
    }
    It 'checks expected count and process status' {
        '<TestRun><Results><UnitTestResult outcome="Passed" /></Results></TestRun>' | Set-Content (Join-Path $TestDrive 'case.trx')
        Get-CullPerfTestEvidence $TestDrive 0 | Should -Be 'pass'
        Get-CullPerfTestEvidence $TestDrive 1 | Should -Be 'fail'
        Get-CullPerfTestEvidence $TestDrive 0 2 | Should -Be 'inconclusive'
    }
    It 'caps unverified builds at inconclusive even when every measured gate passes' {
        $build = Get-CullPerfBuildEvidence -Verified $false
        $build.verdict | Should -Be 'inconclusive'
        $build.reason | Should -Match 'diagnostic-only'
        Get-CullPerfVerdict @('pass', $build.verdict) | Should -Be 'inconclusive'
        Get-CullPerfVerdict @('fail', $build.verdict) | Should -Be 'inconclusive'
        (Get-CullPerfBuildEvidence -Verified $true).verdict | Should -Be 'pass'
    }
    It 'retains distinct attempts for the same commit' {
        $first = New-CullPerfAttempt $TestDrive '1234567890'
        $second = New-CullPerfAttempt $TestDrive '1234567890'
        $first | Should -Not -Be $second
        Test-Path -LiteralPath $first | Should -BeTrue
    }
    It 'gives incomplete evidence precedence over failed thresholds' {
        Get-CullPerfVerdict @('pass', 'fail', 'inconclusive') | Should -Be 'inconclusive'
        Get-CullPerfVerdict @('pass', 'fail') | Should -Be 'fail'
        Get-CullPerfVerdict @('pass') | Should -Be 'pass'
    }
}
