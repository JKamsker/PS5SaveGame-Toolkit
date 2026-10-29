# Contributing

Read [AGENTS.md](AGENTS.md) for repository scope and coding rules. Start with the
[user guide](docs/USER_GUIDE.md) for app behavior, the [API reference](docs/API.md)
for HTTP contracts, and the [plugin guide](docs/PLUGIN_GUIDE.md) before plugin work.

## Build and test

Use PowerShell 7, the .NET 10 SDK selected by [global.json](global.json), Node.js 24
and Python 3.10+ for documentation checks. From the repository root:

```powershell
$scratch = 'D:/Temp/saveeditor/test-scratch/contributing'
New-Item -ItemType Directory -Force $scratch | Out-Null
$env:SAVETOOLKIT_TEST_TEMP = "$scratch/tests"
$env:TEMP = $env:TMP = $env:TMPDIR = $scratch
$env:npm_config_cache = "$scratch/npm-cache"
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj --configuration Release --warnaserror
./scripts/test.ps1 -Configuration Release -ResultsDirectory "$scratch/results"
# Optional focused run: ./scripts/test.ps1 -Filter Pragmata
Push-Location web
npm ci
npm run lint --if-present
npm run typecheck
npm test
npm run build
Pop-Location
python -B docs/tests/test_documentation.py
```

Tests use Microsoft.Testing.Platform: direct invocations require
`dotnet test --project <test-project>`. Require zero warnings and failures.
For local development, see [Run from source](README.md#run-from-source).

After the Release API and UI builds above, run the synthetic browser smoke tests:

```powershell
$env:SAVETOOLKIT_E2E_SCRATCH = "$scratch/e2e"
$env:PLAYWRIGHT_BROWSERS_PATH = "$scratch/browsers"
Push-Location web
npx playwright install chromium
npm run e2e
Pop-Location
```

Playwright starts the built API automatically. See [e2e details](docs/RELEASING.md#built-app-browser-smoke-tests)
and [CI, coverage and packaging](docs/RELEASING.md) for prerequisites and reports.
The optional real-save browser regression skips without the external Rebirth sample; the
synthetic browser suite needs no private saves.

Environment variables:

- `SAVETOOLKIT_ARTIFACTS`: optional external private sample directory; never the checkout.
- `SAVETOOLKIT_TEST_TEMP`: backend test scratch root, including sample imports.
- `SAVETOOLKIT_E2E_SCRATCH`: browser test scratch, results and HTML report root.
- `SAVETOOLKIT_E2E_URL` and `SAVETOOLKIT_E2E_RUN_DIRECTORY`: managed by the Playwright
  config to share a run's free loopback URL and temporary directory with workers; leave unset.

## Developer tooling at a glance

Run from the repository root after configuring [disk and scratch](docs/RELEASING.md#disk-and-scratch).
The linked pages own each tool's options, prerequisites and review procedure.

| Tool | Purpose / authoritative instructions |
| --- | --- |
| [`scripts/test.ps1`](scripts/test.ps1) | API build and backend test projects; [parameters and coverage](docs/RELEASING.md#script-parameters). |
| [`scripts/self-test.ps1`](scripts/self-test.ps1) | Script/scaffold regressions; [parameters](docs/RELEASING.md#script-parameters). |
| [`scripts/bench.ps1`](scripts/bench.ps1) | Opt-in synthetic timing/allocation baselines; [benchmark guide](docs/PERFORMANCE.md). |
| [`scripts/release-notes.ps1`](scripts/release-notes.ps1) | Draft notes from merged work; [curation workflow](docs/RELEASING.md#prepare-curated-notes-and-check-the-candidate). |
| [`scripts/release-check.ps1`](scripts/release-check.ps1) | Read-only pre-tag readiness checks; [candidate checks](docs/RELEASING.md#prepare-curated-notes-and-check-the-candidate). |
| [`dotnet-stryker`](.config/dotnet-tools.json) | Opt-in Core/SDK mutation testing through `tests/Invoke-Mutations.ps1`; [runner and results](tests/README.md). |
| OpenAPI re-pin | Review and update the generated HTTP contract; [snapshot workflow](docs/API.md#openapi-contract-and-regression-guard). |
| Embedded-pack re-pin | Review each pack's content baseline; [snapshot workflow](docs/PLUGIN_GUIDE.md#embedded-pack-snapshot-baseline). |

## Frontend component map

The view renderer delegates state and presentation to focused components/hooks under `web/src`:

| Area | Implementation home |
| --- | --- |
| Eager/server tables, query controllers, cells and selection | [`components/document/table/`](web/src/components/document/table) |
| F6 preview and semantic changes, revision-aware Jump and revert | [`components/document/review/`](web/src/components/document/review) |
| Reference labels, search and picker lifecycle | [`components/fields/reference/`](web/src/components/fields/reference) |
| Direct action parameters and dialog | [`ActionParameterFields.tsx`](web/src/components/document/ActionParameterFields.tsx), [`DirectActionDialog.tsx`](web/src/components/document/DirectActionDialog.tsx), [`useActionDialog.ts`](web/src/components/ui/useActionDialog.ts) |
| Export dialog and request lifecycle | [`ExportDialog.tsx`](web/src/components/workspace/ExportDialog.tsx), [`useExportDialog.ts`](web/src/components/workspace/useExportDialog.ts) |

Keep request cancellation, document/revision isolation and focus restoration with their owning hooks.
Preserve native mixed checkbox state, persistent live announcements, visible labels in accessible
names, dialog/picker keyboard behavior and focus after clearing selection or reverting a change.
Colocated Testing Library tests and the [built-app browser regressions](docs/RELEASING.md#built-app-browser-smoke-tests)
exercise these behaviors. For plugin-facing selection declarations, use the
[selection binding contract](docs/PLUGIN_GUIDE.md#selection-table-binding-and-advisory-conformance-40).

## Samples and evidence

Never commit save files, decrypted dumps or extracted sample data; keep them outside the
checkout. See [samples and capture protocol](docs/SAMPLES.md). Sample-dependent tests
must skip cleanly when samples are absent; synthetic tests must still run. To reproduce
CI, unset `SAVETOOLKIT_ARTIFACTS` and ensure no ancestor `artifacts/` folder is discoverable.
For a sample-backed run, set it in the same shell and report skipped counts per project;
a passing run with skips is not full sample validation. Do not upload save files publicly;
describe the folder/file layout and redact personal paths and account identifiers from logs.
The [cross-plugin detection matrix](docs/PLUGIN_GUIDE.md#tests) exercises all registered
plugins against shared synthetic fixtures and every available external sample file.

## Scope, commits and review

Work on your assigned/topic branch, never directly on `main`. Commit on your branch only;
agents must never push or merge. Maintainers handle integration and releases.
Use conventional commits, e.g. `docs(contributing): add contributor guidance (#40)` or
`feat(pragmata): describe the change (#issue)`, with a body explaining what changed and why.
See [release instructions](docs/RELEASING.md) before any version/tag work.

Keep changes focused and include validation commands/results in the pull request.
Reviews, findings and follow-up work are tracked on the relevant plugin issue and the
[review programme (#40)](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40);
code review discussions belong on the corresponding pull request.

## Plugin contributions and SDK requests

- Stay inside `plugins/<Name>/`; do not edit framework, web, tests or other plugins.
  The scope exception is a `<PackageVersion>` entry in `Directory.Packages.props`.
- Research existing tools and format documentation before implementing; cite every source
  in the plugin README and verify claims against sample bytes. Never execute untrusted code blindly.
- Never lose data: preserve unknown bytes and prove byte-identical no-op round trips.
  Fix integrity, keep views bounded, and keep plugins stateless with no file/network I/O.
- Use honest `Verified` / `Inferred` / `Unknown` confidence labels. Format checks do not
  establish console acceptance; follow the [hard rules](docs/PLUGIN_GUIDE.md#hard-rules).
- Follow **rule 9** there for licence policy, owner reuse decisions, attribution, notices
  and share-alike terms, including the remaining GPL-2.0-only code and NC/no-redistribution
  refusals. Record sources in sidecars, the README, plugin references and UI provenance.
  Coordinate central notices and the [licence register](docs/LICENCE_EXCLUSIONS.md) with
  a maintainer when those updates fall outside plugin scope.
- Run conformance and format-specific tests, including corruption rejection and absent-sample skips.

For a missing SDK capability, first check [SDK request triage](docs/SDK_REQUESTS.md).
Describe the concrete use case, current workaround, evidence and proposed additive contract
under `## SDK requests` in your plugin README; link the relevant issue/#40 and existing
SDK request ID where applicable. Ask for framework work separately instead of changing `src/`.
