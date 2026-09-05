# Quarantined tests

Quarantine keeps a demonstrated flaky test visible without letting it block
ordinary development or a release. It is temporary test governance, not an
acceptance of nondeterministic behavior.

## Entry gate

A test may enter quarantine only after it fails twice on the same source while
immediate retries pass. Create a repair issue, add the `Category=Quarantined`
trait, and register the fully qualified test name in
`Tests/quarantined-tests.json`. Every entry records an owner, reason,
introduction date, issue, and expiry no more than 90 days later.

The required CI validation compares the registry with one in-process xUnit
`-list full/json` listing per prebuilt test project. The checker never builds;
run `dotnet build HappyPhoton.sln --configuration Release` first when using it
standalone. It writes names and traits to `<ResultsDirectory>/manifests/`
(default `artifacts/test-results/discovery/manifests/`). `-ManifestDirectory`
reuses explicit manifests instead of launching discovery. Counts include every
namespace and count listed cases before theory expansion; the minima are 1300
for unit tests and 340 for headless tests. Missing or unregistered traits,
duplicate entries, invalid metadata and expired entries fail validation.

`verify.ps1` builds once unless `-NoBuild`, then runs tests with `--no-build
--no-restore`. It always creates an isolated run directory beneath
`-ResultsDirectory` (default `artifacts/test-results/`) containing manifests
and TRX, and reports build, policy+discovery and tests+reconciliation timings.
The checker's `-Mode StableResults` reconciles those manifests with TRX: no
registered quarantined test may execute, and every stable manifest case must
appear as Passed, Failed or NotExecuted, matching display names or theory
prefixes. Both StableResults and nightly Results mode require exactly one TRX
per governed project from the current run; Results mode additionally requires
every registered quarantined test to have executed. Callers must supply an
isolated current-run directory, never a directory of accumulated results.
`./scripts/check-test-quarantine-fixtures.ps1` exercises these checks without
a build.

## Execution lanes

`HappyPhoton.runsettings` excludes quarantined tests from ordinary local runs,
three-platform CI, and release qualification, and `HappyPhoton.FullCpu.runsettings`
keeps the same exclusion for `HAPPY_PHOTON_FULL_CPU=1` runs. This keeps those
gates strict for every non-quarantined test.

The `Quarantined tests` workflow runs nightly and on manual dispatch. It uses
`HappyPhoton.AllTests.runsettings` to execute the entire Windows suite, keeping
the load and ordering that can expose a race. Registered flaky failures are
reported as warnings in the job summary and retained TRX artifact. Harness
errors, missing quarantined results, expired entries, and failures outside the
registry still fail the workflow.

To reproduce the observation workload locally:

```powershell
dotnet test HappyPhoton.sln --configuration Release `
  --settings HappyPhoton.AllTests.runsettings
```

## Exit gate

The repair issue owns removal. After the fix, keep the test in the observation
lane until it passes 20 full-suite-load repetitions and three consecutive
scheduled runs. Then remove its trait and registry entry together. If the
expiry arrives first, required CI fails until the test is fixed or the user
explicitly approves a new, justified quarantine window.
