# Syntax-aware source extraction uses Roslyn shipped with the installed SDK, not a new dependency.
function Initialize-ProfileSyntax {
    if ('Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree' -as [type]) { return }

    $sdk = (& dotnet --list-sdks | Select-Object -Last 1)
    if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'Cannot locate SDK Roslyn.' }

    $compiler = Join-Path $Matches[2] "$($Matches[1])/Roslyn/bincore"
    Add-Type -Path "$compiler/Microsoft.CodeAnalysis.dll"
    Add-Type -Path "$compiler/Microsoft.CodeAnalysis.CSharp.dll"
}

function Read-ProfileProject([string] $Project) {
    $json = & dotnet msbuild $Project -p:Configuration=Release -getItem:Compile -getProperty:DefineConstants
    if ($LASTEXITCODE -ne 0) { throw "Cannot evaluate Release Compile items and symbols: $Project" }

    $evaluated = $json -join "`n" | ConvertFrom-Json

    return Read-ProfileSources @($evaluated.Items.Compile.FullPath) @($evaluated.Properties.DefineConstants -split ';' | Where-Object { $_ })
}

function Read-ProfileSources([string[]] $Files, [string[]] $Symbols = @()) {
    Initialize-ProfileSyntax
    $index = @{}
    $options = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithPreprocessorSymbols($Symbols)

    foreach ($file in $Files) {
        $source = Get-Content -Raw -LiteralPath $file
        $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($source, $options)
        $directives = @($tree.GetRoot().DescendantTrivia() | Where-Object HasStructure |
            ForEach-Object GetStructure | Where-Object { $_.GetType().Name -match '^(If|Elif|Else|EndIf)DirectiveTriviaSyntax$' })

        foreach ($type in $tree.GetRoot().DescendantNodes() | Where-Object { $_.GetType().Name -eq 'ClassDeclarationSyntax' }) {
            $ancestors = @($type.Ancestors())
            $namespace = @($ancestors | Where-Object { $_.GetType().Name -in @('FileScopedNamespaceDeclarationSyntax', 'NamespaceDeclarationSyntax') })
            $name = (@($namespace | ForEach-Object { $_.Name.ToString() }) + $type.Identifier.ValueText) -join '.'
            if (-not $index.ContainsKey($name)) { $index[$name] = [Collections.Generic.List[object]]::new() }

            foreach ($member in $type.Members) {
                $index[$name].Add([pscustomobject]@{ node = $member; class = $name; file = $file; source = $source; directives = $directives })
            }
        }
    }

    $script:profileTypeNames = @{}

    foreach ($key in $index.Keys) {
        $short = ($key -split '\.')[-1]
        if (-not $script:profileTypeNames.ContainsKey($short)) { $script:profileTypeNames[$short] = @() }
        $script:profileTypeNames[$short] += $key
    }

    return $index
}

function Get-MethodCondition($Index, [string] $Class, [string] $Method) {
    $result = @{ category = 'always-on'; variables = @(); predicted = 'Passed'; unresolved = @(); evidence = @() }
    if (-not $Index.ContainsKey($Class)) {
        $result.unresolved += 'Class source not found in evaluated Compile items.'
        return $result
    }

    $members = @($Index[$Class].ToArray())
    $methods = @($members | Where-Object { $_.node.GetType().Name -eq 'MethodDeclarationSyntax' -and $_.node.Identifier.ValueText -eq $Method })
    if ($methods.Count -ne 1) {
        $result.unresolved += 'Method missing or overloaded; supply an explicit resolution.'
        return $result
    }

    $queue = [Collections.Generic.Queue[object]]::new()
    $queue.Enqueue($methods[0])
    $visited = [Collections.Generic.HashSet[string]]::new()

    foreach ($constructor in $members | Where-Object { $_.node.GetType().Name -eq 'ConstructorDeclarationSyntax' }) {
        $queue.Enqueue($constructor)
    }

    while ($queue.Count) {
        $entry = $queue.Dequeue()
        $node = $entry.node
        if (-not $visited.Add("$($entry.file):$($node.SpanStart)")) { continue }

        # Token text excludes inactive preprocessor trivia and comments.
        $text = ($node.DescendantTokens() | ForEach-Object Text) -join ' '
        $activeLines = @(if ($entry.directives.Count) { $node.DescendantNodes() | Where-Object {
            ($_.GetType().Name -eq 'ReturnStatementSyntax' -and -not $_.Expression) -or
            ($_.GetType().Name -eq 'InvocationExpressionSyntax' -and $_.Expression.ToString() -match '^(?:Assert\.)?Skip(?:When|Unless)?$')
        } | ForEach-Object { $_.GetLocation().GetLineSpan().StartLinePosition.Line } })
        $result.evidence += @{ file = $entry.file; line = $node.GetLocation().GetLineSpan().StartLinePosition.Line + 1 }
        $variables = @{}

        foreach ($declaration in $node.DescendantNodes() | Where-Object { $_.GetType().Name -eq 'VariableDeclaratorSyntax' }) {
            if ($declaration.Initializer) { $variables[$declaration.Identifier.ValueText] = $declaration.Initializer.Value.ToString() }
        }

        foreach ($invocation in $node.DescendantNodes() | Where-Object { $_.GetType().Name -eq 'InvocationExpressionSyntax' }) {
            $called = $invocation.Expression.ToString()

            if ($called -eq 'Environment.GetEnvironmentVariable' -and $invocation.ArgumentList.Arguments.Count -eq 1) {
                $argument = $invocation.ArgumentList.Arguments[0].Expression
                if ($argument.GetType().Name -eq 'LiteralExpressionSyntax') { $result.variables += $argument.Token.ValueText }
            }

            if ($called -match '^(?:Assert\.)?Skip(?:When|Unless)?$') {
                $condition = $invocation.ArgumentList.Arguments[0].Expression.ToString()
                $conditional = @($invocation.Ancestors() | Where-Object { $_.GetType().Name -in @('IfStatementSyntax', 'ConditionalExpressionSyntax') }).Count -gt 0

                foreach ($key in $variables.Keys) {
                    $condition = [regex]::Replace($condition, "\b$([regex]::Escape($key))\b", [Text.RegularExpressions.MatchEvaluator]{ param($m) $variables[$m.Value] })
                }

                $condition = $condition -replace '\s+', ''
                $presence = [regex]::Match($condition, '^(!?)string\.IsNullOr(?:Empty|WhiteSpace)\(Environment\.GetEnvironmentVariable\("(?<var>[^"]+)"\)\)$')
                $gate = [regex]::Match($condition, '^Environment\.GetEnvironmentVariable\("(?<var>[^"]+)"\)\s*(?<op>==|!=)\s*(?<value>"[^"]*"|null)$')

                if (($gate.Success -or $presence.Success) -and -not $conditional) {
                    $variable = if ($presence.Success) { $presence.Groups['var'].Value } else { $gate.Groups['var'].Value }
                    $result.variables += $variable
                    $equals = $gate.Groups['value'].Value -eq 'null'
                    $truth = if ($presence.Success) { -not $condition.StartsWith('!') } elseif ($gate.Groups['op'].Value -eq '==') { $equals } else { -not $equals }
                    $skip = if ($called.EndsWith('Unless')) { -not $truth } else { $truth }
                    if ($result.category -ne 'opt-in') {
                        $result.category = if ($variable -match 'CHILD') { 'child-only' } elseif ($skip) { 'opt-in' } else { $result.category }
                    }
                    if ($skip) { $result.predicted = 'NotExecuted' }
                } elseif ($condition -match '^!?OperatingSystem\.Is(Windows|Linux|MacOS)\(\)$' -and -not $conditional) {
                    $truth = $condition -match 'IsWindows'
                    if ($condition.StartsWith('!')) { $truth = -not $truth }
                    if ($called.EndsWith('Unless')) { $truth = -not $truth }
                    if ($result.category -eq 'always-on') { $result.category = 'platform/build-conditioned' }
                    if ($truth) { $result.predicted = 'NotExecuted' }
                } else {
                    $result.unresolved += "Conditional skip: $($invocation.ToString())"
                }
            } else {
                # Follow same-class and explicitly named source helpers; never infer a gate from
                # another method merely because it shares a class or file.
                $helperClass = $entry.class
                $helperMethod = $called -replace '^.*\.', ''

                if ($called.Contains('.')) {
                    $qualifier = $called -replace '\.[^.]+$', ''
                    $candidates = if ($Index.ContainsKey($qualifier)) { @($qualifier) } elseif ($script:profileTypeNames.ContainsKey($qualifier)) { $script:profileTypeNames[$qualifier] } else { @() }
                    if (@($candidates).Count -eq 1) { $helperClass = @($candidates)[0] } else { continue }
                }

                foreach ($helper in $Index[$helperClass] | Where-Object {
                    $_.node.GetType().Name -eq 'MethodDeclarationSyntax' -and $_.node.Identifier.ValueText -eq $helperMethod
                }) {
                    $conditionalCall = @($invocation.Ancestors() | Where-Object {
                        $_.GetType().Name -in @('IfStatementSyntax', 'ConditionalExpressionSyntax', 'SwitchSectionSyntax')
                    }).Count -gt 0

                    if ($conditionalCall -and $helper.node.ToString() -match 'Skip|GetEnvironmentVariable') {
                        $result.unresolved += "Conditional helper call: $called"
                    }

                    $queue.Enqueue($helper)
                }
            }
        }

        if ($node -eq $methods[0].node) {
            $returns = @($node.DescendantNodes() | Where-Object {
                $_.GetType().Name -eq 'ReturnStatementSyntax' -and -not $_.Expression -and
                @($_.Ancestors() | Where-Object { $_.GetType().Name -match 'Lambda|LocalFunction' }).Count -eq 0
            })

            if ($returns.Count) {
                $result.category = 'silent-return'
                $result.predicted = 'Passed'
                $result.unresolved += 'Early return requires review of its precondition.'
            }
        }

        if (Test-ActiveProfileDirective $entry.directives $activeLines) {
            if ($result.category -eq 'always-on') { $result.category = 'platform/build-conditioned' }
            $result.unresolved += 'Build directive requires review for the measured configuration.'
        }

        if ($text -match '\bSkip\s*=|SkipWhen\s*=|SkipUnless\s*=|Explicit\s*=') {
            $result.unresolved += 'Attribute execution condition requires review.'
        }
    }

    $result.variables = @($result.variables | Sort-Object -Unique)
    $result.unresolved = @($result.unresolved | Sort-Object -Unique)

    return $result
}

# Inspect active branches only; disabled source is trivia, not executable syntax.
function Test-ActiveProfileDirective($Directives, $ActiveLines) {
    $stack = [Collections.Generic.List[bool]]::new()
    $cursor = 0

    foreach ($line in $ActiveLines | Sort-Object -Unique) {
        while ($cursor -lt $directives.Count -and $directives[$cursor].GetLocation().GetLineSpan().StartLinePosition.Line -le $line) {
            $directive = $directives[$cursor++]

            switch ($directive.GetType().Name) {
                'IfDirectiveTriviaSyntax' { $stack.Add($directive.BranchTaken) }

                'EndIfDirectiveTriviaSyntax' { $stack.RemoveAt($stack.Count - 1) }

                default { $stack[$stack.Count - 1] = $directive.BranchTaken }
            }
        }

        if ($stack.Count -and -not $stack.Contains($false)) { return $true }
    }

    return $false
}

function Resolve-ProfileCondition($Condition, $Resolutions, [string] $Project, [string] $Class, [string] $Method) {
    if (-not $Resolutions) { return $Condition }

    $found = @($Resolutions | Where-Object { $_.project -eq $Project -and $_.class -eq $Class -and $_.method -eq $Method })
    if ($found.Count -gt 1) { throw "Duplicate condition resolution: $Project $Class.$Method" }
    if (-not $found.Count) { return $Condition }

    $decision = $found[0]
    if (-not $decision.reason -or $decision.category -notin @('always-on', 'opt-in', 'platform/build-conditioned', 'child-only', 'silent-return') -or
        $decision.predicted -notin @('Passed', 'NotExecuted', 'Environment') -or
        ($decision.predicted -eq 'Environment' -and $decision.category -ne 'platform/build-conditioned')) {
        throw "Invalid resolution: $Class.$Method"
    }

    $casePredictions = @(if ($decision.PSObject.Properties['casePredictions']) { $decision.casePredictions })
    $caseNames = [Collections.Generic.HashSet[string]]::new()

    foreach ($case in $casePredictions) {
        if (-not $case.name.StartsWith("$Class.${Method}(") -or
            $case.predicted -notin @('Passed', 'NotExecuted') -or -not $caseNames.Add($case.name)) {
            throw "Invalid case resolution: $Class.$Method"
        }
    }

    $Condition.category = $decision.category
    $Condition.variables = @(@($Condition.variables) + @($decision.variables) | Sort-Object -Unique)
    $Condition.predicted = $decision.predicted
    $Condition.unresolved = @()
    $Condition.resolution = $decision.reason
    $Condition.casePredictions = $casePredictions

    return $Condition
}
