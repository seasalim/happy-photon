[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/test-profile-common.ps1"
. "$PSScriptRoot/test-profile-conditions.ps1"
. "$PSScriptRoot/test-profile-report.ps1"
$fixtures = Join-Path $PSScriptRoot 'fixtures/test-profile'

function Assert-Equal($Actual, $Expected, [string] $Label) {
    if ($Actual -ne $Expected) { throw "${Label}: expected '$Expected', got '$Actual'." }
}

$samples = @(Get-Content "$fixtures/samples.txt" | ForEach-Object { $_ | ConvertFrom-Json })
$tests = @(Read-ProfileTrx $fixtures)
$join = Get-ProfileAttribution $samples $tests
Assert-Equal $join.hosts[0].g4Passed $true 'G4 passing fixture'
Assert-Equal $join.hosts[0].attributedShare 1 'G4 full coverage'
Assert-Equal ($join.classes | Where-Object class -eq Fixture.A).growthBytes 30 'A hand-computed growth'
Assert-Equal ($join.classes | Where-Object class -eq Fixture.B).growthBytes 70 'B hand-computed growth'
$extra = [pscustomobject]@{ host = 'HappyPhoton.Tests'; class = 'Fixture.C'; name = 'Fixture.C.Three'
    outcome = 'Passed'; start = $tests[0].start; end = $tests[1].end }
$over = Get-ProfileAttribution $samples (@($tests) + $extra)
Assert-Equal $over.hosts[0].overBoundCount 1 'Bound overlap'
Assert-Equal $over.hosts[0].g4Passed $false 'G4 overlap fails'
$uncovered = @($samples) + [pscustomobject]@{ utc = '2026-10-14T00:00:03Z'; instance = '1'; host = 'HappyPhoton.Tests'; pagedBytes = 250 }
$under = Get-ProfileAttribution $uncovered $tests
Assert-Equal $under.hosts[0].attributedShare 0.8 'Uncovered lifetime sample retained'
Assert-Equal $under.hosts[0].g4Passed $false 'G4 coverage fails'
Assert-Equal $under.hosts[0].unattributedGrowthBytes 70 'Unattributed growth retained'
$large = @(foreach ($index in 0..2) {
    [pscustomobject]@{
        utc = ([DateTimeOffset]::Parse('2026-10-14T00:00:00Z').AddSeconds($index)).ToString('o')
        instance = 'large'; host = 'HappyPhoton.Tests'; pagedBytes = @([long]9e9, [long]0, [long]10e9)[$index]
    }
})
$largeGrowth = Get-ProfileAttribution $large @()
Assert-Equal $largeGrowth.hosts[0].unattributedGrowthBytes ([long]10e9) 'Int64 growth and decline beyond Int32 range'
$headlessSamples = $samples | ConvertTo-Json | ConvertFrom-Json
$headlessTests = $tests | ConvertTo-Json | ConvertFrom-Json

foreach ($row in @($headlessSamples) + @($headlessTests)) {
    $row.host = 'HappyPhoton.Headless.Tests'
}

$headless = Get-ProfileAttribution $headlessSamples $headlessTests
Assert-Equal $headless.hosts[0].overBoundCount 1 'Headless bound is one'

Assert-Equal (Test-ProfileKeepCitation '../FIRST.md' $true 'FIRST.md' "---`nstatus: approved`n---`n") $true 'Frontmatter field order'
$index = Read-ProfileSources @("$fixtures/conditions.cs.txt") @('PROFILE_RELEASE')
$expected = @{
    Always = @('always-on', 'Passed', 0)
    ParameterOnly = @('always-on', 'Passed', 0)
    Multiline = @('opt-in', 'NotExecuted', 0)
    PresenceEmpty = @('opt-in', 'NotExecuted', 0)
    PresenceWhitespace = @('opt-in', 'NotExecuted', 0)
    PresenceNegated = @('always-on', 'Passed', 0)
    ActiveBuild = @('opt-in', 'NotExecuted', 1)
    OptIn = @('opt-in', 'NotExecuted', 0)
    Child = @('child-only', 'NotExecuted', 0)
    Platform = @('platform/build-conditioned', 'Passed', 0)
    Build = @('always-on', 'Passed', 0)
    Silent = @('silent-return', 'Passed', 1)
    Helper = @('opt-in', 'NotExecuted', 0)
    Unresolved = @('always-on', 'Passed', 1)
    FixtureDependent = @('always-on', 'Passed', 1)
}
$categories = @()

foreach ($method in $expected.Keys) {
    $condition = Get-MethodCondition $index ProfileFixture.Conditions $method
    Assert-Equal $condition.category $expected[$method][0] "Category $method"
    Assert-Equal $condition.predicted $expected[$method][1] "Prediction $method"
    Assert-Equal $condition.unresolved.Count $expected[$method][2] "Unresolved $method"
    $categories += $condition.category
}

Assert-Equal (Get-MethodCondition $index ProfileFixture.Conditions ParameterOnly).variables[0] 'PROFILE_FIXTURE' 'R1-5 parameter reported without opt-in'
Assert-Equal (@($categories | Sort-Object -Unique).Count -gt 1) $true 'Mixed class preserves method categories'
$approved = "---`nid: KEEP`nstatus: approved`n---`nDone epic"
Assert-Equal (Test-ProfileKeepCitation 'docs/pipeline/TESTING.md' $false 'TESTING.md' '') $true 'Pipeline citation'
Assert-Equal (Test-ProfileKeepCitation 'scripts/runner.ps1' $false 'runner.ps1' '') $true 'Tool citation'
Assert-Equal (Test-ProfileKeepCitation '../KEEP.md' $true 'KEEP.md' $approved) $true 'Approved board citation'
Assert-Equal (Test-ProfileKeepCitation '../TESTSUITE.md' $true 'TESTSUITE.md' $approved) $false 'Self citation excluded'
Assert-Equal (Test-ProfileKeepCitation '../ARCHIVE/KEEP.md' $false 'KEEP.md' $approved) $false 'Archive excluded'
Assert-Equal (Test-ProfileKeepCitation '../PARKED.md' $true 'PARKED.md' "---`nstatus: parked`n---") $false 'Unapproved excluded'
Assert-Equal (Test-ProfileKeepCitation 'docs/ARCHITECTURE.md' $false 'ARCHITECTURE.md' '') $false 'Other docs informational'
$report = [IO.Path]::GetTempFileName()

try {
    $classes = @(foreach ($hostName in @('HappyPhoton.Tests', 'HappyPhoton.Headless.Tests')) {
        foreach ($number in 1..22) {
            [pscustomobject]@{
                host = $hostName; class = "Fixture.Always$number"; category = 'always-on'
                project = 'Unit'; methods = @(@{ condition = @{ category = 'always-on' } }); variables = @(); citations = @(); runnerWorkloads = @()
                measurements = @(foreach ($workload in @('solo', 'nightly')) {
                    @{ workload = $workload; wallSeconds = $number; attributedGrowthBytes = $null }
                })
            }
        }
    }) + [pscustomobject]@{
        host = 'HappyPhoton.Tests'; class = 'Fixture.Retire'; category = 'opt-in'; project = 'Unit'
        methods = @(@{ condition = @{ category = 'opt-in' } }); variables = @('GATE'); measurements = @()
        citations = @(@{ path = 'docs/notes.md'; line = 3; keepQualifying = $false })
        runnerWorkloads = @(@{ runner = 'runner.ps1'; workloadReferences = @(@{ line = 23; text = '-Filter Fixture.Retire' }) })
    }
    $data = @{ workloads = @('solo', 'nightly'); classes = $classes; unresolved = @('Unresolved method'); retireTableReady = $false }
    Write-ProfileReport $data $report
    $text = Get-Content -Raw $report
    Assert-Equal ($text -match 'BLOCKED: 1 unresolved') $true 'Unresolved conditions block retire table'
    Assert-Equal ($text -match 'No approved board spec') $false 'No premature retirement'
    Assert-Equal ([regex]::Matches($text, '### Twenty slowest always-on classes').Count) 4 'Both workloads for both hosts'
    Assert-Equal ([regex]::Matches($text, '\| Fixture.Always\d+ \| \d+ \|').Count) 80 'Twenty rows per host and workload'
    Assert-Equal ($text -match 'runner.ps1:23 -Filter Fixture.Retire \| docs/notes.md:3') $true 'Opt-in runner and citation retained'
    Assert-Equal ($text -match 'Ten largest|GC arms|Probe limit') $false 'Dropped memory tables absent'
    Assert-Equal ($text -match '\| Fixture.Retire \| GATE \|') $true 'Opt-in workload retained while retire table is blocked'
    Assert-Equal ($text -match '\| Fixture.Always22 \| 22 \|') $true 'Slowest row included'
    Assert-Equal ($text -match '\| Fixture.Always1 \| 1 \|') $false 'Only twenty slowest rows'
    $data.retireTableReady = $true
    $data.unresolved = @()
    Write-ProfileReport $data $report
    Assert-Equal ((Get-Content -Raw $report) -match '\| Fixture.Retire \| GATE \| No approved') $true 'Resolved retirement reason'
    $data.workloads = @('nightly')
    Write-ProfileReport $data $report
    $text = Get-Content -Raw $report
    Assert-Equal ([regex]::Matches($text, '### Twenty slowest always-on classes \(nightly\)').Count) 2 'Nightly-only inventory keeps its tables'
    Assert-Equal ($text -match 'classes \(solo\)') $false 'Unrequested workload table absent'
} finally {
    Remove-Item -LiteralPath $report
}
. "$PSScriptRoot/check-test-profile-review-fixtures.ps1"
. "$PSScriptRoot/check-test-profile-sampler-fixtures.ps1"
Write-Host 'Test profile fixtures passed: attribution data, method conditions, citation scope, ranked/blocked reports and sampler lifecycle.'
