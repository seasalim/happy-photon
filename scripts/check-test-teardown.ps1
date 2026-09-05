[CmdletBinding()]
param(
    [string[]] $IncludePatterns = @(
        ":(glob)Tests/**/*.cs",
        ":(glob)HeadlessTests/**/*.cs")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$teardownPattern = [regex]::new(
    '\bRequestedThemeVariant\s*=(?!=|>)|\.Show\s*\(|\bnew\s+MainWindow\b',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)
$allowPattern = '//[ \t]*test-teardown-policy:[ \t]*allow[ \t]*-[ \t]*\S'

Push-Location $projectRoot
try {
    $sourceFiles = @(git ls-files --cached --others --exclude-standard -- $IncludePatterns |
        Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not enumerate test source files."
    }

    $matches = @(foreach ($sourceFile in $sourceFiles) {
        $path = Join-Path $projectRoot $sourceFile
        $content = [System.IO.File]::ReadAllText($path)
        $lines = [System.IO.File]::ReadAllLines($path)
        foreach ($match in $teardownPattern.Matches($content)) {
            $lineNumber = [regex]::Matches(
                $content.Substring(0, $match.Index), "`n").Count + 1
            $currentLine = $lines[$lineNumber - 1]
            $previousLine = if ($lineNumber -gt 1) {
                $lines[$lineNumber - 2]
            } else {
                ""
            }
            $scopeApproved = $false
            if ($match.Value -match '^new\s+MainWindow') {
                # Require an unbound construction followed by its named binding scope.
                $tail = $content.Substring($match.Index)
                $declaration = [regex]::Match($content.Substring(0, $match.Index),
                    '\bvar\s+(\w+)\s*=\s*$')
                $scopeApproved = $declaration.Success -and $tail -match (
                    '^new\s+MainWindow(?:\s*\(\))?\s*(?:\{[^{}]*\}\s*)?;\s*(?:using\s+)?var\s+\w+\s*=\s*TestUiScope\.ForMainWindow\(\s*' +
                    [regex]::Escape($declaration.Groups[1].Value) + '\s*,')
                if ($tail -match '^new\s+MainWindow[^;]*\bDataContext\s*=') {
                    $scopeApproved = $false
                }
            }
            elseif ($match.Value -match '^\.Show') {
                $prefix = $content.Substring(0, $match.Index)
                $receiver = [regex]::Match($prefix, '(\w+)$').Value
                if ($receiver) {
                    $scopeApproved = $prefix -match ("\bvar\s+" + [regex]::Escape($receiver) +
                        '\s*=\s*TestUiScope\.ForMainWindow\(')
                }
            }
            [pscustomobject]@{
                File = $sourceFile
                Line = $lineNumber
                Approved = $scopeApproved -or $sourceFile.Replace('\', '/') -ceq 'Tests/TestUiScope.cs' -or
                    "$previousLine`n$currentLine" -match $allowPattern
            }
        }
    })
    $approved = @($matches | Where-Object Approved)
    $unresolved = @($matches | Where-Object { -not $_.Approved })
    Write-Host "Raw matches: $($matches.Count); approved exceptions: $($approved.Count); unresolved violations: $($unresolved.Count)"
    foreach ($violation in $unresolved) {
        Write-Host "$($violation.File):$($violation.Line)"
    }
    if ($unresolved.Count -gt 0) {
        throw "Test teardown requires failure-safe cleanup; unresolved sites need review."
    }
    Write-Host "Checked $($sourceFiles.Count) test files; teardown uses the shared policy."
}
finally {
    Pop-Location
}
