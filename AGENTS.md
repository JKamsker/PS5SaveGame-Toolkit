# PS5 Save Game Toolkit — agent instructions

Web-based, plugin-driven editor for decrypted PS5 save games.
Backend: ASP.NET Core (.NET 10). Frontend: React + TypeScript (Vite) in `web/`.

## Layout

```
src/SaveToolkit.Sdk       Plugin contract: IGamePlugin, ISaveDocument, ViewNode, FieldSpec, FieldValue
src/SaveToolkit.Core      Import (zip → save set), plugin catalog, sessions, validation, undo, export
src/SaveToolkit.Api       HTTP API + static hosting of the built web app
src/SaveToolkit.Testing   SampleSaves + PluginConformanceTests<T>
plugins/<Name>/           One folder per game/engine plugin (+ its .Tests project)
tests/                    Framework tests
web/                      React UI (renders the abstract view protocol)
docs/PLUGIN_GUIDE.md      How to write a plugin — read before touching plugins/
docs/SAMPLES.md           Sample titles and what is known about them
artifacts/                Sample saves (git-ignored, never commit, never copy into the repo)
```

## Commands

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj           # builds Core, Sdk and every plugin
dotnet test --project tests/SaveToolkit.Core.Tests                 # framework tests
dotnet test --project plugins/<Name>/SaveToolkit.Plugins.<Name>.Tests
./scripts/test.ps1 -Filter <Name>                                  # API build + matching backend projects
./scripts/test.ps1 -Coverage -Configuration Release -ResultsDirectory <path>
./scripts/self-test.ps1 -ScratchDirectory <path>                    # script/scaffold regressions
cd web; npm ci; npm run dev                                        # UI on :5173, proxies /api to :5180
dotnet run --project src/SaveToolkit.Api                           # API on http://127.0.0.1:5180
```

Tests use the MTP runner (`global.json`), so pass `--project`. Warnings are errors.
See [RELEASING.md](docs/RELEASING.md) for the authoritative script parameters, CI jobs,
web checks, coverage, packaging, versioning and release procedure. Configure scratch as described
in its [disk and scratch guidance](docs/RELEASING.md#disk-and-scratch) before running tests/builds.
See [the scaffold walkthrough](docs/PLUGIN_GUIDE.md#scaffold) to create a new plugin.
Documentation checks: `python -B docs/tests/test_documentation.py --code-ref main`.

## Rules for plugin work

Read [PLUGIN_GUIDE.md](docs/PLUGIN_GUIDE.md) before touching a plugin. Its
[contribution workflow](docs/PLUGIN_GUIDE.md#contribution-workflow) owns plugin scope, research,
style and tooling rules; its [hard rules](docs/PLUGIN_GUIDE.md#hard-rules) own preservation,
integrity, purity, confidence and the owner's licence rule 9. Use those rules instead of older plans.

## Collaboration policy

- Use conventional commits (`feat(pragmata): ...`) with a short summary and a body explaining
  the changes and why they were needed. Commit on your assigned branch only; never push or merge
  unless the task explicitly authorizes it. Never commit private samples or extracted save data.
- For [#40](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40), security and vulnerability
  reviews are out of scope by owner rule. Correctness, data safety and malformed-input robustness
  checks remain in scope; mutation fuzzing is such a robustness check.

## Forge

GitHub (`github`), private repo `JKamsker/PS5SaveGame-Toolkit`, api_base `https://api.github.com`.
Use `gh` for PRs, issues, releases and Actions.

## Subagents

Use by default: gpt-6.1 sol; high reasoning
Eg ``codex exec --yolo -m gpt-6.1-sol -c 'model_reasoning_effort="high"' "Your prompt here"``
If you have an internal tool or skill for that, prefer that over ``codex exec``.
