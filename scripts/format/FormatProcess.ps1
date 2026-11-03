$script:AllowedPrograms = @('git', 'dotnet', 'node', 'npm')
$script:ProcessLedger = [Collections.Generic.List[string]]::new()

# Child processes never create user-profile state: no first-run sentinel, telemetry, PATH edit or package cache.
function Get-ChildEnvironment {
    param([string]$Scratch)

    return @{
        DOTNET_CLI_HOME = $Scratch
        DOTNET_NOLOGO = '1'
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
        DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
        NUGET_PACKAGES = Join-Path $Scratch 'nuget'
        MSBUILDDISABLENODEREUSE = '1'
        GIT_OPTIONAL_LOCKS = '0'
    }
}

function Read-BoundedOutput {
    param([Diagnostics.Process]$Process, [datetime]$Deadline, [int]$Limit)

    $readers = @($Process.StandardOutput, $Process.StandardError)
    $texts = @([Text.StringBuilder]::new(), [Text.StringBuilder]::new())
    $truncated = @($false, $false)
    $buffers = @([char[]]::new(8192), [char[]]::new(8192))
    $reads = @($readers[0].ReadAsync($buffers[0], 0, 8192), $readers[1].ReadAsync($buffers[1], 0, 8192))

    while ($null -ne $reads[0] -or $null -ne $reads[1]) {
        if ([datetime]::UtcNow -gt $Deadline) {
            return $null
        }

        $pending = [Threading.Tasks.Task[]]@($reads | Where-Object { $null -ne $_ })
        [void][Threading.Tasks.Task]::WaitAny($pending, 200)

        for ($i = 0; $i -lt 2; $i++) {
            if ($null -eq $reads[$i] -or -not $reads[$i].IsCompleted) {
                continue
            }

            $count = $reads[$i].Result

            if ($count -eq 0) {
                $reads[$i] = $null
                continue
            }

            # Keep the head of the output and drain the rest, so a noisy child can neither block nor exhaust memory.
            $room = $Limit - $texts[$i].Length

            if ($room -lt $count) {
                $truncated[$i] = $true
            }

            if ($room -gt 0) {
                [void]$texts[$i].Append($buffers[$i], 0, [math]::Min($count, $room))
            }

            $reads[$i] = $readers[$i].ReadAsync($buffers[$i], 0, 8192)
        }
    }

    return [pscustomobject]@{ Texts = $texts; Truncated = $truncated[0] -or $truncated[1] }
}

function Invoke-FormatProcess {
    param(
        [Parameter(Mandatory)][string]$Program,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [hashtable]$Environment = @{},
        [int]$TimeoutSeconds = 300,
        [int]$OutputLimit = 65536,
        [switch]$AllowFailure
    )

    if ($Program -cnotin $script:AllowedPrograms) {
        throw "PROCESS: executable not allowed: $Program"
    }

    $command = Get-Command $Program -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1

    if (-not $command) {
        throw "VERSION: $Program was not found on PATH"
    }

    $info = [Diagnostics.ProcessStartInfo]::new($command.Source)

    foreach ($argument in $Arguments) {
        $info.ArgumentList.Add($argument)
    }

    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $info.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)

    foreach ($key in $Environment.Keys) {
        $info.Environment[$key] = $Environment[$key]
    }

    $script:ProcessLedger.Add((@($Program) + $Arguments) -join ' ')
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $started = $false

    try {
        $started = $process.Start()
        $process.StandardInput.Close()
        $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
        $output = Read-BoundedOutput $process $deadline $OutputLimit
        $remaining = [math]::Max(0, ($deadline - [datetime]::UtcNow).TotalMilliseconds)

        if ($null -eq $output -or -not $process.WaitForExit([int]$remaining)) {
            throw "PROCESS: $Program timed out after $TimeoutSeconds s"
        }

        $process.WaitForExit()
        $result = [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $output.Texts[0].ToString()
            Error = $output.Texts[1].ToString()
            Truncated = $output.Truncated
        }

        if ($result.ExitCode -ne 0 -and -not $AllowFailure) {
            throw "PROCESS: $Program exited $($result.ExitCode): $($result.Error.Trim()) $($result.Output.Trim())"
        }

        return $result
    } finally {
        if ($started -and -not $process.HasExited) {
            $process.Kill($true)
            [void]$process.WaitForExit(10000)
        }

        $process.Dispose()
    }
}
