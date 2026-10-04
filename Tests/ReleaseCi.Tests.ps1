BeforeAll {
    $script:gate = Join-Path $PSScriptRoot '../scripts/check-release-ci.ps1'
    $script:revision = 'a' * 40

    function New-Qualification {
        return @{
            id = 123
            head_sha = $script:revision
            head_branch = 'main'
            head_repository = @{ full_name = 'owner/repo' }
            path = '.github/workflows/ci.yml'
            event = 'push'
            status = 'completed'
            conclusion = 'success'
            html_url = 'https://github.com/owner/repo/actions/runs/123'
            run_attempt = 1
        }
    }
}

Describe 'Release CI qualification' {
    BeforeEach {
        $global:releaseCiTestRuns = @((New-Qualification))
        $global:releaseCiTestJobs = @('Repository policy', 'Windows x64', 'Linux x64', 'macOS Apple Silicon') |
            ForEach-Object {
                $step = if ($_ -eq 'Repository policy') { 'Verify repository policy' } else { 'Verify repository' }

                return @{
                    name = $_
                    status = 'completed'
                    conclusion = 'success'
                    steps = @(@{ name = $step; conclusion = 'success' })
                }
            }
        $global:releaseCiTestApiFailure = $false
        $global:releaseCiTestJobsFailure = $false
        Mock gh {
            if ($args[1] -like '*workflows/ci.yml/runs?*') {
                $global:LASTEXITCODE = [int]$global:releaseCiTestApiFailure
                return @{ workflow_runs = $global:releaseCiTestRuns } | ConvertTo-Json -Depth 8
            }

            $global:LASTEXITCODE = [int]$global:releaseCiTestJobsFailure
            return @{ total_count = $global:releaseCiTestJobs.Count; jobs = $global:releaseCiTestJobs } | ConvertTo-Json -Depth 8
        }
    }

    It 'accepts exact-commit main CI with all required verification steps' {
        { & $script:gate -Repository owner/repo -SourceRevision $script:revision } | Should -Not -Throw
    }

    It 'accepts a manual main CI run' {
        $global:releaseCiTestRuns[0].event = 'workflow_dispatch'
        { & $script:gate -Repository owner/repo -SourceRevision $script:revision } | Should -Not -Throw
    }

    It 'rejects missing qualification' {
        $global:releaseCiTestRuns = @()
        { & $script:gate -Repository owner/repo -SourceRevision $script:revision } | Should -Throw
    }

    It 'rejects <field> mismatch' -TestCases @(
        @{ field = 'head_sha'; value = ('b' * 40) }
        @{ field = 'head_branch'; value = 'feature' }
        @{ field = 'path'; value = '.github/workflows/pages.yml' }
        @{ field = 'event'; value = 'pull_request' }
        @{ field = 'status'; value = 'in_progress' }
        @{ field = 'conclusion'; value = 'failure' }
    ) {
        param($field, $value)
        $global:releaseCiTestRuns[0][$field] = $value
        { & $script:gate -Repository owner/repo -SourceRevision $script:revision } | Should -Throw
    }

    It 'rejects a foreign repository' {
        $global:releaseCiTestRuns[0].head_repository.full_name = 'someone/fork'
        { & $script:gate -Repository owner/repo -SourceRevision $script:revision } | Should -Throw
    }

    It 'does not fall back to an older green run' {
        $newer = New-Qualification
        $newer.id = 124
        $newer.conclusion = 'failure'
        $global:releaseCiTestRuns += $newer
        { & $script:gate -Repository owner/repo -SourceRevision $script:revision } | Should -Throw
    }

    It 'rejects a missing platform' {
        $global:releaseCiTestJobs = $global:releaseCiTestJobs[0..2]
        { & $script:gate -Repository owner/repo -SourceRevision $script:revision } | Should -Throw
    }

    It 'rejects skipped verification inside a successful job' {
        $global:releaseCiTestJobs[1].steps[0].conclusion = 'skipped'
        { & $script:gate -Repository owner/repo -SourceRevision $script:revision } | Should -Throw
    }

    It 'rejects failed jobs and API failures' {
        $global:releaseCiTestJobs[1].conclusion = 'failure'
        { & $script:gate -Repository owner/repo -SourceRevision $script:revision } | Should -Throw
        $global:releaseCiTestJobs[1].conclusion = 'success'
        $global:releaseCiTestApiFailure = $true
        { & $script:gate -Repository owner/repo -SourceRevision $script:revision } | Should -Throw
        $global:releaseCiTestApiFailure = $false
        $global:releaseCiTestJobsFailure = $true
        { & $script:gate -Repository owner/repo -SourceRevision $script:revision } | Should -Throw
    }
}

AfterAll {
    Remove-Variable releaseCiTestRuns, releaseCiTestJobs, releaseCiTestApiFailure, releaseCiTestJobsFailure -Scope Global -ErrorAction SilentlyContinue
}
