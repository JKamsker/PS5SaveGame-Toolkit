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
compatibility. Web checks include lint when configured, TypeScript, Vitest and the production build.
NuGet/npm downloads are cached; per-project TRX and web JUnit results are uploaded even on failure,
and each job writes a summary. The repository currently has no web lint script.

Local equivalent (PowerShell 7, .NET SDK from global.json, Node.js 24):

```powershell
Remove-Item Env:SAVETOOLKIT_ARTIFACTS -ErrorAction SilentlyContinue
$env:SAVETOOLKIT_TEST_TEMP = 'D:/Temp/saveeditor/scratch/cicd/tests'
$env:TEMP = 'D:/Temp/saveeditor/scratch/cicd'
$env:TMP = $env:TEMP
$env:npm_config_cache = 'D:/Temp/saveeditor/scratch/cicd/npm-cache'
./scripts/test.ps1 -Configuration Release -ResultsDirectory D:/Temp/saveeditor/scratch/cicd/results
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

### Code coverage

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

### Built app browser smoke tests

The Ubuntu-only `e2e` CI job runs alongside validation with a 12-minute limit. Chromium imports
the committed synthetic fixtures through the bundled UI, edits a Generic JSON field, reviews its
change and visible confidence/console warning, then verifies every exported payload byte. A second
test undoes the edit and verifies the original bytes. No private samples or `SAVETOOLKIT_ARTIFACTS`
are needed. Failures upload the Playwright HTML report with traces and screenshots.

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
./scripts/package.ps1 -Runtime win-x64 -OutputDirectory D:/Temp/saveeditor/scratch/cicd/packages
./scripts/smoke-package.ps1 -Runtime win-x64 `
  -PackageDirectory D:/Temp/saveeditor/scratch/cicd/packages `
  -ScratchDirectory D:/Temp/saveeditor/scratch/cicd/smoke
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

`smoke-package.ps1` verifies the archive checksum, extracts into a unique scratch folder, starts
the actual apphost on a free loopback port, checks `/api/version`, HTML and its JavaScript entry
point, stops the server and removes the extraction. Supply `-ExpectedVersion 1.2.3` to require a
particular SemVer (commit metadata is ignored for that comparison). Each invocation retains uniquely
named `smoke-<id>.stdout.log` and `smoke-<id>.stderr.log` files in scratch.

## Publish a release

Only maintainers should create tags/releases. Ordinary CI has read-only repository permissions;
only the release publication job has `contents: write`. No additional release credential is needed
for this private repository: the workflow uses GitHub's scoped `GITHUB_TOKEN` through `gh`.

Two entry paths run the same validation and release pipeline:

1. **Tag:** tag the intended committed revision, then push that tag, e.g.
   `git tag v1.2.3 <commit>` and `git push origin v1.2.3`. The `v*.*.*` workflow validates the
   version and runs both OS validation jobs before packaging all four RIDs.
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
Write commits as `feat(scope): ...`, `fix(scope): ...`, `ci(scope): ...`, etc. Give commit bodies the
reason for changes; use `BREAKING CHANGE:` for incompatible changes.

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
