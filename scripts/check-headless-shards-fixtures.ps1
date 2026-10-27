[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'test-headless-shards.ps1')
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "hp-shard-fixtures-$([Guid]::NewGuid().ToString('N'))"
$null = [IO.Directory]::CreateDirectory($fixtureRoot)
$manifestPath = Join-Path $fixtureRoot 'manifest.json'

function New-Case([string] $Class, [string] $Method, $Weight = $null) {
    $traits = if ($null -eq $Weight) { @{} } else { @{ ShardCost = @($Weight) } }

    return @{ Class = $Class; Method = $Method; DisplayName = "$Class.$Method"; Traits = $traits }
}

function Partition($Cases, [int] $Count = 4) {
    @{ schemaVersion = 1; project = 'HeadlessTests/HappyPhoton.Headless.Tests.csproj'; tests = @($Cases) } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath

    return @(Get-HeadlessTestShards $manifestPath $Count)
}

function Require([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Reject([scriptblock] $Action, [string] $Message) {
    $caught = $null

    try { & $Action | Out-Null }
    catch { $caught = $_.Exception.Message }

    Require ($null -ne $caught -and $caught.Contains($Message)) "Expected '$Message'; got '$caught'."
}

try {
    # Four heavy classes are legal and cost, rather than method count, puts each first.
    $cases = @(
        New-Case 'Heavy.A' 'One' '10'
        New-Case 'Heavy.B' 'One' '10'
        New-Case 'Heavy.C' 'One' '10'
        New-Case 'Heavy.D' 'One' '10'
        New-Case 'Light.A' 'One'
        New-Case 'Light.B' 'One'
        New-Case 'Light.C' 'One'
        New-Case 'Light.D' 'One'
    )
    $shards = @(Partition $cases)

    for ($index = 0; $index -lt 4; $index++) {
        $letter = [char](65 + $index)
        Require (($shards[$index].Classes -join ',') -ceq "Heavy.$letter,Light.$letter") 'Cost assignment or ordinal tie changed.'
        Require ($shards[$index].EstimatedCost -eq 11) 'Incorrect cost.'
        Require ($shards[$index].Filter.Contains("FullyQualifiedName~Heavy.$letter.")) 'Filter lost its class boundary.'
        Require ($shards[$index].Filter.StartsWith('(Category!=Quarantined)&(')) 'Quarantine filter was lost.'
    }

    $reversed = @($cases)
    [array]::Reverse($reversed)
    $again = @(Partition $reversed)
    Require (($shards.Filter -join ';') -ceq ($again.Filter -join ';')) 'Partition depends on discovery order.'

    $rows = @(
        New-Case 'Two.Rows' 'Theory(value: 1)' '3'
        New-Case 'Two.Rows' 'Theory(value: 2)' '3'
        New-Case 'One.Heavy' 'Fact' '5'
        New-Case 'One.Light' 'Fact'
    )
    $weighted = @(Partition $rows 2)
    Require ($weighted[0].Classes.Contains('Two.Rows')) 'Case count did not multiply the weight.'
    Require ($weighted[0].MethodCount -eq 2 -and $weighted[0].EstimatedCost -eq 6) 'Wrong case cost.'
    Require ($weighted[1].EstimatedCost -eq 6) 'Wrong least-loaded placement.'

    foreach ($value in @('0', '-1', 'NaN', 'Infinity', 'not-a-number', '1,5')) {
        Reject { Partition @((New-Case 'Invalid.Cost' 'Fact' $value)) 1 } 'Invalid ShardCost'
    }

    Reject { Partition @((New-Case 'Conflicting.Cost' 'One' '2'), (New-Case 'Conflicting.Cost' 'Two')) 1 } 'Inconsistent ShardCost'
    $multiple = New-Case 'Multiple.Cost' 'One'
    $multiple.Traits.ShardCost = @('2', '3')
    Reject { Partition @($multiple) 1 } 'Invalid ShardCost'
    Reject { Partition $cases 9 } 'Cannot partition'
    Reject { Partition $cases 0 } 'Cannot partition'

    $duplicate = @($shards) + @($shards[0])
    Reject { Assert-HeadlessTestShards $cases $duplicate } 'occurs 2 times'
    Reject { Assert-HeadlessTestShards $cases @($shards | Select-Object -Skip 1) } 'occurs 0 times'
    $unknown = [pscustomobject]@{ Classes = @('Unknown.Class') }
    Reject { Assert-HeadlessTestShards $cases (@($shards) + @($unknown)) } 'Unknown shard class'

    Write-Host 'Weighted headless shard fixtures passed (cost, count, ties, determinism, filters and rejected inputs/partitions).'
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)

    if (-not $resolved.StartsWith($temporary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Fixture cleanup escaped temporary root: $resolved"
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force
}
