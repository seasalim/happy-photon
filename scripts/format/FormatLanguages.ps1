function Get-FormatPins {
    return Get-Content -LiteralPath (Join-Path $script:FormatHome 'config/versions.json') -Raw | ConvertFrom-Json
}

function Add-FormatFinding {
    param($Check, [string]$Code, [string]$Detail)

    if (-not $Check.Findings.Contains($Code)) {
        $Check.Findings.Add($Code)
    }

    if ($Detail) {
        $Check.Details.Add($Detail)
    }
}

function Add-FormatError {
    param($Check, [string]$Message, [string]$DefaultCode)

    $code = [regex]::Match($Message, '^([A-Z-]+):').Groups[1].Value

    if (-not $code) {
        $code = $DefaultCode
    }

    Add-FormatFinding $Check $code $Message
}

# Formatters see LF text with one final newline; the file's own newline style and BOM are restored afterwards.
function Complete-FormatText {
    param([string]$Text)

    $text = $Text.Replace("`r`n", "`n").TrimEnd("`n")

    if ($text.Length -eq 0) {
        return ''
    }

    return $text + "`n"
}

function Assert-FormatTools {
    param([string]$ToolRoot, [string]$Scratch)

    $pins = Get-FormatPins
    $manifest = Join-Path $ToolRoot "PSScriptAnalyzer/$($pins.pssa)/PSScriptAnalyzer.psd1"
    $prettier = Join-Path $ToolRoot 'node_modules/prettier/bin/prettier.cjs'

    if (-not (Test-Path -LiteralPath $manifest -PathType Leaf) -or -not (Test-Path -LiteralPath $prettier -PathType Leaf)) {
        throw "VERSION: formatter tools are missing from $ToolRoot; run scripts/format/install-format-tools.ps1"
    }

    if ((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -ne $pins.pssaManifestSha256) {
        throw 'VERSION: PSScriptAnalyzer manifest hash does not match the pin'
    }

    # The scratch global.json selects the pinned SDK with roll-forward disabled.
    $environment = Get-ChildEnvironment $Scratch
    $sdk = Invoke-FormatProcess -Program dotnet -Arguments @('--version') -WorkingDirectory $Scratch `
        -Environment $environment -TimeoutSeconds 120 -AllowFailure

    if ($sdk.ExitCode -ne 0) {
        throw "VERSION: .NET SDK $($pins.sdk) is required and is not installed"
    }

    if ($sdk.Output.Trim() -ne $pins.sdk) {
        throw "VERSION: .NET SDK $($pins.sdk) is required; dotnet selected $($sdk.Output.Trim())"
    }

    $version = Invoke-FormatProcess -Program node -Arguments @($prettier, '--version') -WorkingDirectory $Scratch `
        -Environment $environment -TimeoutSeconds 120 -AllowFailure

    if ($version.ExitCode -ne 0 -or $version.Output.Trim() -ne $pins.prettier) {
        throw "VERSION: Prettier $($pins.prettier) is required; found '$($version.Output.Trim())'"
    }

    try {
        $module = Import-Module -Name $manifest -PassThru -ErrorAction Stop
    } catch {
        throw "VERSION: PSScriptAnalyzer $($pins.pssa) could not be loaded (run in a new pwsh process): $($_.Exception.Message)"
    }

    if ($module.Version.ToString() -ne $pins.pssa -or (Get-Command Invoke-Formatter).Module.Version.ToString() -ne $pins.pssa) {
        throw "VERSION: PSScriptAnalyzer $($pins.pssa) is required; loaded $($module.Version)"
    }

    if (-not ('Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree' -as [type])) {
        Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
        Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
    }

    return $prettier
}

function New-FormatCheck {
    param($Selection)

    $check = [pscustomobject]@{
        Root = $Selection.Root
        RootLinks = $Selection.RootLinks
        Path = $Selection.Path
        File = $Selection.File
        Family = $Selection.Family
        Bytes = [IO.File]::ReadAllBytes($Selection.File)
        Bom = $false
        Newline = "`n"
        Text = $null
        Canonical = $null
        Output = $null
        Findings = [Collections.Generic.List[string]]::new()
        Details = [Collections.Generic.List[string]]::new()
    }
    $bytes = $check.Bytes
    $check.Bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $offset = 0

    if ($check.Bom) {
        $offset = 3
    }

    try {
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes, $offset, $bytes.Length - $offset)
    } catch {
        Add-FormatFinding $check 'ENCODING' 'not valid UTF-8'
        return $check
    }

    # Normalizing mixed endings could change a multi-line literal, so they are fixed by hand.
    $crlf = [regex]::Matches($text, "`r`n").Count

    if ([regex]::IsMatch($text, "`r(?!`n)") -or ($crlf -and [regex]::IsMatch($text, "(?<!`r)`n"))) {
        Add-FormatFinding $check 'LINE-ENDINGS' 'mixed or bare-CR line endings'
        return $check
    }

    if ($crlf) {
        $check.Newline = "`r`n"
    }

    $check.Text = $text.Replace("`r`n", "`n")

    return $check
}

function Write-ScratchCopy {
    param([string]$Folder, [int]$Index, $Check)

    # One folder per file keeps the original name, which some parsers infer from (package.json, .mjs).
    $directory = Join-Path $Folder $Index
    [void][IO.Directory]::CreateDirectory($directory)
    $file = Join-Path $directory ($Check.Path -split '/')[-1]
    [IO.File]::WriteAllText($file, $Check.Text, [Text.UTF8Encoding]::new($false))

    return $file
}

function Read-ScratchCopy {
    param([string]$File)

    return Complete-FormatText ([IO.File]::ReadAllText($File, [Text.UTF8Encoding]::new($false)))
}

function Get-CSharpTree {
    param([string]$Text)

    # FileBasedProgram admits the '#:' directives of .NET 10 single-file programs under scripts/.
    $feature = [Collections.Generic.KeyValuePair[string, string]]::new('FileBasedProgram', 'true')
    $options = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::new([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::Preview)
    $options = $options.WithFeatures([Collections.Generic.KeyValuePair[string, string][]]@($feature))
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($Text, $options)
    $errors = @($tree.GetDiagnostics() | Where-Object { $_.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error })

    if ($errors.Count) {
        throw "PARSE: $($errors[0].ToString())"
    }

    return $tree
}

# Trivia-insensitive tree comparison: every token, literal text included, must be unchanged.
function Test-CSharpEquivalent {
    param($Before, $After)

    return [Microsoft.CodeAnalysis.CSharp.SyntaxFactory]::AreEquivalent($Before, $After, $false)
}

function Format-CSharpBatch {
    param([object[]]$Checks, [string]$Scratch)

    $folder = Join-Path $Scratch 'csharp'
    $pending = [Collections.Generic.List[object]]::new()

    for ($i = 0; $i -lt $Checks.Count; $i++) {
        try {
            $tree = Get-CSharpTree $Checks[$i].Text
        } catch {
            Add-FormatError $Checks[$i] $_.Exception.Message 'PARSE'
            continue
        }

        $file = Write-ScratchCopy $folder $i $Checks[$i]
        $pending.Add([pscustomobject]@{ Check = $Checks[$i]; Tree = $tree; File = $file })
    }

    if (-not $pending.Count) {
        return
    }

    $arguments = @('format', 'whitespace', $folder, '--folder', '--verbosity', 'quiet')
    [void](Invoke-FormatProcess -Program dotnet -Arguments $arguments -WorkingDirectory $Scratch `
            -Environment (Get-ChildEnvironment $Scratch))

    foreach ($item in $pending) {
        $formatted = Read-ScratchCopy $item.File

        try {
            $same = Test-CSharpEquivalent $item.Tree (Get-CSharpTree $formatted)
        } catch {
            $same = $false
        }

        # Whitespace formatting must leave every token and literal value unchanged.
        if (-not $same) {
            Add-FormatFinding $item.Check 'SEMANTICS' 'C# formatting changed a token or literal'
            continue
        }

        $item.Check.Canonical = $formatted
    }
}

function Get-PowerShellShape {
    param([string]$Text)

    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput($Text, [ref]$tokens, [ref]$errors)

    if ($errors.Count) {
        throw "PARSE: $($errors[0].Message) (line $($errors[0].Extent.StartLineNumber))"
    }

    $words = foreach ($token in $tokens) {
        if ($token.Kind -notin @('NewLine', 'LineContinuation')) {
            "$($token.Kind) $($token.Text)"
        }
    }
    # Pre-order node types with their depths fix the tree's shape, so a moved line break cannot regroup statements.
    $nodes = foreach ($node in $ast.FindAll({ $true }, $true)) {
        $depth = 0

        for ($parent = $node.Parent; $null -ne $parent; $parent = $parent.Parent) {
            $depth++
        }

        "$depth $($node.GetType().Name)"
    }

    return (@($words) + '--' + @($nodes)) -join "`0"
}

function Format-PowerShellBatch {
    param([object[]]$Checks)

    $settings = Join-Path $script:FormatHome 'config/PSScriptAnalyzerSettings.psd1'

    foreach ($check in $Checks) {
        try {
            $before = Get-PowerShellShape $check.Text
        } catch {
            Add-FormatError $check $_.Exception.Message 'PARSE'
            continue
        }

        $formatted = Complete-FormatText (Invoke-Formatter -ScriptDefinition $check.Text -Settings $settings)

        try {
            $after = Get-PowerShellShape $formatted
        } catch {
            $after = $null
        }

        # Tokens other than line breaks, string bodies included, and the syntax tree must be unchanged.
        if ($after -cne $before) {
            Add-FormatFinding $check 'SEMANTICS' 'PowerShell formatting changed a token, literal or statement'
            continue
        }

        $check.Canonical = $formatted
    }
}
