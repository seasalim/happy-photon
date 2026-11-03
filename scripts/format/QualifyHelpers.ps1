# Shared helpers for qualify-format.ps1; dot-sourced, never run alone.

function Add-Property {
    param([string]$Name, [bool]$Passed, [string]$Detail = '')

    $script:Properties.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })

    if (-not $Passed) {
        Write-Host "FAIL $Name - $Detail"
    }

    return $Passed
}

function Get-Sha256 {
    param([byte[]]$Bytes)

    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes))
}

# Cross-platform hashes compare LF text, which is what the repository stores.
function Get-LfHash {
    param([byte[]]$Bytes)

    $text = [Text.Encoding]::UTF8.GetString($Bytes).Replace("`r`n", "`n")

    return Get-Sha256 ([Text.Encoding]::UTF8.GetBytes($text))
}

function Get-Utf8Bytes {
    param([string]$Text)

    return [Text.UTF8Encoding]::new($false).GetBytes($Text)
}

function Test-SameBytes {
    param([byte[]]$Left, [byte[]]$Right)

    return [Convert]::ToBase64String($Left) -ceq [Convert]::ToBase64String($Right)
}

function Invoke-Git {
    param([string]$Repo, [string[]]$Arguments)

    $output = & git -C $Repo -c core.autocrlf=false -c core.safecrlf=false @Arguments 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed: $output"
    }
}

# Every case repository also holds a project file, so any restore or build would leave obj/ or bin/ behind.
function New-CaseRepo {
    param([hashtable]$Files)

    $repo = Join-Path $script:Scratch ('repo' + $script:RepoCount)
    $script:RepoCount++
    [void][IO.Directory]::CreateDirectory($repo)
    Invoke-Git $repo @('-c', 'init.defaultBranch=main', 'init', '-q')
    $all = @{ 'App.csproj' = '<Project Sdk="Microsoft.NET.Sdk" />' + "`n" }

    foreach ($key in $Files.Keys) {
        $all[$key] = $Files[$key]
    }

    foreach ($path in $all.Keys) {
        $file = Join-Path $repo $path
        [void][IO.Directory]::CreateDirectory((Split-Path $file))
        $content = $all[$path]

        if ($content -is [string]) {
            $content = Get-Utf8Bytes $content
        }

        [IO.File]::WriteAllBytes($file, [byte[]]$content)
    }

    Invoke-Git $repo @('add', '-A')

    return $repo
}

function Get-TreeState {
    param([string]$Root)

    $entries = foreach ($item in Get-ChildItem -LiteralPath $Root -Recurse -Force) {
        $relative = [IO.Path]::GetRelativePath($Root, $item.FullName).Replace('\', '/')

        if ($relative -eq '.git' -or $relative.StartsWith('.git/')) {
            continue
        }

        if ($item.PSIsContainer) {
            "dir $relative"
        } else {
            "file $relative $((Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash) $($item.LastWriteTimeUtc.Ticks)"
        }
    }

    return (@($entries) | Sort-Object) -join "`n"
}

# Places a dotnet, npm or PowerShell run could leave persistent state outside the repository.
function Get-HostState {
    $profileRoot = [Environment]::GetFolderPath('UserProfile')
    $roots = @('.dotnet', '.nuget', '.npm', '.templateengine', '.local/share/powershell', '.config/NuGet') |
        ForEach-Object { Join-Path $profileRoot $_ }
    $roots += Join-Path $profileRoot '.dotnet/corefx'

    if ($IsWindows) {
        $roots += Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PowerShell/Modules'
    }

    $entries = [Collections.Generic.List[string]]::new()

    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) {
            $entries.Add("absent $root")
            continue
        }

        $recurse = $root.EndsWith('corefx')

        foreach ($item in Get-ChildItem -LiteralPath $root -Force -Recurse:$recurse) {
            $entries.Add("$($item.FullName) $($item.LastWriteTimeUtc.Ticks)")
        }
    }

    $leftovers = @(Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Filter 'hp-format-*' -Force | ForEach-Object Name)
    $entries.Add('temp ' + ($leftovers -join ','))

    if ($IsWindows) {
        $entries.Add('user-path ' + [Environment]::GetEnvironmentVariable('Path', 'User'))

        foreach ($store in @('Cert:\CurrentUser\My', 'Cert:\CurrentUser\Root')) {
            $entries.Add("$store " + (@(Get-ChildItem -LiteralPath $store | ForEach-Object Thumbprint) -join ','))
        }
    }

    return $entries -join "`n"
}

function Invoke-Format {
    param([string]$Command, [string]$Repo, [string[]]$Path, [string]$Tools = $script:ToolRoot)

    $entry = Join-Path $script:FormatRoot "$Command-format.ps1"
    $lines = @(& $entry -Path $Path -RepoRoot $Repo -ToolRoot $Tools)
    $exitCode = $LASTEXITCODE
    $module = Get-Module FormatTools

    if ($module) {
        $script:Ledger.AddRange([string[]]@(& $module { $script:ProcessLedger }))
    }

    return [pscustomobject]@{ ExitCode = $exitCode; Text = $lines -join "`n" }
}

# A fresh pwsh per run, as a contributor would use it; needed where a tool root other than the real one is loaded.
function Invoke-FormatChild {
    param([string]$Command, [string]$Repo, [string[]]$Path, [string]$Tools, [string]$Root = $script:FormatRoot)

    $entry = Join-Path $Root "$Command-format.ps1"
    $lines = @(& $script:Pwsh -NoProfile -NonInteractive -File $entry -RepoRoot $Repo -ToolRoot $Tools @Path 2>&1 |
            ForEach-Object { "$_" })

    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = $lines -join "`n" }
}

function Test-Verdict {
    param($Result, [int]$ExitCode, [string]$Pattern)

    return $Result.ExitCode -eq $ExitCode -and $Result.Text -match $Pattern
}

function Start-ProxySink {
    # Any HTTP(S) attempt by a child process lands here and is counted; nothing is ever answered.
    $script:Sink = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $script:Sink.Start()
    $proxy = "http://127.0.0.1:$($script:Sink.LocalEndpoint.Port)"
    $script:SavedEnvironment = @{}
    $names = @('HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY', 'NO_PROXY', 'http_proxy', 'https_proxy', 'all_proxy', 'no_proxy',
        'npm_config_offline', 'POWERSHELL_TELEMETRY_OPTOUT', 'POWERSHELL_UPDATECHECK')

    foreach ($name in $names) {
        $script:SavedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $null)
    }

    foreach ($name in @('HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY', 'http_proxy', 'https_proxy', 'all_proxy')) {
        [Environment]::SetEnvironmentVariable($name, $proxy)
    }

    # The child shells' own startup telemetry and update check are pwsh behaviour, not the formatter's.
    [Environment]::SetEnvironmentVariable('npm_config_offline', 'true')
    [Environment]::SetEnvironmentVariable('POWERSHELL_TELEMETRY_OPTOUT', '1')
    [Environment]::SetEnvironmentVariable('POWERSHELL_UPDATECHECK', 'Off')
}

# Returns what each connection asked for (its first request line), so any attempt is attributable.
function Stop-ProxySink {
    $requests = [Collections.Generic.List[string]]::new()

    while ($script:Sink.Pending()) {
        $client = $script:Sink.AcceptTcpClient()

        try {
            $client.ReceiveTimeout = 2000
            $buffer = [byte[]]::new(512)
            $read = $client.GetStream().Read($buffer, 0, $buffer.Length)
            $requests.Add(([Text.Encoding]::ASCII.GetString($buffer, 0, $read) -split "`r?`n")[0])
        } catch {
            $requests.Add('(no request line)')
        } finally {
            $client.Dispose()
        }
    }

    $script:Sink.Stop()

    foreach ($name in $script:SavedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $script:SavedEnvironment[$name])
    }

    return $requests.ToArray()
}
