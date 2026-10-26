function Test-ProfileOptInClass($Class) {
    return @($Class.methods | Where-Object { $_.condition.category -eq 'opt-in' }).Count -gt 0
}

function Test-ProfileKeepCitation([string] $Relative, [bool] $TopSpec, [string] $Name, [string] $Text) {
    $frontmatter = [regex]::Match($Text, '(?s)\A---\r?\n(.*?)\r?\n---(?:\r?\n|$)')
    $approved = $frontmatter.Success -and $frontmatter.Groups[1].Value -match '(?m)^status:[ \t]*approved[ \t]*\r?$'

    return $Relative -match '^docs/pipeline/[^/]+\.md$|^scripts/[^/]+\.ps1$' -or
        ($TopSpec -and $approved -and $Name -ne 'TESTSUITE.md')
}
function Get-ProfileRunnerWorkloads($Citations) {
    foreach ($group in @($Citations | Where-Object { $_.path.EndsWith('.ps1') } | Group-Object path)) {
        $tokens = $null
        $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($group.Name, [ref]$tokens, [ref]$errors)
        $environment = @($ast.FindAll({ param($node)
            $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -match '\$env:'
        }, $true) | ForEach-Object { $_.Extent.Text })
        [pscustomobject]@{
            runner = $group.Name
            parameters = if ($ast.ParamBlock) { $ast.ParamBlock.Extent.Text } else { '' }
            environmentAssignments = $environment
            workloadReferences = @($group.Group | Select-Object line, text)
        }
    }
}

function Write-ProfileReport($Data, [string] $Path) {
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('# Test suite profile')
    $lines.Add('')
    $lines.Add('Memory is PagedMemorySize64, decimal GB. Class wall is the sum of TRX durations; concurrent cases can overlap. Growth is positive sample deltas split equally among active cases, a screening signal only.')
    $lines.Add('')

    foreach ($hostName in @('HappyPhoton.Tests', 'HappyPhoton.Headless.Tests')) {
        $classes = @($Data.classes | Where-Object host -eq $hostName)
        $lines.Add("## $hostName")
        $lines.Add('')

        foreach ($workload in @('nightly', 'solo')) {
            $lines.Add("### Twenty slowest always-on classes ($workload)")
            $lines.Add('')
            $lines.Add('| Class | TRX seconds |')
            $lines.Add('|---|---:|')
            $ranked = @(foreach ($class in $classes | Where-Object category -eq 'always-on') {
                foreach ($measurement in $class.measurements | Where-Object workload -eq $workload) {
                    @{ class = $class.class; seconds = $measurement.wallSeconds }
                }
            })

            foreach ($row in $ranked | Sort-Object seconds -Descending | Select-Object -First 20) {
                $lines.Add("| $($row.class) | $($row.seconds) |")
            }

            $lines.Add('')
        }

        $lines.Add('### Opt-in workloads')
        $lines.Add('')
        $lines.Add('| Class | Environment variables | Runner workloads | Citations |')
        $lines.Add('|---|---|---|---|')

        foreach ($class in $classes | Where-Object { Test-ProfileOptInClass $_ } | Sort-Object class) {
            $runners = ($class.runnerWorkloads | ForEach-Object {
                $runner = $_.runner

                $_.workloadReferences | ForEach-Object { "${runner}:$($_.line) $($_.text)" }
            }) -join '; '
            $references = ($class.citations | ForEach-Object { "$($_.path):$($_.line)" }) -join '; '
            $lines.Add("| $($class.class) | $($class.variables -join ', ') | $($runners.Replace('|', '\|')) | $references |")
        }

        $lines.Add('')
    }

    $lines.Add('## Retire candidates under D-3')
    $lines.Add('')

    if (-not $Data.retireTableReady) {
        $lines.Add("BLOCKED: $($Data.unresolved.Count) unresolved source/discovery/TRX conditions or missing measurement evidence. Resolve inventory.json entries and regenerate; no retire recommendations are emitted.")
    } else {
        $lines.Add('| Project | Class | Variables | Reason | Informational citations |')
        $lines.Add('|---|---|---|---|---|')

        foreach ($class in $Data.classes | Where-Object { (Test-ProfileOptInClass $_) -and @($_.citations | Where-Object keepQualifying).Count -eq 0 }) {
            $references = ($class.citations | ForEach-Object { "$($_.path):$($_.line)" }) -join '; '
            $lines.Add("| $($class.project) | $($class.class) | $($class.variables -join ', ') | No approved board spec, pipeline doc or scripts/*.ps1 citation | $references |")
        }
    }

    $lines | Set-Content -LiteralPath $Path
}
