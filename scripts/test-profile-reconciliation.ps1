# Reconcile one method against discovery without hiding failed or absent cases.
function Compare-ProfileMethod($Condition, $Cases, $Observed, [bool] $Quarantined) {
    $issues = [Collections.Generic.List[string]]::new()
    $counts = [ordered]@{ Passed = 0; NotExecuted = 0; Failed = 0 }

    foreach ($group in $Observed | Group-Object outcome) {
        $counts[$group.Name] = $group.Count
    }

    if ($Quarantined) {
        if ($Observed.Count) { $issues.Add('Quarantine unexpectedly present in TRX.') }
    } else {
        if (-not $Observed.Count) { $issues.Add('Method absent from TRX.') }

        $predictions = @(if ($Condition.ContainsKey('casePredictions')) { $Condition.casePredictions })

        foreach ($row in $Observed) {
            $discovered = @($Cases | Where-Object { $row.name -eq $_ -or $row.name.StartsWith("$_(") })
            if (-not $discovered.Count) { $issues.Add("TRX case absent from discovery: $($row.name)") }

            $expected = $Condition.predicted

            if ($predictions.Count) {
                $prediction = @($predictions | Where-Object name -eq $row.name)

                if (-not $prediction.Count) {
                    $issues.Add("TRX case absent from reviewed predictions: $($row.name)")
                    continue
                }

                $expected = $prediction[0].predicted
            }

            $allowed = if ($expected -eq 'Environment') { @('Passed', 'NotExecuted') } else { @($expected) }

            if ($row.outcome -notin $allowed) {
                $issues.Add("Case $($row.name): predicted $expected; TRX [$($row.outcome)].")
            }
        }

        # Unexpanded theories match their runtime rows; reviewed opt-in rows must each appear.
        $required = @($Cases) + @($predictions | ForEach-Object name) | Sort-Object -Unique

        foreach ($case in $required) {
            if (-not @($Observed | Where-Object { $_.name -eq $case -or $_.name.StartsWith("$case(") }).Count) {
                $issues.Add("Discovery/reviewed case absent from TRX: $case")
            }
        }
    }

    return @{ issues = @($issues.ToArray()); outcomeCounts = $counts }
}
