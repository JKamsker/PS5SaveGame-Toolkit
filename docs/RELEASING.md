# Versions, packages and releases

The application version comes from **git tags**, through [MinVer](https://github.com/adamralph/minver).
Use `vMAJOR.MINOR.PATCH` for stable releases and e.g. `v1.2.3-rc.1` for prereleases. Release
inputs reject leading zeroes, a `v` prefix and build metadata. Plugin `PluginInfo.Version` values
remain independent; only the API assembly uses MinVer. The web package.json version is npm
metadata, not the application version.

MinVer is a small build-only dependency with no version file to keep synchronized with tags.
On an exact tag it uses that SemVer; after a stable tag it increments the patch and appends
`alpha.0` plus commit height. Before the first tag it uses `0.0.0-alpha.0.<height>`.
The informational version can also contain the source commit as `+` metadata. `GET /api/version`
returns `{ "version": "..." }`; the home-page footer displays this running-server version. Always fetch
full history and tags (`git fetch --tags`; Actions uses `fetch-depth: 0`). Shallow clones cannot
reliably calculate a release version. Dirty source changes do not automatically change the version.

## Continuous integration

`.github/workflows/ci.yml` validates pushes, pull requests and release candidates on Ubuntu and
Windows. It builds with warnings as errors and runs **every** test project through `scripts/test.ps1`.
Dependency-free PowerShell script regressions also run on Ubuntu, Windows and macOS. Run them
locally with `./scripts/self-test.ps1 -ScratchDirectory <path>`. Only superseded pull-request
validations cancel running CI; pushes (including main) and release validations finish their current run.
Private samples are absent, so sample-dependent tests skip while synthetic tests still run.
There is no sample download step. This validates the sample-free path, not gameplay or console
compatibility. Web checks include ESLint, TypeScript, Vitest and the production build.
NuGet/npm downloads are cached; per-project TRX and web JUnit results are uploaded even on failure,
and the validation jobs write summaries.

The API tests include the generated OpenAPI snapshot and hosting/schema regression
checks in `tests/SaveToolkit.Api.Performance.Tests`. Contract drift fails normal CI;
re-pin only after reviewing an intentional API metadata/contract change using the
[one-line workflow](API.md#openapi-contract-and-regression-guard). Documentation tests
compare the checked-in contract's method/route inventory with `docs/API.md` in both
directions. `/openapi/v1.json` is also available in packaged Production runs.

Performance measurements are opt-in and documented in [PERFORMANCE.md](PERFORMANCE.md).
`./scripts/bench.ps1` runs synthetic BenchmarkDotNet workloads and retains a Markdown summary;
the benchmark executable is outside `*.Tests.csproj` discovery and does not run in CI Validate,
`scripts/test.ps1`, or `dotnet test`.

The web package's `lint` script runs `eslint src e2e`, using `web/eslint.config.js` (flat config).
The `validate` job invokes `npm run lint --if-present` on both Ubuntu and Windows immediately
after installing web dependencies, before typecheck, Vitest and the production build; lint errors
fail that step. The script is present, so `--if-present` does not skip linting. Recommended ESLint,
TypeScript, React Hooks and JSX accessibility rules cover unused variables, type imports, duplicate
imports, hooks, floating/misused promises, console calls and debugger statements. File size over
400 lines and functions over 50 lines are warnings, including JSX and test bodies; warnings do not
fail CI. Existing dependency/virtualizer warnings remain visible for controller refactoring.

Narrow exceptions in the config preserve existing request lifetimes, render snapshots and effect
timing; line-level exceptions preserve keyboard scrolling, combobox active-descendant navigation,
validation metadata and intentional test diagnostics. Every exception includes a reason. No broad
disable directives are used, and unused disable directives are errors.

Typed linting uses the TypeScript 6 API through the `typescript` npm alias; the `@typescript/native`
alias retains TypeScript 7's `tsc` for typecheck and builds, following Microsoft's
[side-by-side guidance](https://devblogs.microsoft.com/typescript/announcing-typescript-7-0/#running-side-by-side-with-typescript-6.0).
The targeted `eslint-plugin-jsx-a11y` peer override permits ESLint 10 while that plugin's published
peer range ends at ESLint 9. Keep these compatibility choices under review when upgrading the tools;
plain `npm ci` must succeed without legacy peer-dependency flags.

The job IDs below are the workflow keys, rather than matrix-expanded display names:

| Workflow | Job ID | Work |
| --- | --- | --- |
| [ci.yml](../.github/workflows/ci.yml) | `validate` | Ubuntu/Windows backend and web checks; .NET coverage on Windows, web coverage and documentation/actionlint checks on Linux. |
| [ci.yml](../.github/workflows/ci.yml) | `script-tests` | PowerShell script/scaffold regressions on Ubuntu, Windows and macOS. |
| [ci.yml](../.github/workflows/ci.yml) | `e2e` | Ubuntu Chromium tests against the built API/UI. |
| [nightly-fuzz.yml](../.github/workflows/nightly-fuzz.yml) | `fuzz` | Ubuntu scheduled/manual mutation sweep with synthetic seeds, reproducible seed/count overrides and metadata-only failure reports. |
| [release.yml](../.github/workflows/release.yml) | `resolve` | Validate the requested tag/version and pin the source commit. |
| [release.yml](../.github/workflows/release.yml) | `validate` | Call the complete reusable CI workflow, including script tests and e2e. |
| [release.yml](../.github/workflows/release.yml) | `package` | Package and smoke-test all four RIDs on matching runners. |
| [release.yml](../.github/workflows/release.yml) | `publish` | Verify checksums, upload a draft and publish it. |

The inherited conformance `MutationFuzz` fact runs in ordinary backend test projects and the
[nightly mutation sweep](#nightly-mutation-fuzz). It skips when neither private samples nor
synthetic seeds are available. See the
[mutation harness contract](PLUGIN_GUIDE.md#deterministic-mutation-fuzzing-40).

## Script parameters

These are the repository scripts' declared parameters (PowerShell common parameters, where enabled,
are additional). All commands below run from the repository root in PowerShell 7.

| Script | Declared parameters | Defaults / requirements |
| --- | --- | --- |
| `test.ps1` | `-Filter`, `-Configuration`, `-ResultsDirectory`, `-Coverage` | Always builds the API first; empty filter runs all backend test projects. Configuration defaults to Debug; only Debug/Release are accepted. Results are optional unless coverage is requested. |
| `bench.ps1` | `-ScratchDirectory`, `-Job`, `-Filter` | Opt-in synthetic benchmarks, Release only. Job defaults to Quick (ShortRun); Full uses the default BenchmarkDotNet job. Filter defaults to `*`. Scratch defaults to `SAVETOOLKIT_TEST_TEMP`, then a system-temp subdirectory; retains a unique run directory with reports and `summary.md`. |
| `self-test.ps1` | `-ScratchDirectory` | Defaults to system temp. Runs script and real scaffold build/test checks; requires the selected .NET SDK and restore access, no Pester or private saves. |
| `package.ps1` | `-Runtime`, `-OutputDirectory`, `-SkipWebBuild` | Output directory is required. Runtime is an array defaulting to all four supported RIDs; web build is included by default. |
| `smoke-package.ps1` | `-PackageDirectory`, `-Runtime`, `-ScratchDirectory`, `-ExpectedVersion` | First three are required; exactly one matching archive plus its `.sha256` must exist. Expected version is optional. |
| `new-plugin.ps1` | `-Name`, `-Id`, `-TitleIds`, `-GameName` | Name/ID are required; title IDs default to empty, game name to Name. See the [scaffold walkthrough](PLUGIN_GUIDE.md#scaffold) for format and name restrictions. |
| `release-notes.ps1` | `-Version`, `-PreviousTag`, `-Force` | Version is required (SemVer without `v` or build metadata). Previous tag defaults to the nearest reachable `v*` tag on the first-parent history. Writes a draft; existing notes require Force. No network. |
| `release-check.ps1` | `-Version` | Version is required in the same format. Read-only pre-tag checks require committed notes and a clean main branch; queries exact-HEAD main push CI through gh, or warns when gh is unavailable. |

## Disk and scratch

Keep private saves outside the checkout, and keep temporary extraction/build data separate from
those originals. On the owner's Windows workstation, use a task-specific directory under
`D:/Temp/saveeditor/test-scratch/`; this documentation pass uses
`D:/Temp/saveeditor/test-scratch/framework-docs`. On other machines choose a writable equivalent;
hosted CI uses `runner.temp`. Create it before redirecting environment variables.

Set `SAVETOOLKIT_TEST_TEMP` for `TestScratch`/sample imports and `TEMP`, `TMP`, `TMPDIR` for other
tools. The runner restores those three temp variables after each project; an ordinary direct
`dotnet test` needs the environment set too. Direct source runs also put workspaces under the
system temp directory unless `Toolkit:DataRoot` is configured. Bundle launchers use `./data`;
see [user storage guidance](USER_GUIDE.md#where-your-data-lives).

Allow space for expanded samples, concurrent workers, coverage reports, npm cache, package staging
and extracted smoke bundles; compressed archive size is not a sufficient disk estimate. Automatic
cleanup covers invocation-owned test/staging/extraction directories, not retained reports, caches,
package archives or smoke logs. Inspect warnings and clean up known task-owned leftovers after
review. Never recursively delete a sample directory or another task's scratch.

Local equivalent (PowerShell 7, .NET SDK from global.json, Node.js 24):

```powershell
Remove-Item Env:SAVETOOLKIT_ARTIFACTS -ErrorAction SilentlyContinue
$scratch = 'D:/Temp/saveeditor/test-scratch/framework-docs'
New-Item -ItemType Directory -Force $scratch | Out-Null
$env:SAVETOOLKIT_TEST_TEMP = "$scratch/tests"
$env:TEMP = $env:TMP = $env:TMPDIR = $scratch
$env:npm_config_cache = "$scratch/npm-cache"
./scripts/self-test.ps1 -ScratchDirectory $scratch
python -B docs/tests/test_documentation.py
./scripts/test.ps1 -Configuration Release -ResultsDirectory "$scratch/results"
Push-Location web
npm ci --no-audit --no-fund
npm run lint --if-present
npm run typecheck
npm test
npm run build
Pop-Location
```

Keep the checkout outside a sample tree when reproducing CI: SampleSaves can also find an
`artifacts/` directory by walking up from the test binary. Unsetting the variable alone does not
hide such a directory. Workflow syntax is checked with [actionlint](https://github.com/rhysd/actionlint)
on Linux; run `actionlint` locally after workflow edits too.

## Nightly mutation fuzz

`.github/workflows/nightly-fuzz.yml` runs daily at **03:23 UTC**, or manually through
`workflow_dispatch` with optional `seed` and `mutations` inputs. It runs only plugin test projects,
using the MTP/xUnit `--filter-method '*MutationFuzz'` filter, on Ubuntu with coverage disabled.
The default nightly seed is the UTC run date as a decimal `YYYYMMDD` integer. The summary records
both its decimal and hexadecimal forms, mutation count, and actual per-plugin seed/case coverage.
The nightly count is **5,000 mutations per seed**. A **60-second per-plugin wall-clock cap**
(including worker startup/preparation) and a **30-minute job timeout** bound the run. Exceeding a
cap fails the test and kills the isolated worker; it never silently passes an incomplete run.

Nightly CI has **synthetic seeds only**, with no private sample download. All **17 plugin conformance
classes** now supply **28 synthetic seeds**: shared defaults provide GenericJson (one) and XorJson
(two), and the other **15 plugins** reuse authored test-fixture builders through
`MutationFuzzSyntheticSeeds`. Every baseline must probe, open and build its initial view; validity
facts also query collections. No private bytes or extracted save data are embedded.

| Plugin | Synthetic seeds | Parser paths / limits |
| --- | ---: | --- |
| AstroPlayroom | 1 | Exact 1 MiB authored memory.dat. |
| AtomicHeart | 1 | SHA-1 envelope, stored Oodle block and inventory properties. |
| BaldursGate3 | 2 | Plain LSF and zstd LSPK containing LZ4 LSF. |
| Cyberpunk2077 | 2 | Stored and LZ4-compressed CSAV node tables. |
| EldenRing | 1 | One character/menu in the required 0x1BA0080-byte allocation. |
| FinalFantasy16 | 1 | Combined known FAITH blocks with valid CRC. |
| FinalFantasy7 | 3 | Remake, Rebirth and demo common containers; no gameplay-body coverage. |
| Gearbox | 4 | BL3 and Wonderlands character/profile envelopes and protobufs. |
| GenericJson | 1 | UTF-8 JSON scalars and containers. |
| GodOfWarRagnarok | 2 | Nested wallets/resources, links, flags and ID/index words; variable gear framing. |
| Insomniac | 1 | Rift Apart currency binary fixture. |
| Pragmata | 1 | DSSS inventory; parser-valid integrity metadata, no authentic RSA signature. |
| SuckerPunch | 2 | Tsushima and Yotei inventory layouts and integrity. |
| TalesOfArise | 1 | Reproducible encrypted envelope and fixed-layout character data. |
| UnrealGvas | 1 | Hogwarts GVAS/SQLite discovery fixture. |
| Witcher3 | 2 | SAV3 scalars/archive and typed player identity, points and inventory. |
| XorJson | 2 | XOR-1 and byte-minus-one JSON. |

No plugin requires a rejection-only seed. A small accepted Elden Ring file is infeasible because
its probe requires the full fixed allocation. A local Windows preflight with seed `20261029` and
5,000 requested mutations exceeded the 60-second cap near index 1,852; this is a run-budget failure,
not evidence of a parser exception. Cyberpunk's two-seed run also exceeded that cap near the end
of its second seed (index 4,756). The workflow's cap is unchanged. All 17 plugins now have
sample-free inputs, but a complete green nightly run within that cap is not established.
FF7 uses small common files because `CoverageFixture` is sample-backed; its authored gameplay
fixtures require 9-23 MiB each and remain in focused tests. Pragmata's synthetic integrity proves
parser acceptance only, not authentic signing or console acceptance. See each plugin README's
fuzz/testing notes for provenance and limitations. Local runs additionally use selected real samples.
The workflow's introductory summary still mentions its original two built-in formats; its actual
per-plugin result table is authoritative. Updating workflow text is outside this test-only scope.

The sample-free Windows verification at seed `20261029` completed **135,000 mutations** across
the 27 seeds then authored (the second Ragnarök seed came later): **21,527 opens**, **113,473 rejections**, zero unexpected or quarantined failures.
An open means the complete probe/open/view/first-collection-pages/dispose lifecycle succeeded;
rejections include nonmatching probes and documented format exceptions. The two cap failures
were rerun with a 300-second local budget: Cyberpunk completed in 48.7 seconds and Elden Ring
in 107.7 seconds. The 17 completed runs totalled 369.8 seconds including build/runner overhead,
with 154.9 seconds of parser work. These local timings vary with host load and do not establish
hosted Ubuntu performance. Outcome counters were collected using scratch-only MSBuild
instrumentation; the committed harness and nightly cap are unchanged.

The harness accepts these optional environment overrides; **unset** variables retain existing
plugin defaults (normally seed `0x40C0FFEE`, 200 mutations per selected file, no overall plugin cap):

| Variable | Accepted value |
| --- | --- |
| `SAVETOOLKIT_FUZZ_SEED` | Unsigned 32-bit decimal or `0x`-prefixed hexadecimal integer, including zero |
| `SAVETOOLKIT_FUZZ_MUTATIONS` | Positive 32-bit integer, per synthetic or selected real seed file |
| `SAVETOOLKIT_FUZZ_TIME_BUDGET` | Optional positive 32-bit integer number of seconds per plugin |
| `SAVETOOLKIT_FUZZ_REPORT_DIRECTORY` | Optional directory for metadata-only JSON results/reproducers |

Empty, malformed, negative and out-of-range numeric values fail with a named configuration error.
Ordinary CI sets none of these overrides and remains deterministic. On failure, the nightly workflow
uploads only `*-failure.json` reports for 14 days: plugin, seed, mutation index/description,
sample identity/hash when applicable, exception type and stack frames. Reports exclude sample bytes,
exception messages and raw worker output. A process/build failure before a mutation starts has no
mutation index; its fallback report identifies the project and seed. Nothing from scratch or private
sample directories is uploaded. Known quarantined failures remain visible in result counts.

Reproduce a failure locally with the exact plugin project and seed from the report (PowerShell 7):

```powershell
$scratch = 'D:/Temp/saveeditor/test-scratch/nightly-fuzz'
New-Item -ItemType Directory -Force $scratch | Out-Null
$env:TEMP = $env:TMP = $env:TMPDIR = $scratch
$env:SAVETOOLKIT_TEST_TEMP = "$scratch/tests"
$env:SAVETOOLKIT_FUZZ_REPORT_DIRECTORY = "$scratch/repro-reports"
$env:SAVETOOLKIT_FUZZ_SEED = '0x013528A5' # replace with the reported seed (20261029 here)
$env:SAVETOOLKIT_FUZZ_MUTATIONS = '5000' # at least the reported zero-based index + 1
$env:SAVETOOLKIT_FUZZ_TIME_BUDGET = '300' # allow more time for local private-sample runs
dotnet test --project plugins/GenericJson/SaveToolkit.Plugins.GenericJson.Tests --configuration Release --filter-method '*MutationFuzz'
```

For nightly reproduction, leave private samples unavailable (including any ancestor `artifacts/`
fallback). For a sample-backed reproducer, set `SAVETOOLKIT_ARTIFACTS` to external samples on D:
and match the report's original SHA-256. Each mutation depends only on original bytes, seed and
zero-based index, so changing the mutation count does not change earlier cases. Per-case timeout
and allocation guards continue to apply independently of the optional overall cap. Remove the
overrides before returning to normal CI-equivalent validation.

## Code coverage

Windows validation collects .NET coverage through the Microsoft.Testing.Platform extension; Ubuntu
validation collects web coverage through Vitest V8. (On Linux the .NET coverage extension adds about
105 s of fixed startup per test process, which pushed the 21 projects past the job timeout.)
Coverage is informational; there are no minimum thresholds. The job summaries show .NET
line/branch coverage by assembly (Windows) and the web text summary (Ubuntu). Download
`coverage-Windows` for per-project and merged .NET Cobertura and HTML, and `coverage-Linux` for the
web Cobertura, JSON summary and HTML reports; both are retained for 14 days. Shared test
helpers and test assemblies are excluded from the merged .NET report; web coverage includes all
TypeScript source files, including untested files, excluding tests and declarations.

Collect and merge locally from the repository root (set scratch/TEMP as above):

```powershell
./scripts/test.ps1 -Coverage -Configuration Release -ResultsDirectory D:/Temp/saveeditor/test-scratch/ci-coverage/results
dotnet tool restore
dotnet tool run reportgenerator '-reports:D:/Temp/saveeditor/test-scratch/ci-coverage/results/**/coverage.cobertura.xml' '-targetdir:D:/Temp/saveeditor/test-scratch/ci-coverage/backend' '-reporttypes:Cobertura;MarkdownAssembliesSummary;Html' '-assemblyfilters:-*.Tests;-SaveToolkit.Testing'
Push-Location web
npm test -- --coverage --coverage.reportsDirectory=D:/Temp/saveeditor/test-scratch/ci-coverage/web
Pop-Location
```

`-Coverage` defaults to the repository's ignored `TestResults` directory when `-ResultsDirectory`
is omitted. Ordinary test runs remain unchanged. The coverage package is centrally referenced only
by projects named `*.Tests` in `Directory.Packages.props`; new test projects should follow that name.

## Built app browser smoke tests

The Ubuntu-only `e2e` CI job runs alongside validation with a 12-minute limit. Chromium imports
the committed synthetic fixtures through the bundled UI. The suite covers import errors and recovery,
raw read-only inspection, editing/review/undo, document isolation and redo, full-collection search/filter/
sort/paging, bulk preview/cancel/apply, keyboard-only application interaction, and repeated exports.
F6 actions cover selected-row and filtered-query scopes (including unloaded pages), empty table
selection versus document scope, cancel without writes, and exact apply/undo/redo values and exports.
Export assertions compare every path and decompressed payload byte; ZIP metadata is not compared.
No private samples or `SAVETOOLKIT_ARTIFACTS` are needed. Failures upload the Playwright HTML report
with traces and screenshots.

Unknown-layout export path preservation is covered by an active regression in
[import.e2e.ts](../web/e2e/import.e2e.ts), and clearing a JSON-type column filter by one in
[table.e2e.ts](../web/e2e/table.e2e.ts). No browser case is currently skipped.

Build the Release API and UI before running locally (Node.js 24):

```powershell
$env:SAVETOOLKIT_E2E_SCRATCH = 'D:/Temp/saveeditor/test-scratch/e2e-smoke'
New-Item -ItemType Directory -Force $env:SAVETOOLKIT_E2E_SCRATCH | Out-Null
$env:TEMP = $env:TMP = $env:TMPDIR = $env:SAVETOOLKIT_E2E_SCRATCH
$env:PLAYWRIGHT_BROWSERS_PATH = "$env:SAVETOOLKIT_E2E_SCRATCH/browsers"
$env:npm_config_cache = "$env:SAVETOOLKIT_E2E_SCRATCH/npm-cache"
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj --configuration Release
Push-Location web
npm ci --no-audit --no-fund
npm run build
npx playwright install chromium
npm run e2e
Pop-Location
```

Playwright's `webServer` starts the real API with `--no-build` on a free loopback port and uses
temporary workspace/reference-pack directories, removed after the run. Test output and the HTML
report remain under scratch. `SAVETOOLKIT_E2E_SCRATCH` overrides the platform default (the D: path
above on Windows, a directory under the system temp folder elsewhere).

## Build and check bundles locally

```powershell
./scripts/package.ps1 -Runtime win-x64 -OutputDirectory D:/Temp/saveeditor/test-scratch/framework-docs/packages
./scripts/smoke-package.ps1 -Runtime win-x64 `
  -PackageDirectory D:/Temp/saveeditor/test-scratch/framework-docs/packages `
  -ScratchDirectory D:/Temp/saveeditor/test-scratch/framework-docs/smoke
```

Omit `-Runtime` to build all four RIDs: win-x64, linux-x64, osx-arm64 and osx-x64. The script runs
`npm ci` and builds the UI before self-contained Release publishes. `-SkipWebBuild` reuses an
already built `wwwroot`; use this only when it matches the source being packaged. .NET and Node
are build-time requirements; bundle users do not need either installed. Supply an explicit output
directory outside private saves. Staging publish directories are removed even after failures.
Keep package archives for distribution or delete them when local verification is complete.
Test runs isolate and restore `TEMP`, `TMP` and Unix `TMPDIR`; cleanup failures produce a warning
with the directory to remove. Relative scratch and package output paths resolve from the caller's
PowerShell location.

Each zip (Windows) or tar.gz (Unix) contains a top-level version/RID directory, `wwwroot`,
the platform `e_sqlite3` native library, launcher, README and licence notices. A missing native
library or UI fails packaging. The launcher sets the content root, binds to `127.0.0.1:5180`,
and stores workspaces/reference packs under `data/` beside it. It requires a writable extraction
folder. Native platform prerequisites still apply (e.g. current supported .NET Linux/macOS OS
versions and Linux ICU/OpenSSL). Bundles are not signed or notarized. Unix bundles made on
Windows require `chmod +x start.sh SaveToolkit.Api`; releases package on their native OS.

The API binds the `Toolkit` section to startup-only typed options. Command-line arguments override
environment variables, which override `appsettings.json` (and its environment-specific variant).
The three existing settings are:

| Setting | Environment variable | Default for direct apphost/source runs |
| --- | --- | --- |
| `Toolkit:DataRoot` | `Toolkit__DataRoot` | System temp directory + `SaveToolkit/workspaces`; wiped when the workspace store starts. |
| `Toolkit:PackRoot` | `Toolkit__PackRoot` | Local application data directory + `SaveToolkit/reference-packs`; retained across restarts. |
| `Toolkit:Locales` | `Toolkit__Locales` | `en`; comma-separated, ordered preferences, with whitespace trimmed and empty entries removed. |

Absent or empty values use these defaults. Whitespace-only locale lists yield no preferences;
duplicates remain in order. Paths may be relative and need not exist yet. Whitespace-only or
malformed paths fail startup with an error naming `Toolkit:DataRoot` or `Toolkit:PackRoot`.
Settings are captured at startup; changes require a restart. Upload/form limits remain fixed constants.

Launchers supply their own command-line settings and do not forward extra arguments. To change
the URL or storage location, invoke the apphost directly, as in the
[user guide](USER_GUIDE.md#where-your-data-lives), with `--urls`, `--Toolkit:DataRoot` and
`--Toolkit:PackRoot`. The standard URL default remains `http://127.0.0.1:5180`.

To test the real bundle's configuration providers and `--urls` override, extract a matching native
archive into scratch, set `SAVETOOLKIT_TEST_BUNDLE` to its top-level bundle directory, and run:

```powershell
dotnet test --project tests/SaveToolkit.Core.Tests --configuration Release --filter-class '*ApiBundleConfigurationTests'
```

These three cases exercise appsettings, environment and command-line storage settings through
the packaged apphost, including actual workspace/pack writes. They skip explicitly when the bundle
variable is absent. Keep the [scratch environment](#disk-and-scratch) configured for this command.

`smoke-package.ps1` verifies the archive checksum, extracts into a unique scratch folder, starts
the actual apphost on a free loopback port, checks `/api/version`, HTML and its JavaScript entry
point, stops the server and removes the extraction. Supply `-ExpectedVersion 1.2.3` to require a
particular SemVer (commit metadata is ignored for that comparison). Each invocation retains uniquely
named `smoke-<id>.stdout.log` and `smoke-<id>.stderr.log` files in scratch.

## Publish a release

Only maintainers should create tags/releases. Ordinary CI has read-only repository permissions;
only the release publication job has `contents: write`. No additional release credential is needed
for this private repository: the workflow uses GitHub's scoped `GITHUB_TOKEN` through `gh`.

### Prepare curated notes and check the candidate

After integrating the release work, generate the next candidate's draft (PowerShell 7 on Windows,
Linux or macOS; git and full local history/tags are required):

```powershell
./scripts/release-notes.ps1 -Version 0.1.0-rc.3
```

To choose the base explicitly and replace an existing draft:

```powershell
./scripts/release-notes.ps1 -Version 0.1.0-rc.3 -PreviousTag v0.1.0-rc.2 -Force
```

The offline generator uses `git log --merges --first-parent` from the previous tag to HEAD.
The default base is `git describe --tags --match 'v*' --abbrev=0 --first-parent HEAD`;
explicit bases must be local tags ancestral to HEAD. Fetch history/tags separately when needed.
It prints candidates grouped by conventional-commit type and scope, recognizing both
`fix(core): ...` and repository merge subjects such as `merge: fix (xorjson): ... (#40)`.
Unrecognized subjects remain under `other`; `!` and merge-message `BREAKING CHANGE:` markers
remain visible. Direct commits and nested topic-branch merges are excluded: review direct
commits separately when preparing highlights. Each candidate links to its merge commit.
Net changes against the merge's first parent in `src/SaveToolkit.Sdk` or `docs/SDK_REQUESTS.md`
also flag that candidate **For plugin authors** and list it in that section.

The output is `docs/release-notes/v<Version>.md`, with the rc.2 highlights sections, a checksums
section and a temporary grouped inventory. It is a **draft**, not a finished changelog.
Review the candidates, write evidence-backed highlights, update known limitations, and remove
the temporary inventory and all `DRAFT`, `TODO`, `TBD`, `FIXME`, `PLACEHOLDER`, `XXX` markers
and HTML comments. Remove unused highlight sections. The checksum section describes verification;
actual hashes come from the packaged assets, so no hashes are invented before packaging.
Existing curated files are preserved unless `-Force` explicitly replaces them with a new draft.

Commit the curated notes through the normal integration process. On clean `main` at the
intended release commit, after its push CI has completed, run:

```powershell
./scripts/release-check.ps1 -Version 0.1.0-rc.3
```

The check rejects shallow history, another branch/detached HEAD, staged/unstaged/untracked
changes, an existing local tag, missing/empty/mistitled notes or placeholder markers, and
inconsistent `docs/SDK_REQUESTS.md` summary/status/appendix counts (including duplicate IDs).
Version validation matches the release workflow's SemVer restrictions. Since the application
has no pinned release-version file, it verifies the API's `MinVerTagPrefix` is `v`, the MinVer
package reference and central package pin exist, and no explicit application-version override
is introduced in the API project or root `Directory.Build.props` / `Directory.Build.targets`.
Do not bump independent npm metadata or plugin versions to match the release tag.

With `gh` installed, the latest `ci.yml` **main push** run for the exact HEAD must be completed
and successful. Missing, pending or failed runs and gh/query/authentication errors fail the check;
older successful runs cannot mask a newer failing run. Only an unavailable gh executable skips
this check, with a warning requiring manual CI verification. The script neither fetches tags nor
checks remote tag existence: fetch tags before preparing a real release. It does not tag, push,
dispatch, or publish. Re-run it if HEAD changes. The existing release workflow remains the
authoritative validation/publication gate, including for manual releases.

The regressions run through `./scripts/self-test.ps1 -ScratchDirectory <path>` in disposable
scratch git repositories with mocked gh responses; they never alter the checkout or contact CI.

### Tag or dispatch the checked revision

Two entry paths run the same validation and release pipeline:

1. **Tag:** tag the intended committed revision, then push that tag, e.g.
   `git tag v1.2.3 <commit>` and `git push origin v1.2.3`. The `v*.*.*` workflow validates the
   version and runs the complete reusable CI workflow before packaging all four RIDs.
2. **Manual:** Actions → Release → Run workflow, select the intended branch/revision and enter
   `1.2.3` or `1.2.3-rc.1` (without `v`). Or use
   `gh workflow run release.yml --ref main -f version=1.2.3`. The workflow records the exact commit,
   validates it, and builds with a local tag. Only after all packages pass does it create the remote
   tag and publish in the **same run**. A tag created using `GITHUB_TOKEN` does not trigger another
   workflow, so this path does not depend on a second tag event. Existing tag names are rejected.

Each RID is packaged and smoke-tested on its matching OS/architecture. Failure blocks publication.
Manual and tag release runs for the same version share a concurrency group, so publication is serialized.
[git-cliff](https://git-cliff.org/docs/) uses `cliff.toml` to group conventional commits since the
previous version tag, retain breaking-change markers, and include other commit subjects separately.
Follow [repository commit policy](../AGENTS.md#collaboration-policy); use `BREAKING CHANGE:`
for incompatible changes so release notes retain that signal.

For curated highlights, commit `docs/release-notes/<tag>.md` (including the `v` prefix,
for example `docs/release-notes/v0.1.0-rc.2.md`) at the revision being released. The publish
job prepends that file to the git-cliff changelog with a blank line between them; without
the file, the generated notes are unchanged. Use absolute GitHub links so citations also
work in the release body.

The publish job verifies all four checksums, creates a **draft**, uploads archives, per-archive
`.sha256` files and combined `SHA256SUMS`, then publishes with the generated notes. Tags containing
a prerelease suffix produce GitHub prereleases. Failed uploads leave a draft, not a partial public
release. Re-run failed jobs to resume a draft; already-published releases are never overwritten.
If a manual run created its remote tag before failing, re-run its failed publication job, not all jobs
(the initial version validation correctly rejects an existing tag). Alternatively dispatch a fresh
patch version. Inspect Actions and the release asset list before announcing a release.

Verify downloads from the archive directory with `sha256sum --check SHA256SUMS` on Linux or
`shasum -a 256 --check SHA256SUMS` on macOS; Windows can compare `Get-FileHash -Algorithm SHA256`
against the adjacent `.sha256` file. The manifest covers archives, not checksum files themselves.

## Rollback and maintenance

Never move a published version tag or replace its assets. Withdraw a broken release (mark it as a
prerelease or remove its release entry if necessary), document the fault, and publish a new patch
tag containing the correction or reverted code. Users can extract a prior known-good bundle to a
different directory and use their untouched save backups. Preserve the old `data/` directory before
switching versions; no automatic data downgrade is promised. Private saves never belong in releases.

Dependabot proposes weekly grouped NuGet, npm and GitHub Actions version updates. Review and run
the same CI before merging them. Container images and release-drafter are intentionally optional and
are not part of this pipeline. The first real hosted CI/release run must be verified after pushing;
local workflow/package checks cannot establish GitHub-side permissions or runner behavior.
