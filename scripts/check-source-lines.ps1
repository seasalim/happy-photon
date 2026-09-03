[CmdletBinding()]
param(
    [int]$MaximumLines = 499,
    # The ':(glob)' pathspec prefix keeps pwsh on Unix from expanding the
    # patterns against the working directory before git sees them.
    [string[]]$IncludePatterns = @(":(glob)**/*.cs", ":(glob)**/*.axaml")
)

$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot

try {
    if ($IncludePatterns.Count -eq 0) {
        throw "At least one include pattern is required."
    }
    $sourceFiles = @(git ls-files --cached --others --exclude-standard -- $IncludePatterns)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not enumerate source files."
    }

    $violations = foreach ($sourceFile in $sourceFiles) {
        $lineCount = ([System.IO.File]::ReadLines(
            (Join-Path $projectRoot $sourceFile)) | Measure-Object).Count
        if ($lineCount -gt $MaximumLines) {
            [pscustomobject]@{
                File = $sourceFile
                Lines = $lineCount
            }
        }
    }

    if ($violations) {
        $violations | Format-Table -AutoSize | Out-String | Write-Error
        throw "Source files must contain no more than $MaximumLines lines."
    }

    Write-Host "Checked $($sourceFiles.Count) source files; all are within the line limit."
}
finally {
    Pop-Location
}
