function Format-PrettierBatch {
    param([object[]]$Checks, [string]$Scratch, [string]$Prettier)

    $folder = Join-Path $Scratch 'prettier'
    $files = @(for ($i = 0; $i -lt $Checks.Count; $i++) {
            Write-ScratchCopy $folder $i $Checks[$i]
        })

    if (-not $files.Count) {
        return
    }

    $environment = Get-ChildEnvironment $Scratch
    $common = @($Prettier, '--config', (Join-Path $script:FormatHome 'config/prettier.json'), '--no-editorconfig', '--log-level', 'warn')

    # Prettier's documented --debug-check proves the output parses to an equivalent AST, literal values included.
    $batch = Invoke-FormatProcess -Program node -Arguments ($common + '--debug-check' + $folder) -WorkingDirectory $Scratch `
        -Environment $environment -AllowFailure
    $good = [Collections.Generic.List[int]]::new()

    for ($i = 0; $i -lt $files.Count; $i++) {
        $single = $batch

        if ($batch.ExitCode -ne 0) {
            $single = Invoke-FormatProcess -Program node -Arguments ($common + '--debug-check' + $files[$i]) `
                -WorkingDirectory $Scratch -Environment $environment -AllowFailure
        }

        if ($single.ExitCode -eq 0) {
            $good.Add($i)
            continue
        }

        # Only a parser diagnostic or an equivalence failure is a finding; any other failure stops the run.
        $message = "$(($single.Error + $single.Output) -split "`n" | Where-Object { $_.Trim() } | Select-Object -First 1)".Trim()

        if ($message -match '!==') {
            Add-FormatFinding $Checks[$i] 'SEMANTICS' "Prettier: $message"
        } elseif ($message -match ': SyntaxError: ') {
            Add-FormatFinding $Checks[$i] 'PARSE' "Prettier: $message"
        } else {
            throw "PROCESS: Prettier failed on $($Checks[$i].Path): $message"
        }

        Remove-Item -LiteralPath (Split-Path $files[$i]) -Recurse -Force
    }

    if (-not $good.Count) {
        return
    }

    [void](Invoke-FormatProcess -Program node -Arguments ($common + '--write' + $folder) -WorkingDirectory $Scratch `
            -Environment $environment)

    foreach ($i in $good) {
        $formatted = Read-ScratchCopy $files[$i]

        if ($Checks[$i].Path.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase) -and
            -not (Test-JsonEquivalent $Checks[$i] $formatted)) {
            continue
        }

        $Checks[$i].Canonical = $formatted
    }
}

# Minified rewrite that drops only whitespace, comments and trailing commas; number text stays raw.
function Get-JsonShape {
    param([string]$Text)

    $options = [Text.Json.JsonDocumentOptions]::new()
    $options.CommentHandling = [Text.Json.JsonCommentHandling]::Skip
    $options.AllowTrailingCommas = $true
    $document = [Text.Json.JsonDocument]::Parse($Text, $options)
    $stream = [IO.MemoryStream]::new()
    $writer = [Text.Json.Utf8JsonWriter]::new($stream)

    try {
        $document.RootElement.WriteTo($writer)
        $writer.Flush()
        return [Text.Encoding]::UTF8.GetString($stream.ToArray())
    } finally {
        $writer.Dispose()
        $document.Dispose()
    }
}

# Prettier treats 2.50 and 2.5 as the same number, but JSON read as decimal keeps the difference.
function Test-JsonEquivalent {
    param($Check, [string]$Formatted)

    try {
        $before = Get-JsonShape $Check.Text
    } catch {
        Add-FormatFinding $Check 'PARSE' "JSON: $($_.Exception.Message)"
        return $false
    }

    try {
        $after = Get-JsonShape $Formatted
    } catch {
        $after = $null
    }

    if ($after -cne $before) {
        Add-FormatFinding $Check 'SEMANTICS' 'Prettier changed a JSON key, string or number text'
        return $false
    }

    return $true
}
