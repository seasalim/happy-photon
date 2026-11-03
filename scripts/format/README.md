# Formatter commands

Pinned formatters for explicitly named files: C# (`dotnet format whitespace`), PowerShell
(PSScriptAnalyzer `Invoke-Formatter`) and Prettier (JS, MJS, TS, CSS, JSON). PowerShell 7.4 or later:

```powershell
pwsh scripts/format/install-format-tools.ps1          # the only download step
pwsh scripts/format/check-format.ps1 <files...>       # read-only
pwsh scripts/format/apply-format.ps1 <files...>       # rewrites files whose only finding is DRIFT
pwsh scripts/format/qualify-format.ps1 [-EvidencePath <file>] [-DriftReport <file>] [-CompareWith <file>]
```

Check and apply exit 0 when every named file is clean, 1 when findings remain and 2 when the run was
refused or a tool failed; exit 2 reports no verdicts and writes nothing. A parser diagnostic is a `PARSE`
finding; any other formatter failure is exit 2. Qualification exits 0 when every acceptance property held,
1 otherwise and 3 when an entrypoint is missing.

## Pins

`config/versions.json` pins the .NET SDK, PSScriptAnalyzer (package and manifest SHA-256) and Prettier
(also locked by `config/package-lock.json`). Install restores PSScriptAnalyzer and Prettier into the
ignored `.tools/format` (`-ToolRoot` overrides) and checks that the pinned SDK is installed; it never
installs an SDK. Check and apply validate every pin and download nothing. The SDK is selected by a
`global.json` in a scratch folder with roll-forward disabled, so application builds keep their own SDK.
`config/csharp.editorconfig` is copied there as `.editorconfig`; the repository root has none.

## Selection and bytes

Paths are repository-relative and case-sensitive, and must be tracked or unignored files with a supported
extension, outside `config/scope.json` exclusions (data, fixtures, lockfiles, vendor and generated files)
and not reached through a link. One refused path refuses the run. Formatting runs on copies in the
platform temp directory. Each file keeps its newline style and BOM; mixed or bare-CR endings are reported,
never fixed. Apply settles every verdict first, then replaces each file atomically, only if it is
unchanged since it was read and still reached through no link.

## Findings

| Finding | Meaning |
|---|---|
| `DRIFT` | The bytes differ from the formatter's output. The only finding apply fixes. |
| `PARSE` | The input does not parse. |
| `SEMANTICS` | The output changed a token, literal value, statement or JSON number text. |
| `SIZE` | Over 499 lines after formatting (JSON exempt). |
| `LINE-ENDINGS`, `ENCODING` | Mixed or bare-CR endings; not UTF-8. |
| `CHANGED`, `WRITE` | Apply found the file modified meanwhile, or could not replace it (including a link that appeared after selection). |

## Limits

HTML is not supported: Prettier reflows HTML text, and whether that changes what renders depends on CSS and
scripts the formatter cannot see. Formatters do not enforce AGENTS.md blank-line rules, and PSScriptAnalyzer
leaves `}else` and hashtable brace spacing as written. Review those by hand, as for HTML, AXAML and other
unsupported languages.
PSUseCorrectCasing is off because its output depends on installed commands.
