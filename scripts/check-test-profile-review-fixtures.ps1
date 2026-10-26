# Loaded by check-test-profile-fixtures.ps1; these cases cover review regressions.
$childClass = [pscustomobject]@{ methods = @(@{ condition = @{ category = 'child-only' } }, @{ condition = @{ category = 'always-on' } }) }
Assert-Equal (Test-ProfileOptInClass $childClass) $false 'R1-5 child markers are not opt-in'
$childClass.methods += @{ condition = @{ category = 'opt-in' } }
Assert-Equal (Test-ProfileOptInClass $childClass) $true 'R1-5 mixed opt-in class'
$resolution = @{ project = 'Unit'; class = 'ProfileFixture.Conditions'; method = 'Silent'; category = 'silent-return'
    predicted = 'Passed'; variables = @('PROFILE_SILENT'); reason = 'conditions.cs.txt:38 returns without skipping when the gate is absent.' }
$resolved = Resolve-ProfileCondition (Get-MethodCondition $index ProfileFixture.Conditions Silent) @($resolution) Unit ProfileFixture.Conditions Silent
Assert-Equal $resolved.unresolved.Count 0 'R1-4 reviewed condition'
Assert-Equal $resolved.predicted 'Passed' 'R1-4 silent return retains actual default outcome'

. "$PSScriptRoot/test-profile-reconciliation.ps1"
$environmentResolution = [pscustomobject]@{
    project = 'Unit'; class = 'ProfileFixture.Conditions'; method = 'FixtureDependent'
    category = 'platform/build-conditioned'; predicted = 'Environment'; variables = @()
    reason = 'conditions.cs.txt:96-98 skips per case when its local fixture is unavailable.'
}
$environment = Resolve-ProfileCondition (Get-MethodCondition $index ProfileFixture.Conditions FixtureDependent) `
    @($environmentResolution) Unit ProfileFixture.Conditions FixtureDependent
$caseNames = @('ProfileFixture.Conditions.FixtureDependent(available: False)', 'ProfileFixture.Conditions.FixtureDependent(available: True)')
$observed = @(
    [pscustomobject]@{ name = $caseNames[0]; outcome = 'NotExecuted' }
    [pscustomobject]@{ name = $caseNames[1]; outcome = 'Passed' }
)
$comparison = Compare-ProfileMethod $environment $caseNames $observed $false
Assert-Equal $comparison.issues.Count 0 'R5-1 mixed environment outcomes accepted'
Assert-Equal $comparison.outcomeCounts.Passed 1 'R5-1 observed passed count'
Assert-Equal $comparison.outcomeCounts.NotExecuted 1 'R5-1 observed skipped count'
Assert-Equal $comparison.outcomeCounts.Failed 0 'R5-1 observed failed count'
$observed[1].outcome = 'Failed'
$comparison = Compare-ProfileMethod $environment $caseNames $observed $false
Assert-Equal $comparison.issues.Count 1 'R5-1 Environment never absorbs Failed'
Assert-Equal $comparison.outcomeCounts.Failed 1 'R5-1 failure remains visible'
$observed[1].outcome = 'Passed'
Assert-Equal (Compare-ProfileMethod $environment $caseNames @($observed[0]) $false).issues.Count 1 'R5-1 missing discovered case rejected'
Assert-Equal ((Compare-ProfileMethod $environment $caseNames @() $false).issues.Count -gt 0) $true 'R5-1 absent method rejected'
$unexpected = @($observed) + [pscustomobject]@{ name = 'Fixture.Undiscovered'; outcome = 'Passed' }
Assert-Equal (Compare-ProfileMethod $environment $caseNames $unexpected $false).issues.Count 1 'R5-1 undiscovered case rejected'
Assert-Equal (Compare-ProfileMethod $environment @('ProfileFixture.Conditions.FixtureDependent') $observed $false).issues.Count 0 'R5-1 unexpanded theory reconciles runtime rows'

foreach ($category in @('always-on', 'opt-in', 'child-only', 'silent-return', 'mixed')) {
    $environmentResolution.category = $category
    $rejected = $false

    try {
        $null = Resolve-ProfileCondition @{} @($environmentResolution) Unit ProfileFixture.Conditions FixtureDependent
    } catch {
        $rejected = $_.Exception.Message -eq 'Invalid resolution: ProfileFixture.Conditions.FixtureDependent'
    }

    Assert-Equal $rejected $true "R5-1 Environment rejected for $category"
}

$optInResolution = [pscustomobject]@{
    project = 'Unit'; class = 'ProfileFixture.Conditions'; method = 'Unresolved'; category = 'opt-in'
    predicted = 'Passed'; variables = @('PROFILE_GATE'); reason = 'conditions.cs.txt:54-57 gates only enabled=true.'
    casePredictions = @(
        @{ name = 'ProfileFixture.Conditions.Unresolved(enabled: False)'; predicted = 'Passed' }
        @{ name = 'ProfileFixture.Conditions.Unresolved(enabled: True)'; predicted = 'NotExecuted' }
    )
}
$optIn = Resolve-ProfileCondition (Get-MethodCondition $index ProfileFixture.Conditions Unresolved) `
    @($optInResolution) Unit ProfileFixture.Conditions Unresolved
$optInRows = @(
    [pscustomobject]@{ name = $optInResolution.casePredictions[0].name; outcome = 'Passed' }
    [pscustomobject]@{ name = $optInResolution.casePredictions[1].name; outcome = 'NotExecuted' }
)
$discovered = @('ProfileFixture.Conditions.Unresolved')
Assert-Equal (Compare-ProfileMethod $optIn $discovered $optInRows $false).issues.Count 0 'R5-2 conditional opt-in cases reconcile'
Assert-Equal (Compare-ProfileMethod $optIn $discovered @($optInRows[0]) $false).issues.Count 1 'R5-2 skipped opt-in row must be present'
$optInRows[1].outcome = 'Passed'
Assert-Equal (Compare-ProfileMethod $optIn $discovered $optInRows $false).issues.Count 1 'R5-2 opt-in case cannot pass by default'
$optInRows[1].outcome = 'NotExecuted'
$optInRows[0].outcome = 'NotExecuted'
Assert-Equal (Compare-ProfileMethod $optIn $discovered $optInRows $false).issues.Count 1 'R5-2 ordinary case cannot skip'
$optInRows[0].outcome = 'Failed'
Assert-Equal (Compare-ProfileMethod $optIn $discovered $optInRows $false).issues.Count 1 'R5-2 case predictions never absorb Failed'
