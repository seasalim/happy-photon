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

The required CI validation compares the registry with xUnit discovery. It
fails for missing traits, unregistered traits, duplicate entries, invalid
metadata, expired entries, or any test lost between the stable and quarantine
sets.

## Execution lanes

`HappyPhoton.runsettings` excludes quarantined tests from ordinary local runs,
three-platform CI, and release qualification. This keeps those gates strict for
every non-quarantined test.

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
