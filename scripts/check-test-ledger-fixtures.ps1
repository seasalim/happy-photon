[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$checker = Join-Path $PSScriptRoot 'check-test-ledger.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "happy-photon-ledger-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $fixtureRoot
$unit = 'Tests/HappyPhoton.Tests.csproj'
$headless = 'HeadlessTests/HappyPhoton.Headless.Tests.csproj'
$interop = 'Interop/HappyPhoton.LibRaw.Interop.Tests/HappyPhoton.LibRaw.Interop.Tests.csproj'
$fixtureCount = 0
$sourceCache = @{ projects = @{}; files = @{} }

function New-LedgerFixture {
    $entries = @(
        @{ kind = 'duplicate'; removed = 'Fixture.Tests.Gone'; survivor = @(
            @{ project = $headless; test = 'Fixture.Tests.Keep' },
            @{ project = $unit; test = 'Fixture.Tests.Keep' },
            @{ project = $interop; test = 'Fixture.Tests.Keep' }) },
        @{ kind = 'covered-cheaper'; removed = 'Fixture.Tests.Cheap'; survivor = @(
            @{ project = $unit; test = 'Fixture.Tests.Keep' });
            wiring = @{ project = $headless; test = 'Fixture.Tests.Render' } },
        @{ kind = 'removed-behaviour'; removed = 'Fixture.Retired'; behaviour = 'Retired workflow' },
        @{ kind = 'low-value'; removed = 'Fixture.Tests.Stores'; reason = 'stores-input' },
        @{ kind = 'trimmed-cases'; removed = 'Fixture.Tests.Theory'; method = 'Fixture.Tests.Theory';
            removedCases = @('Fixture.Tests.Theory(value: 1)');
            keptCases = @('Fixture.Tests.Theory(value: 0)', 'Fixture.Tests.Theory(value: 10)') },
        @{ kind = 'folded'; removed = @('Fixture.Tests.FoldOne', 'Fixture.Tests.FoldTwo');
            survivor = @{ project = $headless; test = 'Fixture.Tests.FoldedNew' }; result = 'new' },
        @{ kind = 'folded'; removed = 'Fixture.Tests.FoldThree';
            survivor = @{ project = $headless; test = 'Fixture.Tests.FoldedExisting' }; result = 'existing' },
        @{ kind = 'duplicate'; removed = 'Fixture.Shared.Identical';
            survivor = @(@{ project = $unit; test = 'Fixture.Shared.Identical' }) }
    )

    foreach ($entry in $entries) {
        $entry.project = $headless
        $entry.reference = 'Fixture assertion or equivalence class; TESTSUITE-WP3'
        $entry.wp = 'TESTSUITE-WP3'
        $entry.date = '2026-10-26'
    }

    $manifests = @{}
    $rows = @{}

    foreach ($project in @($unit, $headless, $interop)) {
        $names = if ($project -eq $headless) { @('Keep', 'Render', 'Theory', 'FoldedNew', 'FoldedExisting') } else { @('Keep') }
        $tests = @(foreach ($name in $names) {
            @{ Class = 'Fixture.Tests'; Method = $name; DisplayName = "Fixture.Tests.$name"; Traits = @{} }
        })

        if ($project -eq $unit) {
            $tests += @{ Class = 'Fixture.Shared'; Method = 'Identical'; DisplayName = 'Fixture.Shared.Identical'; Traits = @{} }
        }

        $manifests[$project] = @{ schemaVersion = 1; project = $project; tests = $tests }
        $rows[$project] = @(foreach ($test in $tests) {
            $names = if ($test.Method -eq 'Theory') { @('Fixture.Tests.Theory(value: 0)', 'Fixture.Tests.Theory(value: 10)') } else { @($test.DisplayName) }

            foreach ($name in $names) {
                @{ method = $test.DisplayName; name = $name; outcome = 'Passed' }
            }
        })
    }

    return @{
        ledger = @{ schemaVersion = 1; entries = $entries }
        manifests = $manifests
        rows = $rows
        sharded = $false
        sources = @{
            'HeadlessTests/Cases.cs' = 'namespace Fixture;
public partial class Tests
{
    // public void Gone() {} is a comment, not a declaration.
    public void Keep() { Gone(); }
    public void Render() {}
    public void Theory(int value) {}
    public void FoldedNew() {}
    public void FoldedExisting() {}
}'
            'HeadlessTests/Extra.cs' = ''
            'Tests/Cases.cs' = 'namespace Fixture { public class Tests { public void Keep() {} } public class Shared { public void Identical() {} } }'
            'Tests/Excluded.cs' = 'namespace Fixture; public class Excluded { public void Gone() {} }'
            'Shared/Linked.cs' = 'namespace Fixture; public class Linked { public void Existing() {} }'
            'Interop/HappyPhoton.LibRaw.Interop.Tests/Cases.cs' = 'namespace Fixture; public class Tests { public void Keep() {} }'
        }
    }
}

function Write-LedgerFixtureText([string] $Path, [string] $Text) {
    if ([IO.File]::Exists($Path) -and [IO.File]::ReadAllText($Path) -ceq $Text) { return }

    [IO.File]::WriteAllText($Path, $Text)
}

function Write-LedgerFixture($Model, [string] $Directory) {
    $null = [IO.Directory]::CreateDirectory((Join-Path $Directory 'manifests'))

    foreach ($source in $Model.sources.Keys) {
        $path = Join-Path $Directory $source
        $null = [IO.Directory]::CreateDirectory((Split-Path $path))
        Write-LedgerFixtureText $path $Model.sources[$source]
    }

    foreach ($project in @($unit, $headless, $interop)) {
        $assembly = [IO.Path]::GetFileNameWithoutExtension($project)
        $links = if ($project -eq $unit) { '<Compile Include="../Shared/Linked.cs" /><Compile Remove="Excluded.cs" />' } else { '' }
        # Evaluation only: no SDK, restore or build is needed for these Compile items.
        $xml = "<Project><PropertyGroup><AssemblyName>$assembly</AssemblyName><DefineConstants>FIXTURE</DefineConstants></PropertyGroup><ItemGroup><Compile Include='*.cs' />$links</ItemGroup></Project>"
        Write-LedgerFixtureText (Join-Path $Directory $project) $xml
        Write-LedgerFixtureText (Join-Path $Directory "manifests/$assembly.manifest.json") `
            ($Model.manifests[$project] | ConvertTo-Json -Depth 10)
        $previousShard = Join-Path $Directory "$assembly-1.trx"

        if (Test-Path -LiteralPath $previousShard) { Remove-Item -LiteralPath $previousShard }

        $shards = if ($Model.sharded -and $project -eq $headless) { 2 } else { 1 }

        for ($shard = 0; $shard -lt $shards; $shard++) {
            $definitions = [Collections.Generic.List[string]]::new()
            $results = [Collections.Generic.List[string]]::new()
            $index = 0

            foreach ($row in $Model.rows[$project]) {
                $index++
                if (($index % $shards) -ne $shard) { continue }

                $id = "test-$index"
                $separator = $row.method.LastIndexOf('.')
                $class = $row.method.Substring(0, $separator)
                $method = $row.method.Substring($separator + 1)
                $name = [Security.SecurityElement]::Escape($row.name)
                $definitions.Add("<UnitTest id='$id'><TestMethod codeBase='C:\tests\$assembly.dll' className='$class' name='$method' /></UnitTest>")
                $results.Add("<UnitTestResult testId='$id' testName='$name' outcome='$($row.outcome)' />")
            }

            $trx = "<TestRun xmlns='http://microsoft.com/schemas/VisualStudio/TeamTest/2010'><TestDefinitions>$($definitions -join '')</TestDefinitions><Results>$($results -join '')</Results><ResultSummary><Counters error='0' timeout='0' aborted='0' disconnected='0' notRunnable='0' /></ResultSummary></TestRun>"
            Write-LedgerFixtureText (Join-Path $Directory "$assembly-$shard.trx") $trx
        }
    }

    Write-LedgerFixtureText (Join-Path $Directory 'ledger.json') ($Model.ledger | ConvertTo-Json -Depth 12)
}

function Assert-LedgerFixture([string] $Name, [string] $Mode, [scriptblock] $Change = {}, [string[]] $Expected = @()) {
    $model = New-LedgerFixture
    & $Change $model
    # Every scenario has the same projects, symbols and Compile items. Reset the
    # source contents, manifests and TRX while reusing evaluations and unchanged declaration indexes.
    $directory = $fixtureRoot
    Write-LedgerFixture $model $directory
    $message = ''

    try {
        & $checker -Mode $Mode -RepoRoot $directory -LedgerPath 'ledger.json' -ResultsDirectory $directory `
            -SourceCache $sourceCache 6>$null
    } catch {
        $message = $_.Exception.Message
    }

    if ($Expected.Count -eq 0 -and $message) { throw "Fixture $Name unexpectedly failed: $message" }

    foreach ($text in $Expected) {
        if (-not $message.Contains($text, [StringComparison]::Ordinal)) {
            throw "Fixture $Name expected '$text', got '$message'."
        }
    }

    $script:fixtureCount++
}

try {
    Assert-LedgerFixture 'valid-policy' Policy
    Assert-LedgerFixture 'valid-results' Results
    Assert-LedgerFixture 'shards-pass' Results { param($m) $m.sharded = $true }
    Assert-LedgerFixture 'empty-ledger' Policy { param($m) $m.ledger.entries = @() }
    Assert-LedgerFixture 'invalid-schema' Policy { param($m) $m.ledger.schemaVersion = 2 } @('schemaVersion')
    Assert-LedgerFixture 'invalid-entries' Policy { param($m) $m.ledger.entries = @{} } @('entries array')

    foreach ($field in @('kind', 'project', 'removed', 'reference', 'wp', 'date')) {
        Assert-LedgerFixture "missing-$field" Policy {
            param($m)
            $m.ledger.entries[0].Remove($field)
        } @("$field", 'Entry 1')
    }

    foreach ($row in @(
            @{ index = 0; field = 'survivor'; name = 'Fixture.Tests.Gone' },
            @{ index = 1; field = 'survivor'; name = 'Fixture.Tests.Cheap' },
            @{ index = 1; field = 'wiring'; name = 'Fixture.Tests.Cheap' },
            @{ index = 2; field = 'behaviour'; name = 'Fixture.Retired' },
            @{ index = 3; field = 'reason'; name = 'Fixture.Tests.Stores' },
            @{ index = 4; field = 'method'; name = 'Fixture.Tests.Theory' },
            @{ index = 4; field = 'removedCases'; name = 'Fixture.Tests.Theory' },
            @{ index = 4; field = 'keptCases'; name = 'Fixture.Tests.Theory' },
            @{ index = 5; field = 'survivor'; name = 'Fixture.Tests.FoldOne' },
            @{ index = 5; field = 'result'; name = 'Fixture.Tests.FoldOne' })) {
        Assert-LedgerFixture "kind-$($row.index)-$($row.field)" Policy {
            param($m)
            $m.ledger.entries[$row.index].Remove($row.field)
        } @($row.field, $row.name)
    }

    Assert-LedgerFixture 'invalid-reference' Policy { param($m) $m.ledger.entries[0].survivor[0].Remove('test') } @('Fixture.Tests.Gone', 'fully qualified test')
    Assert-LedgerFixture 'invalid-reference-project' Policy { param($m) $m.ledger.entries[0].survivor[0].Remove('project') } @('Fixture.Tests.Gone', 'project')
    Assert-LedgerFixture 'unknown-kind' Policy { param($m) $m.ledger.entries[0].kind = 'unknown' } @('Fixture.Tests.Gone', 'unsupported kind')
    Assert-LedgerFixture 'invalid-date' Policy { param($m) $m.ledger.entries[0].date = '2026-02-30' } @('Fixture.Tests.Gone', 'invalid date')
    Assert-LedgerFixture 'invalid-reason' Policy { param($m) $m.ledger.entries[3].reason = 'uninteresting' } @('Fixture.Tests.Stores', 'low-value reason')
    Assert-LedgerFixture 'invalid-result' Policy { param($m) $m.ledger.entries[5].result = 'other' } @('Fixture.Tests.FoldOne', 'result')
    Assert-LedgerFixture 'invalid-survivor-array' Policy { param($m) $m.ledger.entries[0].survivor = @() } @('Fixture.Tests.Gone', 'nonempty array')
    Assert-LedgerFixture 'invalid-cheaper-project' Policy { param($m) $m.ledger.entries[1].survivor[0].project = $headless } @('Fixture.Tests.Cheap', 'unit test')
    Assert-LedgerFixture 'invalid-wiring-project' Policy { param($m) $m.ledger.entries[1].wiring.project = $unit } @('Fixture.Tests.Cheap', 'headless tests')
    Assert-LedgerFixture 'method-mismatch' Policy { param($m) $m.ledger.entries[4].method = 'Fixture.Tests.Other' } @('Fixture.Tests.Theory', 'method must equal')
    Assert-LedgerFixture 'case-mismatch' Policy { param($m) $m.ledger.entries[4].keptCases = @('Fixture.Tests.Other(value: 0)') } @('Fixture.Tests.Other(value: 0)', 'invalid keptCases')

    foreach ($reason in @('stores-input', 'interaction-only', 'log-text', 'constant')) {
        Assert-LedgerFixture "reason-$reason" Policy { param($m) $m.ledger.entries[3].reason = $reason }
    }

    Assert-LedgerFixture 'removed-method-declared' Policy {
        param($m)
        $m.sources['HeadlessTests/Extra.cs'] = 'namespace Fixture; public partial class Tests { public void Gone() {} }'
    } @('Fixture.Tests.Gone', 'still declared')
    Assert-LedgerFixture 'case-distinct-removed-method-declared' Policy {
        param($m)
        $m.ledger.entries[0].removed = 'Fixture.X.gone'
        $m.ledger.entries[3].removed = 'Fixture.Y.Gone'
        $m.sources['HeadlessTests/Extra.cs'] = 'namespace Fixture; public class Y { public void Gone() {} }'
    } @('Removed name still declared in HeadlessTests/HappyPhoton.Headless.Tests.csproj: Fixture.Y.Gone.')
    Assert-LedgerFixture 'escaped-method-declared' Policy {
        param($m)
        $m.sources['HeadlessTests/Extra.cs'] = 'namespace Fixture; public partial class Tests { public void \u0047one() {} }'
    } @('Fixture.Tests.Gone', 'still declared')
    Assert-LedgerFixture 'active-symbol-method-declared' Policy {
        param($m)
        $m.sources['HeadlessTests/Extra.cs'] = "#if FIXTURE`nnamespace Fixture; public partial class Tests { public void Gone() {} }`n#endif"
    } @('Fixture.Tests.Gone', 'still declared')
    Assert-LedgerFixture 'inactive-method-accepted' Policy {
        param($m)
        $m.sources['HeadlessTests/Extra.cs'] = "#if UNDEFINED`nnamespace Fixture; public partial class Tests { public void Gone() {} }`n#endif"
    }
    Assert-LedgerFixture 'nested-method-declared' Policy {
        param($m)
        $m.ledger.entries[3].removed = 'Fixture.Outer+Inner.Gone'
        $m.sources['HeadlessTests/Extra.cs'] = 'namespace Fixture { public class Outer { public class Inner { public void Gone() {} } } }'
    } @('Fixture.Outer+Inner.Gone', 'still declared')
    Assert-LedgerFixture 'removed-class-declared' Policy {
        param($m)
        $m.sources['HeadlessTests/Extra.cs'] = 'namespace Fixture { public class Retired {} }'
    } @('Fixture.Retired', 'still declared')
    Assert-LedgerFixture 'linked-method-declared' Policy {
        param($m)
        $m.ledger.entries[3].project = $unit
        $m.ledger.entries[3].removed = 'Fixture.Linked.Existing'
    } @('Fixture.Linked.Existing', 'still declared')
    Assert-LedgerFixture 'excluded-source-accepted' Policy {
        param($m)
        $m.ledger.entries[3].project = $unit
        $m.ledger.entries[3].removed = 'Fixture.Excluded.Gone'
    }
    Assert-LedgerFixture 'trimmed-method-absent' Policy {
        param($m)
        $m.sources['HeadlessTests/Cases.cs'] = ''
    } @('Fixture.Tests.Theory', 'not declared')
    Assert-LedgerFixture 'removed-and-referenced' Policy {
        param($m)
        $m.ledger.entries[0].survivor[0].test = 'Fixture.Tests.Gone'
    } @('Fixture.Tests.Gone', 'Removed and referenced')
    Assert-LedgerFixture 'removed-class-overlaps-survivor' Policy {
        param($m)
        $m.ledger.entries[2].removed = 'Fixture.Tests'
    } @('Fixture.Tests.Render', 'Removed and referenced')
    Assert-LedgerFixture 'removed-and-kept-case' Policy {
        param($m)
        $m.ledger.entries[4].keptCases += 'Fixture.Tests.Theory(value: 1)'
    } @('Fixture.Tests.Theory(value: 1)', 'Removed and referenced')
    Assert-LedgerFixture 'duplicate-removal' Policy {
        param($m)
        $m.ledger.entries[3].removed = 'Fixture.Tests.Gone'
    } @('Fixture.Tests.Gone', 'covered more than once')
    Assert-LedgerFixture 'class-method-overlap' Policy {
        param($m)
        $m.ledger.entries[3].removed = 'Fixture.Retired.Old'
    } @('Fixture.Retired.Old', 'covered more than once')
    Assert-LedgerFixture 'repeated-case' Policy {
        param($m)
        $m.ledger.entries[4].removedCases += 'Fixture.Tests.Theory(value: 1)'
    } @('Fixture.Tests.Theory(value: 1)', 'repeated removedCases')
    Assert-LedgerFixture 'all-violations-reported' Policy {
        param($m)
        $m.ledger.entries[2].Remove('behaviour')
        $m.ledger.entries[3].Remove('reason')
        $m.ledger.entries[5].Remove('result')
    } @('Fixture.Retired', 'missing behaviour', 'Fixture.Tests.Stores', 'low-value reason', 'Fixture.Tests.FoldOne', 'result')

    Assert-LedgerFixture 'survivor-not-discovered' Results {
        param($m)
        $m.manifests[$headless].tests = @($m.manifests[$headless].tests | Where-Object Method -NE 'Keep')
    } @('Fixture.Tests.Keep', 'not discovered')
    Assert-LedgerFixture 'wiring-not-discovered' Results {
        param($m)
        $m.manifests[$headless].tests = @($m.manifests[$headless].tests | Where-Object Method -NE 'Render')
    } @('Fixture.Tests.Render', 'not discovered')
    Assert-LedgerFixture 'folded-not-discovered' Results {
        param($m)
        $m.manifests[$headless].tests = @($m.manifests[$headless].tests | Where-Object Method -NE 'FoldedNew')
    } @('Fixture.Tests.FoldedNew', 'not discovered')
    Assert-LedgerFixture 'surviving-theory-failed-row' Results {
        param($m)
        $m.ledger.entries[0].survivor[0].test = 'Fixture.Tests.Theory'
        $m.ledger.entries = @($m.ledger.entries | Where-Object kind -NE 'trimmed-cases')
        ($m.rows[$headless] | Where-Object name -EQ 'Fixture.Tests.Theory(value: 10)').outcome = 'Failed'
    } @('Fixture.Tests.Theory(value: 10)', 'not Passed')
    Assert-LedgerFixture 'survivor-missing-trx' Results {
        param($m)
        $m.rows[$unit] = @($m.rows[$unit] | Where-Object method -NE 'Fixture.Tests.Keep')
    } @('Fixture.Tests.Keep', $unit, 'missing from TRX')

    foreach ($outcome in @('Failed', 'NotExecuted')) {
        Assert-LedgerFixture "survivor-$outcome" Results {
            param($m)
            $m.rows[$headless][0].outcome = $outcome
        } @('Fixture.Tests.Keep', 'not Passed')
    }

    Assert-LedgerFixture 'removed-case-restored' Results {
        param($m)
        $m.rows[$headless] += @{ method = 'Fixture.Tests.Theory'; name = 'Fixture.Tests.Theory(value: 1)'; outcome = 'Passed' }
    } @('Fixture.Tests.Theory(value: 1)', 'Removed case present')
    Assert-LedgerFixture 'kept-case-missing' Results {
        param($m)
        $m.rows[$headless] = @($m.rows[$headless] | Where-Object name -NE 'Fixture.Tests.Theory(value: 10)')
    } @('Fixture.Tests.Theory(value: 10)', 'missing from TRX')
    Assert-LedgerFixture 'kept-case-not-passed' Results {
        param($m)
        ($m.rows[$headless] | Where-Object name -EQ 'Fixture.Tests.Theory(value: 10)').outcome = 'NotExecuted'
    } @('Fixture.Tests.Theory(value: 10)', 'not Passed')
    Assert-LedgerFixture 'kept-method-not-discovered' Results {
        param($m)
        $m.manifests[$headless].tests = @($m.manifests[$headless].tests | Where-Object Method -NE 'Theory')
    } @('Fixture.Tests.Theory(value: 0)', 'Fixture.Tests.Theory(value: 10)', 'not discovered')
    Assert-LedgerFixture 'shard-case-not-passed' Results {
        param($m)
        $m.sharded = $true
        ($m.rows[$headless] | Where-Object name -EQ 'Fixture.Tests.Theory(value: 10)').outcome = 'Failed'
    } @('Fixture.Tests.Theory(value: 10)', 'not Passed')
    Assert-LedgerFixture 'shard-duplicate' Results {
        param($m)
        $m.sharded = $true
        $m.rows[$headless] += $m.rows[$headless][1]
    } @('Fixture.Tests.Render', 'repeated across shard TRX')
    Write-Host "Ledger fixtures passed ($fixtureCount scenarios)."
} finally {
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Fixture cleanup path is outside the temporary directory: $resolved"
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force
}
