# Runner, guard, real-source and repository controls for qualify-format.ps1; dot-sourced, never run alone.

# Bounded processes: unlisted programs refused, hung children killed with their tree, output capped.
function Test-ProcessControls {
    $module = Get-Module FormatTools
    $child = Join-Path $script:FormatRoot 'fixtures/child.cjs'
    $marker = 'hp-qualify-' + [guid]::NewGuid().ToString('N')
    $results = & $module {
        param($Scratch, $Child, $Marker)

        $refused = $false

        try {
            [void](Invoke-FormatProcess -Program curl -Arguments @('--version') -WorkingDirectory $Scratch)
        } catch {
            $refused = $_.Exception.Message -like 'PROCESS: executable not allowed*'
        }

        $timedOut = $false

        try {
            [void](Invoke-FormatProcess -Program node -Arguments @($Child, 'hang', $Marker) -WorkingDirectory $Scratch -TimeoutSeconds 2)
        } catch {
            $timedOut = $_.Exception.Message -like 'PROCESS: node timed out*'
        }

        $noisy = Invoke-FormatProcess -Program node -Arguments @($Child, 'noisy') -WorkingDirectory $Scratch -OutputLimit 1000
        [pscustomobject]@{ Refused = $refused; TimedOut = $timedOut; Noisy = $noisy }
    } $script:Scratch $child $marker
    $survivors = @(Get-Process -Name node -ErrorAction SilentlyContinue | Where-Object { "$($_.CommandLine)".Contains($marker) })
    [void](Add-Property 'unlisted executable refused' $results.Refused)
    [void](Add-Property 'hung child killed at its timeout' ($results.TimedOut -and -not $survivors.Count) "survivors=$($survivors.Count)")
    [void](Add-Property 'child output capped' ($results.Noisy.Output.Length -eq 1000 -and $results.Noisy.Truncated))
}

# The semantic guards themselves: whitespace-only output passes, any token, literal or statement change fails.
function Test-GuardControls {
    $module = Get-Module FormatTools
    $guards = $script:Cases.guards
    $result = & $module {
        param($Guards)

        $cs = $Guards.csharp
        $before = Get-CSharpTree $cs.before
        $ps = $Guards.powershell
        $shape = Get-PowerShellShape $ps.before
        [ordered]@{
            'C# whitespace-only accepted' = Test-CSharpEquivalent $before (Get-CSharpTree $cs.whitespace)
            'C# literal change rejected' = -not (Test-CSharpEquivalent $before (Get-CSharpTree $cs.literal))
            'C# token change rejected' = -not (Test-CSharpEquivalent $before (Get-CSharpTree $cs.token))
            'PowerShell whitespace-only accepted' = $shape -ceq (Get-PowerShellShape $ps.whitespace)
            'PowerShell literal change rejected' = $shape -cne (Get-PowerShellShape $ps.literal)
            'PowerShell statement change rejected' = $shape -cne (Get-PowerShellShape $ps.statement)
        }
    } $guards

    foreach ($name in $result.Keys) {
        [void](Add-Property "guard: $name" $result[$name])
    }
}

# AC3: real repository files, copied with their checked-out bytes, hashed as LF text for the cross-platform comparison.
function Test-RealSource {
    $real = @(
        'Program.cs',
        'Services/CatalogService.cs',
        'scripts/verify.ps1',
        'site/assets/js/navigation.js',
        'Tests/Site/Platform.Tests.mjs',
        'site/assets/css/base.css',
        'scripts/shared-policy-tests.json'
    )
    $files = @{}

    foreach ($path in $real) {
        $files[$path] = [IO.File]::ReadAllBytes((Join-Path $script:RepoRoot $path))
    }

    $repo = New-CaseRepo $files
    $apply = Invoke-Format apply $repo $real
    $check = Invoke-Format check $repo $real
    [void](Add-Property 'real source formats cleanly' ($apply.ExitCode -eq 0 -and $check.ExitCode -eq 0) "$($apply.Text)`n$($check.Text)")

    foreach ($path in $real) {
        $script:Hashes["real:$path"] = Get-LfHash ([IO.File]::ReadAllBytes((Join-Path $repo $path)))
    }
}

function Get-EligibleFiles {
    param([string]$Prefix = '.')

    $module = Get-Module FormatTools
    $scope = Get-Content -LiteralPath (Join-Path $script:FormatRoot 'config/scope.json') -Raw | ConvertFrom-Json
    $listed = @(& git -C $script:RepoRoot -c core.quotepath=false ls-files --cached --others --exclude-standard --full-name -- $Prefix)

    return @(& $module {
            param($Listed, $Patterns, $Root)

            foreach ($path in $Listed) {
                $eligible = (Get-FormatFamily $path) -and -not (Test-FormatExcluded $path $Patterns)

                if ($eligible -and (Test-Path -LiteralPath (Join-Path $Root $path) -PathType Leaf)) {
                    $path
                }
            }
        } $listed $scope.excluded $script:RepoRoot)
}

# The tooling is its own first adopted scope: every formatter-supported file under scripts/format is clean.
function Test-SelfCheck {
    $prefix = [IO.Path]::GetRelativePath($script:RepoRoot, $script:FormatRoot).Replace('\', '/')
    $files = Get-EligibleFiles $prefix
    $before = (& git -C $script:RepoRoot status --porcelain) -join "`n"
    $check = Invoke-Format check $script:RepoRoot $files
    $after = (& git -C $script:RepoRoot status --porcelain) -join "`n"
    [void](Add-Property 'formatter tooling is clean under its own check' ($check.ExitCode -eq 0 -and $files.Count) $check.Text)
    [void](Add-Property 'check leaves the repository untouched' ($before -ceq $after))
}

# AC7: a read-only drift report of every eligible legacy file, written outside the repository.
function Invoke-DriftReport {
    param([string]$Report)

    $files = Get-EligibleFiles
    $before = (& git -C $script:RepoRoot status --porcelain) -join "`n"
    $check = Invoke-Format check $script:RepoRoot $files
    $after = (& git -C $script:RepoRoot status --porcelain) -join "`n"
    [IO.File]::WriteAllText($Report, $check.Text + "`n")
    [void](Add-Property 'drift report produced read-only' ($check.ExitCode -in @(0, 1) -and $before -ceq $after) ($check.Text -split "`n")[-1])
}

# AC5: every launched process is a formatter, version probe or file listing; nothing restores or builds.
function Test-Invocations {
    $restoreOrBuild = @($script:Ledger | Where-Object {
            ($_ -like 'dotnet *' -and $_ -notlike 'dotnet --version' -and $_ -notlike 'dotnet format whitespace * --folder *') -or
            $_ -match '(?i)\b(restore|msbuild)\b'
        })
    $unexpected = @($script:Ledger | Where-Object { ($_ -split ' ')[0] -notin @('git', 'dotnet', 'node') })
    $artifacts = @(Get-ChildItem -LiteralPath $script:Scratch -Recurse -Directory -Force |
            Where-Object { $_.Name -in @('obj', 'bin') -and $_.FullName -notmatch '[\\/](fake-tools|format-copy)[\\/]' })
    $script:RestoreBuild = $restoreOrBuild.Count + $artifacts.Count
    [void](Add-Property 'zero restore or build invocations' (-not $script:RestoreBuild) (($restoreOrBuild + @($artifacts | ForEach-Object FullName)) -join '; '))
    [void](Add-Property 'only git, dotnet and node launched' (-not $unexpected.Count) ($unexpected -join '; '))
    [void](Add-Property 'process ledger observed' ($script:Ledger.Count -gt 0) "$($script:Ledger.Count) launches")
}
