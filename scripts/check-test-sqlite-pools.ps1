[CmdletBinding()]
param(
    [string[]] $IncludePatterns = @(
        ":(glob)Tests/**/*.cs",
        ":(glob)HeadlessTests/**/*.cs")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$clearAllPoolsPattern = [regex]::new('\bSqliteConnection\.ClearAllPools\s*\(')

Push-Location $projectRoot
try {
    $sourceFiles = @(git ls-files --cached --others --exclude-standard -- $IncludePatterns)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not enumerate test source files."
    }

    $violations = foreach ($sourceFile in $sourceFiles) {
        $path = Join-Path $projectRoot $sourceFile
        $lines = [System.IO.File]::ReadAllLines($path)
        for ($index = 0; $index -lt $lines.Count; $index++) {
            if ($clearAllPoolsPattern.IsMatch($lines[$index])) {
                [pscustomobject]@{
                    File = $sourceFile
                    Line = $index + 1
                }
            }
        }
    }

    if ($violations) {
        $detail = $violations | Format-Table -AutoSize | Out-String
        throw "Process-wide SQLite pool clearing is unsafe in parallel tests. " +
            "Dispose the owning CatalogService or clear only its connection pool.`n$detail"
    }

    Write-Host "Checked $($sourceFiles.Count) test files; SQLite cleanup is pool-scoped."
}
finally {
    Pop-Location
}
