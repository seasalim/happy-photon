# Loaded by the sampler fixtures. A synthetic in-job discovery host survives its command.
& {
    $hostDirectory = Join-Path $fixtureOutput 'host'
    $null = New-Item -ItemType Directory -Path $hostDirectory -Force
    $buildDirectory = Join-Path ([IO.Path]::GetTempPath()) "hp-sampler-host-$([Guid]::NewGuid().ToString('N'))"
    $null = New-Item -ItemType Directory -Path $buildDirectory
    Copy-Item (Join-Path $fixtures 'sampler-host.cs.txt') (Join-Path $buildDirectory 'Program.cs')
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <AssemblyName>HappyPhoton.Tests</AssemblyName>
    <EnableNETAnalyzers>false</EnableNETAnalyzers>
  </PropertyGroup>
</Project>
'@ | Set-Content (Join-Path $buildDirectory 'Fixture.csproj')
    try {
        $build = & dotnet build (Join-Path $buildDirectory 'Fixture.csproj') --configuration Release --output $hostDirectory 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Sampler fixture build failed: $build" }
    } finally {
        $resolved = [IO.Path]::GetFullPath($buildDirectory)
        $temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)

        if (-not $resolved.StartsWith($temporary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Fixture build cleanup escaped temporary root: $resolved"
        }

        Remove-Item -LiteralPath $resolved -Recurse -Force
    }

    $hostPath = Join-Path $hostDirectory 'HappyPhoton.Tests.exe'
    $responsePath = Join-Path $hostDirectory 'discovery.rsp'
    $releasedPath = Join-Path $hostDirectory 'released.txt'
    $token = "Local\SamplerSurvivor-$([Guid]::NewGuid().ToString('N'))"
    $ready = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::ManualReset, "$token-ready")
    $observed = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::ManualReset, "$token-observed")
    $release = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::ManualReset, "$token-release")
    @('-automated', '-list', 'Discovery', "$token-ready", "$token-release", $releasedPath) | Set-Content $responsePath
    $fixtureState = @{ process = $null }
    $command = @'
$settings = Get-Content -Raw -LiteralPath $env:HP_SAMPLER_FIXTURE | ConvertFrom-Json
$start = [Diagnostics.ProcessStartInfo]::new($settings.hostPath)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.ArgumentList.Add('@@')
$start.ArgumentList.Add($settings.responsePath)
$process = [Diagnostics.Process]::Start($start)
$ready = [Threading.EventWaitHandle]::OpenExisting($settings.ready)
$observed = [Threading.EventWaitHandle]::OpenExisting($settings.observed)

try {
    if (-not $ready.WaitOne(30000)) { throw 'Fixture host never became ready.' }
    if (-not $observed.WaitOne(30000)) { throw 'Sampler never observed the fixture host.' }
} finally {
    $ready.Dispose()
    $observed.Dispose()
    $process.Dispose()
}
'@
    $settingsPath = Join-Path $hostDirectory 'settings.json'
    @{ hostPath = $hostPath; responsePath = $responsePath; ready = "$token-ready"; observed = "$token-observed" } |
        ConvertTo-Json | Set-Content $settingsPath
    $savedFixture = $env:HP_SAMPLER_FIXTURE
    $env:HP_SAMPLER_FIXTURE = $settingsPath

    function Get-Process {
        param($Name, $ErrorAction)

        foreach ($process in Microsoft.PowerShell.Management\Get-Process -Name $Name -ErrorAction $ErrorAction) {
            if ($process.Path -eq $hostPath) {
                if ($null -eq $fixtureState.process) {
                    $fixtureState.process = [Diagnostics.Process]::GetProcessById($process.Id)
                    $null = $fixtureState.process.Handle
                }

                $null = $observed.Set()
            }

            $process
        }
    }

    try {
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        $destination = Join-Path $fixtureOutput 'surviving-discovery-host'
        & $sampler -Command pwsh -CommandArguments @('-NoProfile', '-EncodedCommand', $encoded) -OutputDirectory $destination
        Assert-Equal $LASTEXITCODE 0 'Survivor fixture command completed'
        $summary = Get-Content -Raw "$destination/summary.json" | ConvertFrom-Json
        Assert-Equal $summary.measurementValid $true 'The surviving host is contained in the job'
        $hostRow = @($summary.hosts | Where-Object path -EQ $hostPath)
        Assert-Equal $hostRow.Count 1 'The real surviving host is reported once'
        Assert-Equal $hostRow[0].aliveAfterCommand $true 'The host outlives the wrapped command'
        Assert-Equal $fixtureState.process.HasExited $false 'The sampler did not terminate the survivor'
        $cpu = Get-Content -Raw "$destination/cpu-samples.json" | ConvertFrom-Json
        $track = @($cpu.processes | Where-Object pid -EQ $fixtureState.process.Id)
        Assert-Equal $track.Count 1 'Discovery helper has retained CPU evidence'
        Assert-Equal $track[0].kind 'unknown' 'Discovery helper is excluded from proven test CPU and driver subtraction'
        Assert-Equal $track[0].executionEvidence '' 'Discovery is not execution evidence'
    } finally {
        $env:HP_SAMPLER_FIXTURE = $savedFixture
        $null = $release.Set()

        if ($null -ne $fixtureState.process) {
            try {
                if (-not $fixtureState.process.WaitForExit(30000)) { throw 'Fixture survivor did not accept cleanup signal.' }

                Assert-Equal $fixtureState.process.ExitCode 0 'The survivor exits gracefully after fixture release'
                Assert-Equal (Test-Path $releasedPath) $true 'The survivor completed its own cleanup'
            } finally {
                $fixtureState.process.Dispose()
            }
        }

        $ready.Dispose()
        $observed.Dispose()
        $release.Dispose()
    }

    . (Join-Path $PSScriptRoot 'check-test-profile-exit-fixtures.ps1')
}
