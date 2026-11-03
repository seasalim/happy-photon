# Control groups for qualify-format.ps1; dot-sourced after QualifyHelpers.ps1, never run alone.

function Get-CaseFamily {
    param([string]$Path)

    switch ([IO.Path]::GetExtension($Path)) {
        '.cs' { return 'csharp' }
        '.ps1' { return 'powershell' }
        default { return 'prettier' }
    }
}

# AC2: dirty input is rejected without writing, repaired to the pinned output, then stable.
function Test-ParserCases {
    $unselected = $script:Cases.parsers[0].dirty

    foreach ($case in $script:Cases.parsers) {
        $passed = $true

        foreach ($newline in @("`n", "`r`n")) {
            $label = "$($case.name) $(@{ "`n" = 'lf'; "`r`n" = 'crlf' }[$newline])"
            $file = $case.path
            $repo = New-CaseRepo @{ $file = $case.dirty.Replace("`n", $newline); 'other/Unselected.cs' = $unselected }
            $target = Join-Path $repo $file
            $before = Get-TreeState $repo
            $check = Invoke-Format check $repo @($file)
            $passed = (Add-Property "$label check rejects drift" (Test-Verdict $check 1 "(?m)^DRIFT $([regex]::Escape($file))$") $check.Text) -and $passed
            $passed = (Add-Property "$label check writes nothing" ((Get-TreeState $repo) -ceq $before)) -and $passed
            $unselectedBytes = [IO.File]::ReadAllBytes((Join-Path $repo 'other/Unselected.cs'))
            $apply = Invoke-Format apply $repo @($file)
            $actual = [IO.File]::ReadAllBytes($target)
            $expected = Get-Utf8Bytes $case.expected.Replace("`n", $newline)
            $passed = (Add-Property "$label apply repairs to the pinned output" ($apply.ExitCode -eq 0 -and (Test-SameBytes $actual $expected)) $apply.Text) -and $passed
            $text = [Text.Encoding]::UTF8.GetString($actual).Replace("`r`n", "`n")
            $missing = @($case.literals | Where-Object { -not $text.Contains($_) })
            $passed = (Add-Property "$label literal values unchanged" (-not $missing.Count) ($missing -join ' | ')) -and $passed
            $isolated = Test-SameBytes ([IO.File]::ReadAllBytes((Join-Path $repo 'other/Unselected.cs'))) $unselectedBytes
            $passed = (Add-Property "$label unselected file untouched" $isolated) -and $passed
            $recheck = Invoke-Format check $repo @($file)
            $passed = (Add-Property "$label check passes after apply" (Test-Verdict $recheck 0 '(?m)^OK ') $recheck.Text) -and $passed
            $stamp = (Get-Item -LiteralPath $target).LastWriteTimeUtc
            $again = Invoke-Format apply $repo @($file)
            $stable = (Test-Verdict $again 0 '(?m)^OK ') -and (Test-SameBytes ([IO.File]::ReadAllBytes($target)) $expected) -and
            (Get-Item -LiteralPath $target).LastWriteTimeUtc -eq $stamp
            $passed = (Add-Property "$label second apply changes nothing" $stable $again.Text) -and $passed

            if ($newline -eq "`n") {
                $script:Hashes["fixture:$($case.name)"] = Get-LfHash $actual
                $copy = New-CaseRepo @{ $file = $case.dirty }
                [void](Invoke-Format apply $copy @($file))
                $same = Test-SameBytes ([IO.File]::ReadAllBytes((Join-Path $copy $file))) $actual
                $passed = (Add-Property "$label output reproducible" $same) -and $passed
            }
        }

        $family = Get-CaseFamily $case.path

        if (-not $script:Families.ContainsKey($family)) {
            $script:Families[$family] = $true
        }

        $script:Families[$family] = $script:Families[$family] -and $passed
    }
}

function Assert-Refused {
    param([string]$Name, [string]$Repo, [string]$Code, [string]$Path)

    $before = Get-TreeState $Repo
    $result = Invoke-Format apply $Repo @($Path)
    $ok = (Test-Verdict $result 1 "(?m)^$Code[+ ]") -and (Get-TreeState $Repo) -ceq $before
    [void](Add-Property $Name $ok $result.Text)
}

# Findings a person must resolve: apply never writes these files.
function Test-ContentControls {
    foreach ($case in $script:Cases.parseErrors) {
        $repo = New-CaseRepo @{ $case.path = $case.text }
        Assert-Refused "parse error refused: $($case.path)" $repo 'PARSE' $case.path
    }

    foreach ($case in $script:Cases.semantics) {
        $files = @{}

        foreach ($file in $case.files.PSObject.Properties) {
            $files[$file.Name] = $file.Value
        }

        $repo = New-CaseRepo $files
        Assert-Refused "meaning-changing output refused: $($case.name)" $repo 'SEMANTICS' $case.path
    }

    $repo = New-CaseRepo @{
        'scripts/Mixed.ps1' = "`$a = 1`r`n`$b = 2`n"
        'site/bad.json' = [byte[]](0x7B, 0xFF, 0x7D, 0x0A)
        'scripts/Bom.ps1' = [byte[]](@(0xEF, 0xBB, 0xBF) + (Get-Utf8Bytes "`$name='caf$([char]0xE9)'`n"))
    }
    Assert-Refused 'mixed line endings refused' $repo 'LINE-ENDINGS' 'scripts/Mixed.ps1'
    Assert-Refused 'invalid UTF-8 refused' $repo 'ENCODING' 'site/bad.json'
    $apply = Invoke-Format apply $repo @('scripts/Bom.ps1')
    $bytes = [IO.File]::ReadAllBytes((Join-Path $repo 'scripts/Bom.ps1'))
    $expected = [byte[]](@(0xEF, 0xBB, 0xBF) + (Get-Utf8Bytes "`$name = 'caf$([char]0xE9)'`n"))
    [void](Add-Property 'BOM and non-ASCII text preserved' ($apply.ExitCode -eq 0 -and (Test-SameBytes $bytes $expected)) $apply.Text)

    # AC6: canonical output over 499 lines is refused; JSON is exempt from the limit.
    $size = $script:Cases.size
    $items = @(1..$size.count | ForEach-Object { $size.item -f $_ }) -join ','
    $repo = New-CaseRepo @{ $size.jsPath = $size.jsPrefix + $items + $size.jsSuffix; $size.jsonPath = "[$items]`n" }
    Assert-Refused 'over 499 lines after formatting refused' $repo 'DRIFT\+SIZE' $size.jsPath
    $json = Invoke-Format apply $repo @($size.jsonPath)
    $lines = [IO.File]::ReadAllLines((Join-Path $repo $size.jsonPath)).Count
    [void](Add-Property 'JSON exempt from the line limit' ($json.ExitCode -eq 0 -and $lines -gt 499) "$lines lines; $($json.Text)")

    if (-not $IsWindows) {
        $repo = New-CaseRepo @{ 'scripts/Run.ps1' = "`$a=1`n" }
        $file = Join-Path $repo 'scripts/Run.ps1'
        [IO.File]::SetUnixFileMode($file, [IO.UnixFileMode]'UserRead, UserWrite, UserExecute, GroupRead, GroupExecute, OtherRead, OtherExecute')
        $mode = [IO.File]::GetUnixFileMode($file)
        [void](Invoke-Format apply $repo @('scripts/Run.ps1'))
        [void](Add-Property 'executable mode preserved on apply' ([IO.File]::GetUnixFileMode($file) -eq $mode) "$([IO.File]::GetUnixFileMode($file))")
    }
}

# Fail-closed selection: any refused path stops the whole run before a single write.
function Test-SelectionControls {
    $outside = Join-Path $script:Scratch 'outside'
    [void][IO.Directory]::CreateDirectory($outside)
    [IO.File]::WriteAllText((Join-Path $outside 'Linked.cs'), $script:Cases.parsers[0].dirty)
    $repo = New-CaseRepo @{
        'src/Valid.cs' = $script:Cases.parsers[0].dirty
        '.gitignore' = "ignored/`n"
        'ignored/Skip.cs' = $script:Cases.parsers[0].dirty
        'Tests/assets/data.json' = "{`"a`":1}`n"
        'src/fixtures/Case.cs' = $script:Cases.parsers[0].dirty
        'packages.lock.json' = "{`"a`":1}`n"
        'Views/A.axaml' = "<UserControl />`n"
        'site/page.html' = "<p>a</p>`n"
        'notes.md' = "# notes`n"
    }
    $link = Join-Path $repo 'src/linked'

    if ($IsWindows) {
        [void](New-Item -ItemType Junction -Path $link -Target $outside)
    } else {
        [void](New-Item -ItemType SymbolicLink -Path $link -Target $outside)
    }

    $refusals = [ordered]@{
        'missing file' = @('src/Missing.cs')
        'absolute path' = @((Join-Path $repo 'src/Valid.cs'))
        'parent segment' = @('../outside/Linked.cs')
        'dot segment' = @('./src/Valid.cs')
        'ignored file' = @('ignored/Skip.cs')
        'excluded test asset' = @('Tests/assets/data.json')
        'excluded fixture' = @('src/fixtures/Case.cs')
        'excluded lockfile' = @('packages.lock.json')
        'unsupported AXAML' = @('Views/A.axaml')
        'unsupported HTML (deferred)' = @('site/page.html')
        'unsupported Markdown' = @('notes.md')
        'directory' = @('src')
        'wrong case' = @('SRC/Valid.cs')
        'named twice' = @('src/Valid.cs')
        'through a link' = @('src/linked/Linked.cs')
    }
    $before = Get-TreeState $repo

    foreach ($name in $refusals.Keys) {
        $result = Invoke-Format apply $repo (@('src/Valid.cs') + $refusals[$name])
        $ok = (Test-Verdict $result 2 '(?m)^SELECTION: ') -and (Get-TreeState $repo) -ceq $before
        [void](Add-Property "selection refused, nothing written: $name" $ok $result.Text)
    }

    $empty = Invoke-FormatChild check $repo @() $script:ToolRoot
    [void](Add-Property 'selection refused: no paths' (Test-Verdict $empty 2 '(?m)^SELECTION: ') $empty.Text)
    $child = Invoke-FormatChild apply $repo @('src/Valid.cs') $script:ToolRoot
    [void](Add-Property 'entrypoint runs as a child process' (Test-Verdict $child 0 '(?m)^FORMATTED src/Valid.cs$') $child.Text)
}

# AC4: missing or mismatched tools fail before any write and are never replaced by a download.
function Test-ToolControls {
    $repo = New-CaseRepo @{ 'src/A.cs' = $script:Cases.parsers[0].dirty; 'site/a.js' = $script:Cases.parsers[2].dirty }
    $files = @('src/A.cs', 'site/a.js')
    $before = Get-TreeState $repo
    $empty = Join-Path $script:Scratch 'no-tools'
    [void][IO.Directory]::CreateDirectory($empty)
    $result = Invoke-FormatChild apply $repo $files $empty
    $ok = (Test-Verdict $result 2 '(?m)^VERSION: formatter tools are missing') -and -not @(Get-ChildItem -LiteralPath $empty -Force).Count
    [void](Add-Property 'missing tools fail without a download' ($ok -and (Get-TreeState $repo) -ceq $before) $result.Text)

    $fake = Join-Path $script:Scratch 'fake-tools'
    $pssa = Join-Path $fake 'PSScriptAnalyzer'
    Copy-Item -LiteralPath (Join-Path $script:ToolRoot 'PSScriptAnalyzer') -Destination $pssa -Recurse
    $bin = Join-Path $fake 'node_modules/prettier/bin'
    [void][IO.Directory]::CreateDirectory($bin)
    Copy-Item -LiteralPath (Join-Path $script:FormatRoot 'fixtures/fake-prettier.cjs') -Destination (Join-Path $bin 'prettier.cjs')
    $fakeBefore = Get-TreeState $fake
    $modes = [ordered]@{
        version = @(2, '(?m)^VERSION: Prettier 3\.9\.9 is required', 'mismatched Prettier version refused')
        'write-crash' = @(2, '(?m)^PROCESS: node exited 1', 'formatter failure while writing stops the run')
        crash = @(2, '(?m)^PROCESS: Prettier failed on site/a\.js: \[error\] fake formatter crash', 'formatter crash stops the run')
    }

    # A formatter that fails without a parser diagnostic is a tool failure: no verdicts, nothing written.
    foreach ($mode in $modes.Keys) {
        $env:HP_FAKE_PRETTIER = $mode
        $result = Invoke-FormatChild apply $repo $files $fake
        $expect = $modes[$mode]
        $unchanged = (Get-TreeState $repo) -ceq $before
        [void](Add-Property $expect[2] ((Test-Verdict $result $expect[0] $expect[1]) -and $unchanged) $result.Text)
    }

    $env:HP_FAKE_PRETTIER = $null
    $repopulated = (Get-TreeState $fake) -cne $fakeBefore -or @(Get-ChildItem -LiteralPath $empty -Force).Count
    [void](Add-Property 'tool roots never repopulated' (-not $repopulated))
    $manifest = @(Get-ChildItem -LiteralPath $pssa -Recurse -Filter 'PSScriptAnalyzer.psd1')[0].FullName
    [IO.File]::AppendAllText($manifest, "`n# altered")
    $result = Invoke-FormatChild check $repo $files $fake
    $kept = [IO.File]::ReadAllText($manifest).EndsWith('# altered')
    $refused = (Test-Verdict $result 2 '(?m)^VERSION: PSScriptAnalyzer manifest hash') -and $kept
    [void](Add-Property 'altered PSScriptAnalyzer refused, not replaced' $refused $result.Text)

    # An unavailable SDK pin fails rather than rolling forward to an installed SDK.
    $before = Get-TreeState $repo
    $copy = Join-Path $script:Scratch 'format-copy'
    Copy-Item -LiteralPath $script:FormatRoot -Destination $copy -Recurse
    $versions = Join-Path $copy 'config/versions.json'
    $pins = Get-Content -LiteralPath $versions -Raw | ConvertFrom-Json
    $pins.sdk = '10.0.199'
    [IO.File]::WriteAllText($versions, ($pins | ConvertTo-Json))
    $result = Invoke-FormatChild apply $repo $files $script:ToolRoot $copy
    $ok = (Test-Verdict $result 2 '(?m)^VERSION: \.NET SDK 10\.0\.199 is required') -and (Get-TreeState $repo) -ceq $before
    [void](Add-Property 'unavailable SDK pin refused' $ok $result.Text)
}

# Runs apply in this process with $Interfere called just before each file's write, after every verdict is settled.
function Invoke-InterferedApply {
    param([string]$Repo, [string[]]$Path, [scriptblock]$Interfere)

    $manifest = Join-Path $script:FormatRoot 'FormatTools.psm1'
    $module = Import-Module $manifest -Force -PassThru
    & $module {
        param($Interfere)

        $script:QualifyInterfere = $Interfere
        $script:QualifyWrite = ${function:Write-FormatFile}
        function script:Write-FormatFile {
            param($Check)

            & $script:QualifyInterfere $Check
            & $script:QualifyWrite $Check
        }
    } $Interfere
    $result = & $module { param($Repo, $Path, $Tools) Invoke-FormatCommand -Path $Path -RepoRoot $Repo -ToolRoot $Tools -Apply } $Repo $Path $script:ToolRoot
    $script:Ledger.AddRange([string[]]@(& $module { $script:ProcessLedger }))
    [void](Import-Module $manifest -Force)

    return [pscustomobject]@{ ExitCode = $result.ExitCode; Text = $result.Lines -join "`n" }
}

# C-2/C-3: a parent swapped for a link, or a file deleted or unreadable after an earlier write, stays a
# per-file finding; completed writes keep their verdicts and nothing outside the repository changes.
function Test-WriteInterference {
    $dirty = $script:Cases.parsers[0].dirty
    $swaps = [ordered]@{
        'a parent folder' = @('src', 'SELECTION: src/Valid\.cs passes through a link')
        'the repository root' = @('', 'SELECTION: the repository root now resolves to a different folder')
    }

    foreach ($name in $swaps.Keys) {
        $relative = $swaps[$name][0]
        $outside = Join-Path $script:Scratch ('swap-' + $script:RepoCount)
        $decoy = Join-Path $outside 'src/Valid.cs'

        if ($relative) {
            $decoy = Join-Path $outside 'Valid.cs'
        }

        [void][IO.Directory]::CreateDirectory((Split-Path $decoy))
        [IO.File]::WriteAllBytes($decoy, (Get-Utf8Bytes $dirty))
        $repo = New-CaseRepo @{ 'src/Valid.cs' = $dirty }

        # Just before the write, the folder is moved aside and a link to an identical outside copy takes its place.
        $swap = {
            param($Check)

            $source = $Check.Root

            if ($relative) {
                $source = Join-Path $Check.Root $relative
            }

            [IO.Directory]::Move($source, "$source-moved")

            if ($IsWindows) {
                [void](New-Item -ItemType Junction -Path $source -Target $outside)
            } else {
                [void](New-Item -ItemType SymbolicLink -Path $source -Target $outside)
            }
        }.GetNewClosure()
        $result = Invoke-InterferedApply $repo @('src/Valid.cs') $swap
        $ok = (Test-Verdict $result 1 "(?m)^DRIFT\+WRITE src/Valid\.cs - $($swaps[$name][1])") -and
        (Test-SameBytes ([IO.File]::ReadAllBytes($decoy)) (Get-Utf8Bytes $dirty))
        [void](Add-Property "$name swapped for a link after selection: nothing written outside" $ok $result.Text)
    }

    # The root is reached through a link, and a folder above that link's target is swapped before the write.
    $container = Join-Path $script:Scratch ('container-' + $script:RepoCount)
    $outside = Join-Path $script:Scratch ('swap-' + $script:RepoCount + '-target')
    $decoy = Join-Path $outside 'repo/src/Valid.cs'
    [void][IO.Directory]::CreateDirectory((Split-Path $decoy))
    [IO.File]::WriteAllBytes($decoy, (Get-Utf8Bytes $dirty))
    $repo = New-CaseRepo @{ 'src/Valid.cs' = $dirty }
    [void][IO.Directory]::CreateDirectory($container)
    [IO.Directory]::Move($repo, (Join-Path $container 'repo'))
    $linkedRoot = Join-Path $script:Scratch ('linked-root-' + $script:RepoCount)

    if ($IsWindows) {
        [void](New-Item -ItemType Junction -Path $linkedRoot -Target (Join-Path $container 'repo'))
    } else {
        [void](New-Item -ItemType SymbolicLink -Path $linkedRoot -Target (Join-Path $container 'repo'))
    }

    $swap = {
        param($Check)

        [IO.Directory]::Move($container, "$container-moved")

        if ($IsWindows) {
            [void](New-Item -ItemType Junction -Path $container -Target $outside)
        } else {
            [void](New-Item -ItemType SymbolicLink -Path $container -Target $outside)
        }
    }.GetNewClosure()
    $result = Invoke-InterferedApply $linkedRoot @('src/Valid.cs') $swap
    $ok = (Test-Verdict $result 1 '(?m)^DRIFT\+WRITE src/Valid\.cs - SELECTION: the repository root now resolves to a different folder') -and
    (Test-SameBytes ([IO.File]::ReadAllBytes($decoy)) (Get-Utf8Bytes $dirty))
    [void](Add-Property "a folder above a linked root's target swapped after selection: nothing written outside" $ok $result.Text)

    # Windows: the root is a junction into a volume subfolder by GUID path, retargeted to an identical copy.
    if ($IsWindows) {
        $volume = (Get-Volume -FilePath $script:Scratch).Path
        $first = Join-Path $script:Scratch ('volume-first-' + $script:RepoCount)
        $second = Join-Path $script:Scratch ('volume-second-' + $script:RepoCount)
        $junction = Join-Path $script:Scratch ('volume-root-' + $script:RepoCount)
        $repo = New-CaseRepo @{ 'src/Valid.cs' = $dirty }
        [IO.Directory]::Move($repo, $first)
        $decoy = Join-Path $second 'src/Valid.cs'
        [void][IO.Directory]::CreateDirectory((Split-Path $decoy))
        [IO.File]::WriteAllBytes($decoy, (Get-Utf8Bytes $dirty))
        $firstByGuid = $volume + $first.Substring([IO.Path]::GetPathRoot($first).Length)
        $secondByGuid = $volume + $second.Substring([IO.Path]::GetPathRoot($second).Length)
        [void](& cmd /c mklink /J $junction $firstByGuid)
        $retarget = {
            param($Check)

            [IO.Directory]::Delete($junction)
            [void](& cmd /c mklink /J $junction $secondByGuid)
        }.GetNewClosure()
        $result = Invoke-InterferedApply $junction @('src/Valid.cs') $retarget
        $ok = (Test-Verdict $result 1 '(?m)^DRIFT\+WRITE src/Valid\.cs - SELECTION: the repository root now resolves to a different folder') -and
        (Test-SameBytes ([IO.File]::ReadAllBytes($decoy)) (Get-Utf8Bytes $dirty))
        [void](Add-Property 'a junction into a volume subfolder retargeted after selection: nothing written outside' $ok $result.Text)
    }

    $files = @('scripts/A.ps1', 'scripts/B.ps1')
    $holder = [pscustomobject]@{ Stream = $null; File = $null }
    $actions = [ordered]@{
        deleted = { param($Check) if ($Check.Path -eq 'scripts/B.ps1') { [IO.File]::Delete($Check.File) } }
        unreadable = {
            param($Check)

            if ($Check.Path -ne 'scripts/B.ps1') {
                return
            }

            $holder.File = $Check.File

            if ($IsWindows) {
                $holder.Stream = [IO.File]::Open($Check.File, 'Open', 'Read', 'None')
            } else {
                [IO.File]::SetUnixFileMode($Check.File, [IO.UnixFileMode]::None)
            }
        }.GetNewClosure()
    }

    foreach ($name in $actions.Keys) {
        $repo = New-CaseRepo @{ 'scripts/A.ps1' = "`$a=1`n"; 'scripts/B.ps1' = "`$b=2`n" }
        $result = Invoke-InterferedApply $repo $files $actions[$name]

        if ($holder.Stream) {
            $holder.Stream.Dispose()
            $holder.Stream = $null
        } elseif ($holder.File -and -not $IsWindows) {
            [IO.File]::SetUnixFileMode($holder.File, [IO.UnixFileMode]'UserRead, UserWrite')
        }

        $written = [IO.File]::ReadAllText((Join-Path $repo 'scripts/A.ps1')) -ceq "`$a = 1`n"
        $ok = (Test-Verdict $result 1 '(?m)^FORMATTED scripts/A\.ps1$') -and $result.Text -match '(?m)^DRIFT\+WRITE scripts/B\.ps1 - ' -and $written
        [void](Add-Property "file $name after an earlier write: verdicts kept, exit 1" $ok $result.Text)
    }
}
