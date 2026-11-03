# Enforces the adopted formatter scope; verify.ps1, repository CI and site CI all run this. It first proves, in a
# temporary repository with its own two-prefix scope, that clean adopted files pass, drift fails, and excluded or
# unadopted files are neither reported nor written. Then it checks the real adopted scope. It writes no repository file.
# Exit 0: every scenario held and the adopted scope is clean; 1: findings or a failed scenario; 2: could not run.
[CmdletBinding()]
param([string]$ToolRoot)

$version = $PSVersionTable.PSVersion

if ($version.Major -lt 7 -or ($version.Major -eq 7 -and $version.Minor -lt 4)) {
    Write-Output 'VERSION: PowerShell 7.4 or later is required'
    exit 2
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-TrackedState {
    param([string]$Root, [string]$Scratch)

    $listing = Invoke-FormatProcess -Program git -Arguments @('-c', 'core.quotepath=false', 'ls-files', '-z') `
        -WorkingDirectory $Root -Environment (Get-ChildEnvironment $Scratch) -TimeoutSeconds 120 -OutputLimit 67108864

    $entries = foreach ($path in $listing.Output.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)) {
        $file = [IO.FileInfo]::new((Join-Path $Root $path))

        if ($file.Exists) {
            "$path $($file.Length) $($file.LastWriteTimeUtc.Ticks)"
        } else {
            "$path missing"
        }
    }

    return @($entries) -join "`n"
}

function Get-ScratchState {
    param([string]$Root)

    $entries = foreach ($item in Get-ChildItem -LiteralPath $Root -Recurse -File -Force) {
        $relative = [IO.Path]::GetRelativePath($Root, $item.FullName).Replace('\', '/')

        if (-not $relative.StartsWith('.git/')) {
            "$relative $((Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash)"
        }
    }

    return (@($entries) | Sort-Object -CaseSensitive) -join "`n"
}

function Write-Sample {
    param([string]$Root, [string]$Path, [string]$Text)

    $file = Join-Path $Root $Path
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file))
    [IO.File]::WriteAllBytes($file, [Text.UTF8Encoding]::new($false).GetBytes($Text))
}

function Invoke-AdoptedCheck {
    param([string]$FormatRoot, [string]$Repo)

    $lines = @(& (Join-Path $FormatRoot 'check-format.ps1') -Adopted -RepoRoot $Repo -ToolRoot $ToolRoot)
    $exitCode = $LASTEXITCODE
    $verdicts = @($lines | Where-Object { $_ -notmatch '^SUMMARY ' } | Sort-Object -CaseSensitive)

    return [pscustomobject]@{ ExitCode = $exitCode; Lines = $lines; Verdicts = $verdicts -join "`n" }
}

# One clean or dirty run of the scenario repository; its bytes must be unchanged afterwards.
function Invoke-Scenario {
    param([string]$FormatRoot, [string]$Repo, $Samples, [string]$Content)

    foreach ($sample in $Samples) {
        Write-Sample $Repo $sample.Adopted $sample.Case.$Content
    }

    $before = Get-ScratchState $Repo
    $result = Invoke-AdoptedCheck $FormatRoot $Repo
    $result | Add-Member Unchanged ((Get-ScratchState $Repo) -ceq $before)

    return $result
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Import-Module (Join-Path $PSScriptRoot 'FormatTools.psm1') -Force

if (-not $ToolRoot) {
    $ToolRoot = Get-DefaultToolRoot $repoRoot
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) ('hp-format-adopted-' + [guid]::NewGuid().ToString('N'))
$exitCode = 2

try {
    [void][IO.Directory]::CreateDirectory($scratch)
    $tracked = Get-TrackedState $repoRoot $scratch

    # The scenarios use a copy of this formatter whose scope adopts two controlled prefixes, so enrolling real
    # folders never breaks them; the real exclusions still apply.
    $format = Join-Path $scratch 'format'
    [void][IO.Directory]::CreateDirectory((Join-Path $format 'config'))
    Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Extension -in @('.ps1', '.psm1', '.psd1') |
        Copy-Item -Destination $format
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'config') -File | Copy-Item -Destination (Join-Path $format 'config')
    $scope = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'config/scope.json') -Raw | ConvertFrom-Json
    $controlled = [ordered]@{ adopted = @('scripts/format/', 'site/adopted/'); excluded = @($scope.excluded) }
    [IO.File]::WriteAllText((Join-Path $format 'config/scope.json'), ($controlled | ConvertTo-Json) + "`n")

    $repo = Join-Path $scratch 'repo'
    [void][IO.Directory]::CreateDirectory($repo)
    [void](Invoke-FormatProcess -Program git -Arguments @('-c', 'init.defaultBranch=main', 'init', '-q') `
            -WorkingDirectory $repo -Environment (Get-ChildEnvironment $scratch) -TimeoutSeconds 60)
    $cases = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/cases.json') -Raw | ConvertFrom-Json).parsers
    $adopted = @{ csharp = 'scripts/format/samples/Sample.cs'; powershell = 'scripts/format/Sample.ps1'; javascript = 'site/adopted/sample.js' }

    $samples = @(foreach ($name in @('csharp', 'powershell', 'javascript')) {
            $case = @($cases | Where-Object name -eq $name)[0]
            [pscustomobject]@{ Case = $case; Adopted = $adopted[$name] }
        })

    $untouched = [Collections.Generic.List[string]]::new()

    # Excluded files inside an adopted prefix and unadopted legacy files, all deliberately dirty.
    foreach ($sample in $samples) {
        $leaf = [IO.Path]::GetFileName($sample.Case.path)

        foreach ($path in @("scripts/format/fixtures/$leaf", $sample.Case.path)) {
            Write-Sample $repo $path $sample.Case.dirty
            $untouched.Add($path)
        }
    }

    $clean = Invoke-Scenario $format $repo $samples 'expected'
    $drift = Invoke-Scenario $format $repo $samples 'dirty'
    $refusal = @($clean, $drift | Where-Object { $_.ExitCode -eq 2 })

    # A refusal (missing tools, a bad pin) means no scenario ran; report it once instead of as three failures.
    if ($refusal.Count) {
        $refusal[0].Lines
        Write-Output 'RESULT the scenarios could not run; exit 2'
    } else {
        $expectedClean = @($samples | ForEach-Object { "OK $($_.Adopted)" } | Sort-Object -CaseSensitive) -join "`n"
        $expectedDrift = @($samples | ForEach-Object { "DRIFT $($_.Adopted)" } | Sort-Object -CaseSensitive) -join "`n"
        $output = @($clean.Lines) + @($drift.Lines)
        $listed = @($untouched | Where-Object { $path = $_; @($output | Where-Object { $_ -like "* $path*" }).Count })
        $scenarios = [ordered]@{
            clean = $clean.ExitCode -eq 0 -and $clean.Verdicts -ceq $expectedClean -and $clean.Unchanged
            drift = $drift.ExitCode -eq 1 -and $drift.Verdicts -ceq $expectedDrift -and $drift.Unchanged
            excluded = -not $listed.Count -and $clean.Unchanged -and $drift.Unchanged
        }

        foreach ($name in $scenarios.Keys) {
            $verdict = if ($scenarios[$name]) { 'pass' } else { 'FAIL' }
            Write-Output "SCENARIO $name`: $verdict"
        }

        if (-not $scenarios.clean -or -not $scenarios.drift) {
            $output | ForEach-Object { "  $_" }
        }

        if ($listed.Count) {
            Write-Output "  reported excluded or unadopted files: $($listed -join ', ')"
        }

        $real = Invoke-AdoptedCheck $PSScriptRoot $repoRoot
        $real.Lines
        $writes = (Get-TrackedState $repoRoot $scratch) -cne $tracked
        $passed = @($scenarios.Values | Where-Object { $_ }).Count
        Write-Output "RESULT scenarios=$passed/3 adopted=exit$($real.ExitCode) trackedWrites=$(if ($writes) { 'yes' } else { 0 })"

        if ($real.ExitCode -eq 2) {
            $exitCode = 2
        } elseif ($passed -ne 3 -or $real.ExitCode -ne 0 -or $writes) {
            $exitCode = 1
        } else {
            $exitCode = 0
        }
    }
} catch {
    Write-Output "ERROR: $($_.Exception.Message)"
    $exitCode = 2
} finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}

exit $exitCode
