# Only a positive execution request proves host CPU. Discovery/info/unknown stay in the upper bound.
function Get-TestHostInvocationEvidence([string[]] $Arguments) {
    if ($Arguments -ccontains '-list' -or $Arguments -ccontains '-assemblyInfo' -or
        $Arguments -cnotcontains '-automated') {
        return ''
    }

    for ($index = 0; $index -lt $Arguments.Count - 1; $index++) {
        if ($Arguments[$index] -ceq '-run' -and $Arguments[$index + 1].StartsWith('-3:', [StringComparison]::Ordinal)) {
            return 'xunit-v3-automated-run'
        }
    }

    return ''
}

function Get-TestHostExecutionEvidence($Process) {
    try {
        $invocation = Get-CimInstance Win32_Process -Filter "ProcessId = $($Process.Id)" -ErrorAction Stop
        if ($Process.HasExited -or $null -eq $invocation -or -not $invocation.CommandLine) { return '' }

        $arguments = @([TestProcessJob]::SplitCommandLine($invocation.CommandLine) | Select-Object -Skip 1)

        # VSTest's xUnit adapter writes one argument per line; the file lasts only as long as its child.
        if ($arguments.Count -eq 2 -and $arguments[0] -ceq '@@') {
            $arguments = [IO.File]::ReadAllLines($arguments[1])
        }

        return Get-TestHostInvocationEvidence $arguments
    } catch {
        # A missed response file or inaccessible invocation is uncertain, never proven test CPU.
        return ''
    }
}
