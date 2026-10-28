[CmdletBinding()]
param(
    [Parameter(Mandatory)] [object[]] $Shards,
    [Parameter(Mandatory)] [string[]] $TestArguments,
    [Parameter(Mandatory)] [string] $RunDirectory,
    [Parameter(Mandatory)] [string] $LogFilePrefix,
    [int] $ProcessorCount
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'test-processor-budget.ps1')
$cap = if ($ProcessorCount) { $ProcessorCount } else { 8 }
Assert-TestProcessorBudget (@($cap, $cap) + @(2) * $Shards.Count)
$hosts = [Collections.Generic.List[object]]::new()
$records = [Collections.Generic.List[object]]::new()
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
    [IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$shardRoot = Join-Path $tempRoot "hps-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"

function Start-TestHost([string] $Name, [string] $Project, [int] $Cap, $Shard = $null) {
    $arguments = @("test", (Join-Path $repoRoot $Project)) + $TestArguments +
        @("--logger", "trx;LogFilePrefix=$LogFilePrefix-$Name")
    $start = [Diagnostics.ProcessStartInfo]::new("dotnet")
    $start.WorkingDirectory = $repoRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true

    $temporary = $null

    if ($null -ne $Shard) {
        $arguments += @("--filter", $Shard.Filter)
        $temporary = Join-Path $shardRoot "$($Shard.Index)"
        $null = New-Item -ItemType Directory -Path $temporary -Force

        foreach ($variable in @("TEMP", "TMP", "TMPDIR")) {
            $start.Environment[$variable] = $temporary
        }
    }

    if ($Cap -gt 0) {
        $arguments += @("--", "RunConfiguration.EnvironmentVariables.DOTNET_PROCESSOR_COUNT=$Cap")
    }

    foreach ($argument in $arguments) {
        $start.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $hostRun = [pscustomobject]@{
        Name = $Name
        Project = $Project
        Cap = if ($Cap -gt 0) { $Cap } elseif ($env:HAPPY_PHOTON_FULL_CPU -eq '1') { $null } else { 8 }
        TempRoot = $temporary
        Process = $process
        Started = $false
        Output = $null
        Error = $null
        Log = Join-Path $RunDirectory "$Name.log"
    }
    $hosts.Add($hostRun)
    Write-Host "Running tests: $Project ($Name); log: $($hostRun.Log)"
    $hostRun.Started = $process.Start()
    $hostRun.Output = $process.StandardOutput.ReadToEndAsync()
    $hostRun.Error = $process.StandardError.ReadToEndAsync()
}

$exitCode = 0

try {
    Start-TestHost "unit" "Tests/HappyPhoton.Tests.csproj" $ProcessorCount
    Start-TestHost "interop" "Interop/HappyPhoton.LibRaw.Interop.Tests/HappyPhoton.LibRaw.Interop.Tests.csproj" $ProcessorCount

    foreach ($shard in $Shards) {
        Start-TestHost "headless-$($shard.Index)" "HeadlessTests/HappyPhoton.Headless.Tests.csproj" 2 $shard
    }

    foreach ($hostRun in $hosts) {
        $hostRun.Process.WaitForExit()
        $output = $hostRun.Output.GetAwaiter().GetResult()
        $errors = $hostRun.Error.GetAwaiter().GetResult()
        [IO.File]::WriteAllText($hostRun.Log, $output + $errors)
        Write-Host "[$($hostRun.Name)] $output$errors"

        if ($hostRun.Process.ExitCode -ne 0) { $exitCode = $hostRun.Process.ExitCode }
    }
} finally {
    foreach ($hostRun in $hosts) {
        try {
            if ($hostRun.Started) {
                if (-not $hostRun.Process.HasExited) { $hostRun.Process.Kill($true) }

                $hostRun.Process.WaitForExit()
                $records.Add([pscustomobject]@{
                    role = $hostRun.Name
                    project = $hostRun.Project
                    cap = $hostRun.Cap
                    pid = $hostRun.Process.Id
                    tempRoot = $hostRun.TempRoot
                    startUtc = $hostRun.Process.StartTime.ToUniversalTime().ToString('o')
                    exitUtc = $hostRun.Process.ExitTime.ToUniversalTime().ToString('o')
                    wallSeconds = ($hostRun.Process.ExitTime - $hostRun.Process.StartTime).TotalSeconds
                    exitCode = $hostRun.Process.ExitCode
                })
                ConvertTo-Json -InputObject @($records.ToArray()) -Depth 5 |
                    Set-Content -LiteralPath (Join-Path $RunDirectory 'hosts.json')

                if ($null -ne $hostRun.Output -and $null -ne $hostRun.Error) {
                    [IO.File]::WriteAllText($hostRun.Log,
                        $hostRun.Output.GetAwaiter().GetResult() + $hostRun.Error.GetAwaiter().GetResult())
                    & (Join-Path $PSScriptRoot "report-test-abort.ps1") -OutputPath $hostRun.Log -ResultsDirectory $RunDirectory
                }
            }
        } finally {
            $hostRun.Process.Dispose()
        }
    }

    $resolved = [IO.Path]::GetFullPath($shardRoot)

    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Shard cleanup path is outside the temporary directory: $resolved"
    }

    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

return $exitCode
