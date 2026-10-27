# Class-level partitioning shared by verification and partition dry runs.
function Assert-HeadlessTestShards($Tests, $Shards) {
    $classes = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $assigned = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)

    foreach ($case in $Tests) {
        if (-not $classes.ContainsKey($case.Class)) {
            $classes.Add($case.Class, [Collections.Generic.List[string]]::new())
        }

        $classes[$case.Class].Add($case.DisplayName)
    }

    foreach ($shard in $Shards) {
        foreach ($name in $shard.Classes) {
            if (-not $assigned.ContainsKey($name)) { $assigned.Add($name, 0) }
            $assigned[$name]++
        }
    }

    $errors = [Collections.Generic.List[string]]::new()

    foreach ($name in $assigned.Keys) {
        if (-not $classes.ContainsKey($name)) {
            $errors.Add("Unknown shard class: $name.")
        }
    }

    foreach ($name in $classes.Keys) {
        $count = if ($assigned.ContainsKey($name)) { $assigned[$name] } else { 0 }

        if ($count -ne 1) {
            $errors.Add("Shard class $name occurs $count times; expected once. Tests:`n$($classes[$name] -join "`n")")
        }
    }

    if ($errors.Count -gt 0) { throw ($errors -join "`n") }
}

function Get-HeadlessTestShards([string] $ManifestPath, [int] $Count) {
    $manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json

    if ($manifest.schemaVersion -ne 1 -or
        $manifest.project -ne "HeadlessTests/HappyPhoton.Headless.Tests.csproj" -or
        @($manifest.tests).Count -eq 0) {
        throw "Invalid or empty headless discovery manifest: $ManifestPath."
    }

    $classes = [Collections.Generic.SortedDictionary[string, object]]::new([StringComparer]::Ordinal)

    foreach ($case in $manifest.tests) {
        if ([string]::IsNullOrWhiteSpace($case.Class)) { throw "Manifest test has no class: $($case.DisplayName)." }

        if (-not $classes.ContainsKey($case.Class)) {
            $classes.Add($case.Class, [pscustomobject]@{
                Name = $case.Class
                MethodCount = 0
                Own = $false
                OrdinalIndex = 0
            })
        }

        $class = $classes[$case.Class]
        $class.MethodCount++

        if ($null -ne $case.PSObject.Properties["Traits"] -and $null -ne $case.Traits -and
            $null -ne $case.Traits.PSObject.Properties["Shard"] -and "Own" -in @($case.Traits.Shard)) {
            $class.Own = $true
        }
    }

    $own = @($classes.Values | Where-Object Own)

    if ($own.Count -gt 3 -or $own.Count -ge $Count -or $Count -gt $classes.Count) {
        throw "Cannot partition $($classes.Count) headless classes into $Count shards with $($own.Count) Own classes (maximum three; reserve a shared shard)."
    }

    $shards = @(for ($index = 0; $index -lt $Count; $index++) {
        [pscustomobject]@{
            Index = $index + 1
            Classes = [Collections.Generic.List[string]]::new()
            MethodCount = 0
            Filter = ""
        }
    })

    for ($index = 0; $index -lt $own.Count; $index++) {
        $shards[$index].Classes.Add($own[$index].Name)
        $shards[$index].MethodCount = $own[$index].MethodCount
    }

    $index = 0

    foreach ($class in $classes.Values) {
        $class.OrdinalIndex = $index++
    }

    $shared = @($shards | Select-Object -Skip $own.Count)
    $remaining = @($classes.Values | Where-Object { -not $_.Own } |
        Sort-Object @{ Expression = "MethodCount"; Descending = $true }, OrdinalIndex)

    foreach ($class in $remaining) {
        $shard = $shared | Sort-Object MethodCount, Index | Select-Object -First 1
        $shard.Classes.Add($class.Name)
        $shard.MethodCount += $class.MethodCount
    }

    Assert-HeadlessTestShards $manifest.tests $shards

    foreach ($shard in $shards) {
        if ($shard.Classes.Count -eq 0) { throw "Headless shard $($shard.Index) is empty." }

        $shard.Classes.Sort([StringComparer]::Ordinal)
        $clauses = @($shard.Classes | ForEach-Object { "FullyQualifiedName~$_." })
        $shard.Filter = "(Category!=Quarantined)&($($clauses -join '|'))"
        Write-Host "Headless shard $($shard.Index): $($shard.Classes.Count) classes, $($shard.MethodCount) discovered methods."
    }

    return $shards
}
