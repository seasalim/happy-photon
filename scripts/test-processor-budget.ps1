# Validate effective simultaneous caps before discovery or execution starts a host.
function Assert-TestProcessorBudget([int[]] $Caps, [switch] $Serial) {
    if ($env:HAPPY_PHOTON_FULL_CPU -eq '1') { return }

    foreach ($cap in $Caps) {
        if ($cap -lt 1 -or $cap -gt 8) { throw "D-9 requires every test-process cap to be between 1 and 8; got $cap." }
    }

    $total = if ($Serial) { ($Caps | Measure-Object -Maximum).Maximum } else { ($Caps | Measure-Object -Sum).Sum }

    if ($total -gt 24) { throw "D-9 concurrent test-process caps sum to $total; maximum is 24." }
}
