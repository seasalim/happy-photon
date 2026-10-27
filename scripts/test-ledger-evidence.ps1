# Evidence readers for check-test-ledger.ps1. Importing this file performs no work.
function Get-LedgerDeclarations([string] $Project, [string[]] $Targets) {
    . (Join-Path $PSScriptRoot 'test-profile-conditions.ps1')
    Initialize-ProfileSyntax
    $projectPath = Join-Path $RepoRoot $Project

    if (-not $SourceCache.projects.ContainsKey($projectPath)) {
        $json = & dotnet msbuild $projectPath -p:Configuration=Release `
            -getItem:Compile -getProperty:DefineConstants

        if ($LASTEXITCODE -ne 0) { throw "Cannot evaluate Compile items for $Project." }

        $SourceCache.projects[$projectPath] = $json -join "`n" | ConvertFrom-Json
    }

    $evaluated = $SourceCache.projects[$projectPath]
    $symbols = @($evaluated.Properties.DefineConstants -split ';' | Where-Object { $_ })
    $options = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithPreprocessorSymbols([string[]]$symbols)
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

    # Preserve every spelling: C# identifiers and the candidate regex are case-sensitive.
    $simpleNames = @($Targets | ForEach-Object { [regex]::Escape(($_ -split '[.+]')[-1]) })
    # Escaped identifiers and Unicode formatting characters can hide a simple name.
    # Parse those files too; Roslyn still decides whether any declaration exists.
    $candidate = [regex]::new(($simpleNames -join '|') + '|\\[uU][0-9a-fA-F]|\p{Cf}')

    foreach ($item in $evaluated.Items.Compile) {
        $source = [IO.File]::ReadAllText($item.FullPath)
        if (-not $candidate.IsMatch($source)) { continue }

        $cached = $SourceCache.files[$item.FullPath]

        if ($null -eq $cached -or $cached.source -cne $source -or
            $cached.symbols -cne $evaluated.Properties.DefineConstants) {
            $cached = @{
                source = $source
                symbols = $evaluated.Properties.DefineConstants
                names = Get-LedgerFileDeclarations $source $options
            }
            $SourceCache.files[$item.FullPath] = $cached
        }

        $names.UnionWith($cached.names)
    }

    return ,$names
}

function Get-LedgerFileDeclarations([string] $Source, $Options) {
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($Source, $Options)
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

    $containers = [Collections.Generic.Queue[object]]::new()
    $containers.Enqueue($tree.GetRoot())

    while ($containers.Count -gt 0) {
        $type = $containers.Dequeue()

        # Class and namespace members can declare tests; method bodies cannot.
        foreach ($member in $type.Members) {
            if ($member -is [Microsoft.CodeAnalysis.CSharp.Syntax.BaseNamespaceDeclarationSyntax] -or
                $member -is [Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax]) {
                $containers.Enqueue($member)
            }
        }

        if ($type -isnot [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax]) { continue }

        $ancestors = @($type.Ancestors())
        [array]::Reverse($ancestors)
        $parts = @(foreach ($ancestor in $ancestors) {
            switch ($ancestor.GetType().Name) {
                { $_ -in @('FileScopedNamespaceDeclarationSyntax', 'NamespaceDeclarationSyntax') } { $ancestor.Name.ToString() }

                'ClassDeclarationSyntax' { $ancestor.Identifier.ValueText }
            }
        })
        $class = (@($parts) + $type.Identifier.ValueText) -join '.'
        $null = $names.Add($class)

        foreach ($member in $type.Members | Where-Object { $_.GetType().Name -eq 'MethodDeclarationSyntax' }) {
            $null = $names.Add("$class.$($member.Identifier.ValueText)")
        }
    }

    return ,$names
}

function Test-LedgerSources {
    $declarations = @{}

    foreach ($project in @(@($removals | ForEach-Object { $_.project }) + @($methods | ForEach-Object { $_.project }) | Sort-Object -Unique)) {
        try {
            $targets = @($removals | Where-Object { $_.project -ceq $project -and -not $_.case } | ForEach-Object name) +
                @($methods | Where-Object project -CEQ $project | ForEach-Object name)
            $declarations[$project] = Get-LedgerDeclarations $project $targets
        } catch {
            $issues.Add("Cannot check sources in ${project}: $($_.Exception.Message)")
        }
    }

    foreach ($removal in $removals) {
        if (-not $removal.case -and $declarations.ContainsKey($removal.project) -and
            $declarations[$removal.project].Contains($removal.name.Replace('+', '.'))) {
            $issues.Add("Removed name still declared in $($removal.project): $($removal.name).")
        }
    }

    foreach ($method in $methods) {
        if ($declarations.ContainsKey($method.project) -and -not $declarations[$method.project].Contains($method.name.Replace('+', '.'))) {
            $issues.Add("Trimmed method not declared in $($method.project): $($method.name).")
        }
    }
}

function Read-LedgerResults {
    $evidence = @{}
    $assemblies = @{}

    foreach ($project in $projects.Keys) {
        $evidence[$project] = @{
            discovered = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            results = [Collections.Generic.List[object]]::new()
        }
        $assembly = "$($projects[$project]).dll"

        if ($assemblies.ContainsKey($assembly)) { $issues.Add("Ambiguous ledger assembly $assembly for $project.") }

        $assemblies[$assembly] = $project
        $path = Join-Path $ResultsDirectory "manifests/$($projects[$project]).manifest.json"

        try {
            $manifest = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json -AsHashtable

            if ($manifest['schemaVersion'] -ne 1 -or $manifest['project'] -cne $project -or $manifest['tests'] -isnot [array]) {
                throw "Invalid discovery manifest for $project."
            }

            foreach ($test in $manifest['tests']) {
                if (-not (Test-LedgerText $test['Class']) -or -not (Test-LedgerText $test['Method'])) {
                    throw "Manifest test lacks Class/Method in $project."
                }

                $null = $evidence[$project].discovered.Add("$($test['Class']).$($test['Method'])")
            }
        } catch {
            $issues.Add("Cannot read manifest for ${project}: $($_.Exception.Message)")
        }
    }

    $seen = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $projectFiles = @{}

    foreach ($file in Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -Recurse -File) {
        try {
            [xml]$trx = Get-Content -Raw -LiteralPath $file.FullName
            $ns = [Xml.XmlNamespaceManager]::new($trx.NameTable)
            $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
            $definitions = @{}

            foreach ($definition in $trx.SelectNodes('//t:TestDefinitions/t:UnitTest', $ns)) {
                $method = $definition.SelectSingleNode('t:TestMethod', $ns)
                if ($null -eq $method) { throw "Missing TestMethod for $($definition.GetAttribute('id'))." }

                $assembly = ($method.GetAttribute('codeBase') -replace '\\', '/').Split('/')[-1]

                if (-not $assemblies.ContainsKey($assembly)) {
                    $definitions[$definition.GetAttribute('id')] = $null
                    continue
                }

                $project = $assemblies[$assembly]
                $class = $method.GetAttribute('className').Split(',')[0]
                $name = $method.GetAttribute('name').Split('(')[0]
                $definitions[$definition.GetAttribute('id')] = @{ project = $project; method = "$class.$name" }

                if ($projectFiles.ContainsKey($project) -and $projectFiles[$project] -ne $file.FullName -and
                    $project -ne 'HeadlessTests/HappyPhoton.Headless.Tests.csproj') {
                    $issues.Add("Multiple TRX files for $project in $ResultsDirectory.")
                }

                $projectFiles[$project] = $file.FullName
            }

            foreach ($result in $trx.SelectNodes('//t:UnitTestResult', $ns)) {
                $id = $result.GetAttribute('testId')

                if (-not $definitions.ContainsKey($id)) {
                    $issues.Add("TRX result has no definition: $($result.GetAttribute('testName')) in $($file.FullName).")
                    continue
                }

                $definition = $definitions[$id]
                if ($null -eq $definition) { continue }

                $project = $definition.project
                $name = $result.GetAttribute('testName')
                $key = "$project|$name"

                if ($seen.ContainsKey($key) -and $seen[$key] -ne $file.FullName) {
                    $issues.Add("Test repeated across shard TRX files in ${project}: $name.")
                }

                $seen[$key] = $file.FullName
                $evidence[$project].results.Add(@{
                    method = $definition.method; name = $name; outcome = $result.GetAttribute('outcome')
                })
            }
        } catch {
            $issues.Add("Cannot read TRX $($file.FullName): $($_.Exception.Message)")
        }
    }

    return $evidence
}

function Test-LedgerResults {
    $evidence = Read-LedgerResults

    foreach ($reference in $requirements) {
        $project = $reference.project
        $label = "$($reference.name) in $project"

        if (-not $evidence[$project].discovered.Contains($reference.test)) {
            $issues.Add("Ledger test not discovered: $label (method $($reference.test)).")
        }

        $matches = @($evidence[$project].results | Where-Object {
            $_.method -ceq $reference.test -and (-not $reference.case -or $_.name -ceq $reference.name)
        })

        if ($matches.Count -eq 0) {
            $issues.Add("Ledger test missing from TRX: $label.")
        }

        foreach ($result in $matches | Where-Object outcome -CNE 'Passed') {
            $issues.Add("Ledger test not Passed: $($result.name) in $project ($($result.outcome)).")
        }
    }

    foreach ($removal in $removals | Where-Object case) {
        if ($evidence[$removal.project].results | Where-Object name -CEQ $removal.name) {
            $issues.Add("Removed case present in TRX in $($removal.project): $($removal.name).")
        }
    }
}
