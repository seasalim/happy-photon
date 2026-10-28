# Shared readers used by the sampler, inventory and synthetic checks. No commands run on import.
Set-StrictMode -Version Latest

function Get-ProfileSum($Items, [string] $Property) {
    $sum = 0.0

    foreach ($item in $Items) {
        $sum += [double]$item.$Property
    }

    return $sum
}

function Read-ProfileTrx([string] $Directory) {
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Filter *.trx -Recurse -File) {
        [xml]$xml = Get-Content -Raw -LiteralPath $file.FullName
        $ns = [Xml.XmlNamespaceManager]::new($xml.NameTable)
        $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        $definitions = @{}

        foreach ($definition in $xml.SelectNodes('//t:TestDefinitions/t:UnitTest', $ns)) {
            $method = $definition.SelectSingleNode('t:TestMethod', $ns)
            $hostName = [IO.Path]::GetFileNameWithoutExtension(($method.codeBase -replace '\\', '/'))
            $definitions[$definition.id] = @{ host = $hostName; class = [string]$method.className; method = [string]$method.name }
        }

        foreach ($result in $xml.SelectNodes('//t:UnitTestResult', $ns)) {
            $definition = $definitions[$result.testId]
            if (-not $definition) { throw "TRX definition missing: $($result.testId)" }
            if ($definition.host -notin @('HappyPhoton.Tests', 'HappyPhoton.Headless.Tests', 'HappyPhoton.LibRaw.Interop.Tests')) { continue }

            [pscustomobject]@{
                host = $definition.host
                class = ($definition.class -split ',')[0]
                method = $definition.method
                name = [string]$result.testName
                outcome = [string]$result.outcome
                start = [DateTimeOffset]::Parse($result.startTime)
                end = [DateTimeOffset]::Parse($result.endTime)
                seconds = [TimeSpan]::Parse($result.duration, [Globalization.CultureInfo]::InvariantCulture).TotalSeconds
                oom = $result.InnerText -match 'OutOfMemoryException'
                trx = $file.FullName
            }
        }
    }
}

function Get-ProfileAttribution($Samples, $Tests) {
    $classes = @{}
    $hosts = foreach ($group in @($Samples | Group-Object instance)) {
        $rows = @($group.Group | Sort-Object { [DateTimeOffset]$_.utc })
        $hostName = $rows[0].host
        $bound = if ($hostName -eq 'HappyPhoton.Tests') { 2 } else { 1 }
        $spans = @($Tests | Where-Object { $_.host -eq $hostName -and $_.outcome -ne 'NotExecuted' })
        $spans = @($spans | Sort-Object { [DateTimeOffset]$_.start })
        $active = [Collections.Generic.List[object]]::new()
        $cursor = 0
        $attributed = 0
        $over = [Collections.Generic.List[object]]::new()
        [long]$previous = $rows[0].pagedBytes
        $unattributedGrowth = 0.0

        foreach ($row in $rows) {
            $time = [DateTimeOffset]$row.utc

            while ($cursor -lt $spans.Count -and [DateTimeOffset]$spans[$cursor].start -le $time) {
                $active.Add($spans[$cursor])
                $cursor++
            }

            for ($i = $active.Count - 1; $i -ge 0; $i--) {
                if ([DateTimeOffset]$active[$i].end -le $time) { $active.RemoveAt($i) }
            }

            [long]$growth = [Math]::Max([long]0, ([long]$row.pagedBytes - $previous))
            $previous = [long]$row.pagedBytes

            if ($active.Count -gt $bound) {
                $over.Add(@{ utc = $row.utc; tests = @($active.name) })
            }

            if ($active.Count -eq 0) {
                $unattributedGrowth += $growth
                continue
            }

            $attributed++

            foreach ($test in $active) {
                $key = "$hostName|$($test.class)"

                if (-not $classes.ContainsKey($key)) {
                    $classes[$key] = [pscustomobject]@{ host = $hostName; class = $test.class; growthBytes = 0.0 }
                }

                # Split positive growth equally across active test cases, including theories.
                $classes[$key].growthBytes += $growth / $active.Count
            }
        }

        $share = $attributed / $rows.Count
        [pscustomobject]@{
            instance = $group.Name; host = $hostName; samples = $rows.Count
            attributedSamples = $attributed; attributedShare = $share
            parallelismBound = $bound; overBoundCount = $over.Count; overlaps = @($over.ToArray())
            unattributedGrowthBytes = $unattributedGrowth
            g4Passed = $share -ge 0.95 -and $over.Count -eq 0
        }
    }

    $totals = @(foreach ($group in @($hosts | Group-Object host)) {
        $count = (Get-ProfileSum $group.Group samples)
        $attributed = (Get-ProfileSum $group.Group attributedSamples)
        $overBound = (Get-ProfileSum $group.Group overBoundCount)
        [pscustomobject]@{
            host = $group.Name; samples = $count; attributedSamples = $attributed
            attributedShare = $attributed / $count; overBoundCount = $overBound
            unattributedGrowthBytes = (Get-ProfileSum $group.Group unattributedGrowthBytes)
            parallelismBound = $group.Group[0].parallelismBound
            g4Passed = $attributed / $count -ge 0.95 -and $overBound -eq 0
        }
    })

    return @{ hosts = $totals; processes = @($hosts); classes = @($classes.Values | Sort-Object growthBytes -Descending) }
}
