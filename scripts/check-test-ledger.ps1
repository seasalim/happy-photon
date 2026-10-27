[CmdletBinding()]
param(
    [ValidateSet('Policy', 'Results')]
    [string] $Mode = 'Policy',
    [string] $LedgerPath = 'Tests/pruning-ledger.json',
    [string] $RepoRoot = (Split-Path $PSScriptRoot),
    [string] $ResultsDirectory,
    # Fixtures share evaluations only while Compile items and symbols remain fixed.
    [hashtable] $SourceCache = @{ projects = @{}; files = @{} }
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$issues = [Collections.Generic.List[string]]::new()
$removals = [Collections.Generic.List[object]]::new()
$requirements = [Collections.Generic.List[object]]::new()
$methods = [Collections.Generic.List[object]]::new()
$projects = @{}
$namePattern = '^[A-Za-z_]\w*(?:[.+][A-Za-z_]\w*)+$'
$headless = 'HeadlessTests/HappyPhoton.Headless.Tests.csproj'
. (Join-Path $PSScriptRoot 'test-ledger-evidence.ps1')

function Test-LedgerText($Value) {
    return $Value -is [string] -and -not [string]::IsNullOrWhiteSpace($Value)
}

function Test-LedgerProject($Value, [string] $Label) {
    if (-not (Test-LedgerText $Value) -or $Value -notmatch '^[^:]+\.csproj$' -or
        $Value.Contains('\') -or $Value.StartsWith('/') -or
        @($Value.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0) {
        $issues.Add("${Label}: project must be a repo-relative csproj path: $Value.")
        return $false
    }

    if (-not $projects.ContainsKey($Value)) {
        try {
            [xml]$definition = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot $Value)
            $assembly = $definition.SelectSingleNode('/Project/PropertyGroup/AssemblyName')
            $projects[$Value] = if ($null -ne $assembly) { $assembly.InnerText } else {
                [IO.Path]::GetFileNameWithoutExtension($Value)
            }
        } catch {
            $issues.Add("${Label}: cannot read project ${Value}: $($_.Exception.Message)")
            return $false
        }
    }

    return $true
}

function Add-LedgerReference($Reference, [string] $Label) {
    if ($Reference -isnot [Collections.IDictionary]) {
        $issues.Add("${Label}: expected a reference with project and test.")
        return
    }

    $validProject = Test-LedgerProject $Reference['project'] $Label
    $test = $Reference['test']

    if (-not (Test-LedgerText $test) -or $test -cnotmatch $namePattern) {
        $issues.Add("${Label}: invalid fully qualified test: $test.")
    } elseif ($validProject) {
        $requirements.Add(@{ project = $Reference['project']; test = $test; name = $test; case = $false })
    }
}

function Read-LedgerEntry($Entry, [int] $Index) {
    if ($Entry -isnot [Collections.IDictionary]) {
        $issues.Add("Entry ${Index}: expected an object.")
        return
    }

    $label = "Entry $Index [$($Entry['removed'] -join ', ')]"

    foreach ($field in @('kind', 'reference', 'wp', 'date')) {
        if (-not (Test-LedgerText $Entry[$field])) { $issues.Add("${label}: missing or invalid $field.") }
    }

    if ($Entry['reference'] -match '[\r\n]') { $issues.Add("${label}: reference must be one line.") }
    $date = [DateOnly]::MinValue

    if (-not [DateOnly]::TryParseExact([string]$Entry['date'], 'yyyy-MM-dd',
            [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$date)) {
        $issues.Add("${label}: invalid date '$($Entry['date'])'.")
    }

    $project = $Entry['project']
    $validProject = Test-LedgerProject $project $label
    $kind = $Entry['kind']
    $removed = @($Entry['removed'])

    if ($removed.Count -eq 0 -or ($kind -ne 'folded' -and $Entry['removed'] -isnot [string])) {
        $issues.Add("${label}: removed must name one class or method (an array is allowed for folded).")
    }

    foreach ($name in $removed) {
        if (-not (Test-LedgerText $name) -or $name -cnotmatch $namePattern) {
            $issues.Add("${label}: invalid removed name '$name'.")
        } elseif ($validProject -and $kind -ne 'trimmed-cases') {
            $removals.Add(@{ project = $project; name = $name; case = $false })
        }
    }

    switch ($kind) {
        { $_ -in @('duplicate', 'covered-cheaper') } {
            if ($Entry['survivor'] -isnot [array] -or $Entry['survivor'].Count -eq 0) {
                $issues.Add("${label}: survivor must be a nonempty array of references.")
            } else {
                foreach ($reference in $Entry['survivor']) {
                    Add-LedgerReference $reference "$label survivor"

                    if ($kind -eq 'covered-cheaper' -and $reference -is [Collections.IDictionary] -and
                        $reference['project'] -ne 'Tests/HappyPhoton.Tests.csproj') {
                        $issues.Add("${label}: covered-cheaper survivor must be a unit test: $($reference['test']).")
                    }
                }
            }

            if ($kind -eq 'covered-cheaper') {
                Add-LedgerReference $Entry['wiring'] "$label wiring"

                if ($project -ne $headless -or $Entry['wiring'] -isnot [Collections.IDictionary] -or
                    $Entry['wiring']['project'] -ne $headless) {
                    $issues.Add("${label}: covered-cheaper removal and wiring must be headless tests.")
                }
            }
        }

        'removed-behaviour' {
            if (-not (Test-LedgerText $Entry['behaviour'])) { $issues.Add("${label}: missing behaviour.") }
        }

        'low-value' {
            if ($Entry['reason'] -notin @('stores-input', 'interaction-only', 'log-text', 'constant')) {
                $issues.Add("${label}: invalid low-value reason '$($Entry['reason'])'.")
            }
        }

        'folded' {
            Add-LedgerReference $Entry['survivor'] "$label survivor"

            if ($Entry['result'] -notin @('new', 'existing')) { $issues.Add("${label}: result must be new or existing.") }
        }

        'trimmed-cases' {
            $method = $Entry['method']

            if (-not (Test-LedgerText $method) -or $method -cnotmatch $namePattern -or $method -cne $Entry['removed']) {
                $issues.Add("${label}: method must equal the fully qualified removed method.")
                break
            }

            if ($validProject) { $methods.Add(@{ project = $project; name = $method }) }

            foreach ($field in @('removedCases', 'keptCases')) {
                if ($Entry[$field] -isnot [array] -or $Entry[$field].Count -eq 0) {
                    $issues.Add("${label}: $field must be a nonempty array of display names.")
                    continue
                }

                $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

                foreach ($name in $Entry[$field]) {
                    if (-not (Test-LedgerText $name) -or -not $name.StartsWith("$method(", [StringComparison]::Ordinal)) {
                        $issues.Add("${label}: invalid $field display name '$name' for $method.")
                        continue
                    }

                    if (-not $seen.Add($name)) { $issues.Add("${label}: repeated $field display name '$name'.") }
                    if (-not $validProject) { continue }

                    if ($field -eq 'removedCases') {
                        $removals.Add(@{ project = $project; name = $name; case = $true })
                    } else {
                        $requirements.Add(@{ project = $project; test = $method; name = $name; case = $true })
                    }
                }
            }
        }

        default { $issues.Add("${label}: unsupported kind '$kind'.") }
    }
}

function Test-LedgerOverlap($Removal, [string] $Name) {
    return $Removal.name -ceq $Name -or (-not $Removal.case -and
        ($Name.StartsWith("$($Removal.name).", [StringComparison]::Ordinal) -or
         $Name.StartsWith("$($Removal.name)+", [StringComparison]::Ordinal) -or
         $Name.StartsWith("$($Removal.name)(", [StringComparison]::Ordinal)))
}

if (-not [IO.Path]::IsPathRooted($LedgerPath)) { $LedgerPath = Join-Path $RepoRoot $LedgerPath }
$ledger = Get-Content -Raw -LiteralPath $LedgerPath | ConvertFrom-Json -AsHashtable

if ($ledger -isnot [Collections.IDictionary] -or $ledger['schemaVersion'] -ne 1 -or
    $ledger['entries'] -isnot [array]) {
    throw 'Invalid pruning ledger: expected schemaVersion 1 and an entries array.'
}

for ($index = 0; $index -lt $ledger['entries'].Count; $index++) {
    Read-LedgerEntry $ledger['entries'][$index] ($index + 1)
}

for ($index = 0; $index -lt $removals.Count; $index++) {
    $removal = $removals[$index]

    for ($previous = 0; $previous -lt $index; $previous++) {
        $other = $removals[$previous]

        if ($other.project -ceq $removal.project -and
            ((Test-LedgerOverlap $other $removal.name) -or (Test-LedgerOverlap $removal $other.name))) {
            $issues.Add("Removal covered more than once in $($removal.project): $($removal.name); $($other.name).")
        }
    }

    foreach ($reference in $requirements) {
        if ($reference.project -ceq $removal.project -and (Test-LedgerOverlap $removal $reference.name)) {
            $issues.Add("Removed and referenced in $($removal.project): $($removal.name); $($reference.name).")
        }
    }
}

if ($Mode -eq 'Policy') {
    Test-LedgerSources
} else {
    if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) { throw 'ResultsDirectory is required in Results mode.' }
    if (-not [IO.Path]::IsPathRooted($ResultsDirectory)) { $ResultsDirectory = Join-Path $RepoRoot $ResultsDirectory }

    Test-LedgerResults
}

if ($issues.Count -gt 0) { throw "Pruning ledger violations ($($issues.Count)):`n$($issues -join "`n")" }

Write-Host "Pruning ledger $Mode passed ($($ledger['entries'].Count) entries)."
