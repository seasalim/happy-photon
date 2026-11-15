param(
    [Parameter(Mandatory)][string] $FilePath,
    [Parameter(Mandatory)][string[]] $Arguments,
    [int] $TimeoutSeconds = 120
)
$process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -NoNewWindow -PassThru
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    & taskkill /PID $process.Id /T /F
    throw "Command exceeded $TimeoutSeconds seconds."
}
exit $process.ExitCode
