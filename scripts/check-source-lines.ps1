[CmdletBinding()]
param(
    [int]$MaximumLines = 499
)

$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot

try {
    $sourceFiles = @(git ls-files -- "*.cs" "*.axaml")
    if ($LASTEXITCODE -ne 0) {
        throw "Could not enumerate tracked source files."
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
