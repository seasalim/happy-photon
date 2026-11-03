$script:Families = @{
    '.cs' = 'csharp'
    '.ps1' = 'powershell'
    '.psm1' = 'powershell'
    '.psd1' = 'powershell'
    '.js' = 'prettier'
    '.mjs' = 'prettier'
    '.ts' = 'prettier'
    '.css' = 'prettier'
    '.json' = 'prettier'
}

function Get-FormatFamily {
    param([string]$Path)

    return $script:Families[[IO.Path]::GetExtension($Path).ToLowerInvariant()]
}

# A pattern containing '/' matches the repository-relative path; any other pattern matches the file name.
function Test-FormatExcluded {
    param([string]$Path, [string[]]$Patterns)

    $name = ($Path -split '/')[-1]

    foreach ($pattern in $Patterns) {
        $subject = $name

        if ($pattern.Contains('/')) {
            $subject = $Path
        }

        if ($subject -clike $pattern) {
            return $true
        }
    }

    return $false
}

function Get-RepositoryFiles {
    param([string]$RepoRoot, [string]$Scratch)

    $arguments = @('-c', 'core.quotepath=false', 'ls-files', '-z', '--cached', '--others', '--exclude-standard')
    $listing = Invoke-FormatProcess -Program git -Arguments $arguments -WorkingDirectory $RepoRoot `
        -Environment (Get-ChildEnvironment $Scratch) -TimeoutSeconds 120 -OutputLimit 67108864

    if ($listing.Truncated) {
        throw 'SELECTION: the repository file listing exceeded 64 MB'
    }

    $files = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

    foreach ($path in $listing.Output.Split([char]0)) {
        if ($path) {
            [void]$files.Add($path)
        }
    }

    return , $files
}

function Assert-NoLink {
    param([string]$RepoRoot, [string]$Path)

    # A symlink or junction could send a write outside the repository.
    $current = $RepoRoot

    foreach ($segment in $Path.Split('/')) {
        $current = Join-Path $current $segment
        $item = Get-Item -LiteralPath $current -Force

        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "SELECTION: $Path passes through a link at '$segment'"
        }
    }
}

# The root may legitimately be reached through links (a mounted volume, a linked checkout). Every link on
# every component is followed to the physical folder, and every link met on the way is recorded with its
# target, so a link added, removed or retargeted anywhere on that chain during a run changes the result.
# Only a link to a volume's root is a mount point: it names a volume rather than redirecting, so it stays.
function Get-RootLinkState {
    param([string]$RepoRoot)

    $separator = [IO.Path]::DirectorySeparatorChar
    $full = [IO.Path]::GetFullPath($RepoRoot)
    $current = [IO.Path]::GetPathRoot($full)
    $pending = [Collections.Generic.Queue[string]]::new([string[]]$full.Substring($current.Length).Split($separator, [StringSplitOptions]::RemoveEmptyEntries))
    $links = [Collections.Generic.List[string]]::new()

    while ($pending.Count) {
        $current = [IO.Path]::Combine($current, $pending.Dequeue())
        $target = (Get-Item -LiteralPath $current -Force).LinkTarget

        if (-not $target) {
            continue
        }

        $links.Add("$current -> $target")

        if ($target -match '^(\\\\\?\\)?Volume\{[0-9A-Fa-f-]+\}\\?$') {
            continue
        }

        if ($links.Count -gt 40) {
            throw "SELECTION: too many links on the way to $RepoRoot"
        }

        # Windows reports a junction into a volume by its GUID path; follow it like any other target.
        if ($target -match '^Volume\{') {
            $target = '\\?\' + $target
        }

        # Restart from the link's target so that links among the target's own parents are followed too.
        $resolved = [IO.Path]::GetFullPath($target, [IO.Path]::GetDirectoryName($current))
        $rest = @($resolved.Substring([IO.Path]::GetPathRoot($resolved).Length).Split($separator, [StringSplitOptions]::RemoveEmptyEntries)) + @($pending)
        $current = [IO.Path]::GetPathRoot($resolved)
        $pending = [Collections.Generic.Queue[string]]::new([string[]]$rest)
    }

    return (@($current) + $links) -join "`n"
}

# Every listed file under an adopted prefix with a qualified formatter, outside the exclusions. A prefix that
# selects nothing is refused, so a renamed folder or a typo cannot silently drop enforcement.
function Get-AdoptedPaths {
    param($Scope, [Collections.Generic.HashSet[string]]$Listed, [string]$RepoRoot)

    $prefixes = @($Scope.PSObject.Properties['adopted'] | ForEach-Object { $_.Value })

    if (-not $prefixes.Count) {
        throw 'SELECTION: config/scope.json adopts no path prefix'
    }

    $sorted = [string[]]@($Listed)
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    $paths = [Collections.Generic.List[string]]::new()

    foreach ($prefix in $prefixes) {
        $segments = $prefix.TrimEnd('/').Split('/')

        if (-not $prefix.EndsWith('/') -or $prefix.Contains('\') -or @($segments | Where-Object { $_ -in @('', '.', '..') }).Count) {
            throw "SELECTION: adopted prefix '$prefix' must be a plain repository-relative folder ending in '/'"
        }

        $matched = @($sorted | Where-Object {
                $_.StartsWith($prefix, [StringComparison]::Ordinal) -and (Get-FormatFamily $_) -and
                -not (Test-FormatExcluded $_ $Scope.excluded) -and (Test-Path -LiteralPath (Join-Path $RepoRoot $_) -PathType Leaf)
            })

        if (-not $matched.Count) {
            throw "SELECTION: adopted prefix '$prefix' selects no file"
        }

        $paths.AddRange([string[]]@($matched | Where-Object { -not $paths.Contains($_) }))
    }

    return , $paths.ToArray()
}

function Resolve-FormatSelection {
    param([string]$RepoRoot, [string[]]$Path, [string]$Scratch, [switch]$Adopted)

    $named = @($Path | Where-Object { $_ }).Count

    if ($Adopted -and $named) {
        throw 'SELECTION: -Adopted selects the adopted scope; do not also name files'
    }

    if (-not $Adopted -and -not $named) {
        throw 'SELECTION: name at least one repository-relative file with -Path, or use -Adopted'
    }

    $scope = Get-Content -LiteralPath (Join-Path $script:FormatHome 'config/scope.json') -Raw | ConvertFrom-Json
    $listed = Get-RepositoryFiles $RepoRoot $Scratch
    $rootLinks = Get-RootLinkState $RepoRoot

    if ($Adopted) {
        $Path = Get-AdoptedPaths $scope $listed $RepoRoot
    }

    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

    foreach ($name in $Path) {
        $relative = $name.Replace('\', '/')
        $segments = $relative.Split('/')

        if ([IO.Path]::IsPathRooted($name) -or $relative.StartsWith('/') -or
            @($segments | Where-Object { $_ -in @('', '.', '..') }).Count) {
            throw "SELECTION: $name must be a plain repository-relative file path"
        }

        if (-not $listed.Contains($relative)) {
            throw "SELECTION: $name is not a tracked or unignored repository file (paths are case-sensitive)"
        }

        $family = Get-FormatFamily $relative

        if (-not $family) {
            throw "SELECTION: $name has no qualified formatter"
        }

        if (Test-FormatExcluded $relative $scope.excluded) {
            throw "SELECTION: $name is excluded (data, fixtures, lockfiles, vendor or generated files)"
        }

        if (-not $seen.Add($relative)) {
            throw "SELECTION: $name is named twice"
        }

        $file = Join-Path $RepoRoot $relative

        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "SELECTION: $name does not exist"
        }

        Assert-NoLink $RepoRoot $relative
        [pscustomobject]@{ Root = $RepoRoot; RootLinks = $rootLinks; Path = $relative; File = $file; Family = $family }
    }
}
