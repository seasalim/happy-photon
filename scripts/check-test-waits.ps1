[CmdletBinding()]
param(
    [string[]] $IncludePatterns = @(
        ":(glob)Tests/**/*.cs",
        ":(glob)HeadlessTests/**/*.cs")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$literalWaitPattern = [regex]::new(
    '\.WaitAsync\s*\(\s*TimeSpan\.',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)
$allowPattern = 'test-wait-policy:\s*allow\s*-\s*\S'

Push-Location $projectRoot
try {
    $sourceFiles = @(git ls-files --cached --others --exclude-standard -- $IncludePatterns)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not enumerate test source files."
    }

    $violations = foreach ($sourceFile in $sourceFiles) {
        $path = Join-Path $projectRoot $sourceFile
        $content = [System.IO.File]::ReadAllText($path)
        $lines = [System.IO.File]::ReadAllLines($path)
        foreach ($match in $literalWaitPattern.Matches($content)) {
            $lineNumber = [regex]::Matches(
                $content.Substring(0, $match.Index), "`n").Count + 1
            $currentLine = $lines[$lineNumber - 1]
            $previousLine = if ($lineNumber -gt 1) {
                $lines[$lineNumber - 2]
            } else {
                ""
            }
            if ("$previousLine`n$currentLine" -notmatch $allowPattern) {
                [pscustomobject]@{
                    File = $sourceFile
                    Line = $lineNumber
                }
            }
        }
    }

    if ($violations) {
        $detail = $violations | Format-Table -AutoSize | Out-String
        throw "Literal WaitAsync ceilings are not allowed. Use " +
            "TestWaits.Condition, or add 'test-wait-policy: allow - <reason>' " +
            "for a deliberate latency assertion.`n$detail"
    }

    Write-Host "Checked $($sourceFiles.Count) test files; async waits use the shared policy."
}
finally {
    Pop-Location
}
