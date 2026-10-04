[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$')]
    [string] $Repository,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')]
    [string] $SourceRevision
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Only main-branch CI qualifies releases; PR merge refs are different source.
$runsJson = gh api "repos/$Repository/actions/workflows/ci.yml/runs?head_sha=$SourceRevision&branch=main&per_page=100"
if ($LASTEXITCODE -ne 0) { throw 'Could not read CI qualification.' }

$runs = @((($runsJson -join "`n") | ConvertFrom-Json).workflow_runs |
    Where-Object { $_.event -in @('push', 'workflow_dispatch') } |
    Sort-Object id -Descending)
if ($runs.Count -eq 0) { throw "No main-branch CI exists for $SourceRevision." }

$run = $runs[0]
if ($run.head_sha -ne $SourceRevision -or $run.head_branch -ne 'main' -or
    $run.head_repository.full_name -ne $Repository -or $run.path -ne '.github/workflows/ci.yml' -or
    $run.status -ne 'completed' -or $run.conclusion -ne 'success') {
    throw "Latest CI for $SourceRevision must complete successfully before packaging."
}

$jobsJson = gh api "repos/$Repository/actions/runs/$($run.id)/jobs?filter=latest&per_page=100"
if ($LASTEXITCODE -ne 0) { throw 'Could not read CI jobs.' }

$response = ($jobsJson -join "`n") | ConvertFrom-Json
$jobs = @($response.jobs)
if ($jobs.Count -ne $response.total_count) { throw 'CI job evidence is incomplete.' }

$required = @{
    'Repository policy' = 'Verify repository policy'
    'Windows x64' = 'Verify repository'
    'Linux x64' = 'Verify repository'
    'macOS Apple Silicon' = 'Verify repository'
}

foreach ($name in $required.Keys) {
    $matching = @($jobs | Where-Object name -EQ $name)
    if ($matching.Count -ne 1) { throw "Missing or ambiguous CI job: $name." }

    $job = $matching[0]
    $verification = @($job.steps | Where-Object name -EQ $required[$name])
    if ($job.status -ne 'completed' -or $job.conclusion -ne 'success' -or
        $verification.Count -ne 1 -or $verification[0].conclusion -ne 'success') {
        throw "Required verification did not pass: $name."
    }
}

$evidence = "Qualified source $SourceRevision with CI $($run.html_url) (attempt $($run.run_attempt))."
Write-Host $evidence

if ($env:GITHUB_STEP_SUMMARY) {
    $evidence | Out-File -LiteralPath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
