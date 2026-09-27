# Run under the host's exclusive measure lock after the implementor heavy lease exits.
$ErrorActionPreference = 'Stop'
foreach ($fixture in 'raw', 'standard') {
    & "$PSScriptRoot/RunHealGates.ps1" -Gate G3Tick -Fixture $fixture
    foreach ($workload in 'SCap6', 'SArea64') {
        & "$PSScriptRoot/RunHealGates.ps1" -Gate G4Contention -Fixture $fixture -Workload $workload -AreaLimit 0.18849555921538758
    }
    & "$PSScriptRoot/RunHealGates.ps1" -Gate Wp3ExportDelta -Fixture $fixture
    & "$PSScriptRoot/RunHealGates.ps1" -Gate G1Candidates -Fixture $fixture
}
# WP3 mapping: G3Tick -> G1; G4Contention -> G2; Wp3ExportDelta -> G3/G4;
# G1Candidates (FINAL Membrane-additive only) -> G5. Logs retain the WP1 identifiers.
