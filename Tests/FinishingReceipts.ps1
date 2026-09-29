function New-FinishingReceipt {
    param(
        [object[]]$Records,
        [string]$GateId,
        [string]$FixtureId,
        [string]$CandidateId,
        [string]$CandidatePath,
        [string]$AssemblyPath,
        [string]$ProductionPath,
        [string]$LogStamp
    )

    function Median($values) {
        $sorted = @($values | Sort-Object)
        return $sorted[[int][Math]::Floor($sorted.Count / 2)]
    }

    $rows = @($Records | Where-Object { $_.candidate -eq $CandidateId -and $_.gate -eq $GateId })
    $expected = switch ($GateId) { 'G1' { 5 } 'G2' { 1 } 'G3' { 4 } 'G4' { 11 } }
    $pass = $rows.Count -eq $expected
    $summary = $null

    if ($pass -and $GateId -eq 'G1') {
        $pass = @($rows.pid | Sort-Object -Unique).Count -eq 5
        $control = Median @($rows | ForEach-Object { $_.values.control })
        $active = Median @($rows | ForEach-Object { $_.values.active })
        $increment = Median @($rows | ForEach-Object { $_.values.increment })
        $limits = $rows[0].values
        $valid = $control -ge $limits.controlMin -and $control -le $limits.controlMax
        $pass = $pass -and $valid -and $active -le 150 -and $increment -le 60
        $summary = @{ control = $control; active = $active; increment = $increment; valid = $valid;
            controlMin = $limits.controlMin; controlMax = $limits.controlMax; activeLimit = 150; incrementLimit = 60 }
    }
    elseif ($pass) {
        $pass = @($rows | Where-Object { !$_.values.pass }).Count -eq 0
    }

    $receipt = @{ gate = $GateId; fixture = $FixtureId; candidate = $CandidateId; pass = $pass;
        candidateSha256 = (Get-FileHash -LiteralPath $CandidatePath -Algorithm SHA256).Hash;
        assemblySha256 = (Get-FileHash -LiteralPath $AssemblyPath -Algorithm SHA256).Hash;
        productionSha256 = (Get-FileHash -LiteralPath $ProductionPath -Algorithm SHA256).Hash;
        records = $rows; summary = $summary; logStamp = $LogStamp }

    foreach ($row in $rows) {
        foreach ($field in 'candidateSha256','assemblySha256','productionSha256') {
            if ($row.$field -cne $receipt[$field]) {
                $receipt.pass = $false
            }
        }
    }

    return $receipt
}
