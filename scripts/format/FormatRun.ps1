function Get-FullDirectory {
    param([string]$Path)

    $full = [IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path))

    return $full.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

function Test-PathUnder {
    param([string]$Path, [string]$Root)

    $comparison = [StringComparison]::Ordinal

    if ($IsWindows) {
        $comparison = [StringComparison]::OrdinalIgnoreCase
    }

    return $Path.Equals($Root, $comparison) -or $Path.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, $comparison)
}

function New-FormatScratch {
    param([string]$RepoRoot, [string]$ToolRoot)

    $scratch = Get-FullDirectory (Join-Path ([IO.Path]::GetTempPath()) ('hp-format-' + [guid]::NewGuid().ToString('N')))

    if ((Test-PathUnder $scratch $RepoRoot) -or (Test-PathUnder $scratch $ToolRoot)) {
        throw 'SCRATCH: the platform temp directory is inside the repository or tool root'
    }

    [void][IO.Directory]::CreateDirectory($scratch)
    $pins = Get-FormatPins
    $globalJson = "{ `"sdk`": { `"version`": `"$($pins.sdk)`", `"rollForward`": `"disable`", `"allowPrerelease`": false } }"
    [IO.File]::WriteAllText((Join-Path $scratch 'global.json'), $globalJson)
    Copy-Item -LiteralPath (Join-Path $script:FormatHome 'config/csharp.editorconfig') -Destination (Join-Path $scratch '.editorconfig')

    return $scratch
}

function Get-PhysicalLineCount {
    param([string]$Text)

    if ($Text.Length -eq 0) {
        return 0
    }

    $count = [regex]::Matches($Text, "`n").Count

    if (-not $Text.EndsWith("`n")) {
        $count++
    }

    return $count
}

function Complete-FormatCheck {
    param($Check)

    if ($null -ne $Check.Canonical) {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Check.Canonical.Replace("`n", $Check.Newline))

        if ($Check.Bom) {
            $bytes = [byte[]](@(0xEF, 0xBB, 0xBF) + $bytes)
        }

        $Check.Output = [byte[]]$bytes

        if ([Convert]::ToBase64String($Check.Bytes) -cne [Convert]::ToBase64String($Check.Output)) {
            $Check.Findings.Insert(0, 'DRIFT')
        }
    }

    $text = $Check.Canonical

    if ($null -eq $text) {
        $text = [string]$Check.Text
    }

    $lines = Get-PhysicalLineCount $text

    if (-not $Check.Path.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase) -and $lines -gt 499) {
        Add-FormatFinding $Check 'SIZE' "$lines lines after formatting; the limit is 499"
    }
}

# Writes only bytes formatted from what is still on disk, through no link, via a sibling temp file and an
# atomic replace. Every failure is a finding for this file, so a run that has written never exits 2.
function Write-FormatFile {
    param($Check)

    $temp = $null

    try {
        # Selection checked links too; checking again here leaves only the instant before the replace.
        if ((Get-RootLinkState $Check.Root) -cne $Check.RootLinks) {
            throw 'SELECTION: the repository root now resolves to a different folder; a link on its path changed during the run'
        }

        Assert-NoLink $Check.Root $Check.Path
        $current = [IO.File]::ReadAllBytes($Check.File)

        if ([Convert]::ToBase64String($current) -cne [Convert]::ToBase64String($Check.Bytes)) {
            Add-FormatFinding $Check 'CHANGED' 'the file changed while it was being formatted; nothing was written'
            return
        }

        $directory = [IO.Path]::GetDirectoryName($Check.File)
        $temp = Join-Path $directory ('.' + [IO.Path]::GetFileName($Check.File) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
        [IO.File]::WriteAllBytes($temp, $Check.Output)

        if (-not $IsWindows) {
            [IO.File]::SetUnixFileMode($temp, [IO.File]::GetUnixFileMode($Check.File))
        }

        [IO.File]::Move($temp, $Check.File, $true)
        $Check.Findings.Clear()
        $Check.Findings.Add('FORMATTED')
    } catch {
        Add-FormatFinding $Check 'WRITE' $_.Exception.Message
    } finally {
        try {
            if ($temp -and [IO.File]::Exists($temp)) {
                [IO.File]::Delete($temp)
            }
        } catch {
            Add-FormatFinding $Check 'WRITE' "temporary file left behind: $temp"
        }
    }
}

function Invoke-FormatRun {
    param([string]$RepoRoot, [string]$ToolRoot, [string[]]$Path, [switch]$Apply)

    $scratch = New-FormatScratch $RepoRoot $ToolRoot

    try {
        $selection = @(Resolve-FormatSelection $RepoRoot $Path $scratch)
        $prettier = Assert-FormatTools $ToolRoot $scratch
        $checks = @($selection | ForEach-Object { New-FormatCheck $_ })

        foreach ($family in @('csharp', 'powershell', 'prettier')) {
            $ready = @($checks | Where-Object { $_.Family -eq $family -and $null -ne $_.Text -and -not $_.Findings.Count })

            if (-not $ready.Count) {
                continue
            }

            switch ($family) {
                'csharp' { Format-CSharpBatch $ready $scratch }
                'powershell' { Format-PowerShellBatch $ready }
                'prettier' { Format-PrettierBatch $ready $scratch $prettier }
            }
        }

        # Every verdict is settled before the first write; a failure up to here writes nothing.
        foreach ($check in $checks) {
            Complete-FormatCheck $check
        }

        if ($Apply) {
            foreach ($check in $checks) {
                if ($check.Findings.Count -eq 1 -and $check.Findings[0] -eq 'DRIFT') {
                    Write-FormatFile $check
                }
            }
        }

        $lines = [Collections.Generic.List[string]]::new()
        $remaining = 0
        $formatted = 0

        foreach ($check in $checks) {
            $verdict = 'OK'

            if ($check.Findings.Count) {
                $verdict = $check.Findings -join '+'
            }

            if ($verdict -eq 'FORMATTED') {
                $formatted++
            } elseif ($verdict -ne 'OK') {
                $remaining++
            }

            $line = "$verdict $($check.Path)"

            if ($check.Details.Count) {
                $line += ' - ' + ($check.Details -join '; ')
            }

            $lines.Add($line)
        }

        $exitCode = 0

        if ($remaining) {
            $exitCode = 1
        }

        $lines.Add("SUMMARY files=$($checks.Count) formatted=$formatted findings=$remaining")
        return [pscustomobject]@{ ExitCode = $exitCode; Lines = $lines }
    } finally {
        Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-FormatCommand {
    param([string[]]$Path, [string]$RepoRoot, [string]$ToolRoot, [switch]$Apply)

    try {
        if (-not $RepoRoot) {
            $RepoRoot = Join-Path $script:FormatHome '../..'
        }

        if (-not $ToolRoot) {
            $ToolRoot = Join-Path $script:FormatHome '../../.tools/format'
        }

        return Invoke-FormatRun (Get-FullDirectory $RepoRoot) (Get-FullDirectory $ToolRoot) $Path -Apply:$Apply
    } catch {
        # Unknown is never success: refusals, tool problems and formatter failures report no verdicts.
        $message = $_.Exception.Message

        if ($message -notmatch '^[A-Z-]+:') {
            $message = "ERROR: $message"
        }

        return [pscustomobject]@{ ExitCode = 2; Lines = @($message, 'SUMMARY no verdicts; exit 2') }
    }
}
