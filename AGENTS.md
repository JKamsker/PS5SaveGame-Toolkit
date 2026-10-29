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
./scripts/test.ps1 [-Filter <Name>]                                # everything
./scripts/test.ps1 -Configuration Release -ResultsDirectory <path>  # CI-equivalent backend + TRX
./scripts/package.ps1 -Runtime win-x64 -OutputDirectory <path>      # self-contained UI/API bundle + SHA-256
./scripts/smoke-package.ps1 -Runtime win-x64 -PackageDirectory <path> -ScratchDirectory <path>
./scripts/new-plugin.ps1 -Name <Name> -Id <id> -TitleIds PPSA..... -GameName "<Game>"
cd web; npm install; npm run dev                                   # UI on :5173, proxies /api to :5180
dotnet run --project src/SaveToolkit.Api                           # API on http://127.0.0.1:5180
```

Tests use the MTP runner (`global.json`), so pass `--project`. Warnings are errors.
CI runs without private samples on Ubuntu and Windows; missing samples must skip cleanly.
Use Node.js 24, `npm ci`, `npm run typecheck`, `npm test` and `npm run build` for web validation
(run `npm run lint --if-present` when lint is configured). Validate workflow edits with `actionlint`.
Application versions come from `v`-prefixed SemVer tags through API-only MinVer; plugin versions
stay independent. See `docs/RELEASING.md` before creating release tags or running the Release workflow.

## Rules for plugin work

- Stay inside your `plugins/<Name>/` folder. Do **not** edit `src/`, `web/`, `tests/` or other plugins.
  If the SDK is missing something, work around it and describe the need under `## SDK requests` in your
  plugin README. Exception: adding a `<PackageVersion>` line to `Directory.Packages.props`.
- Follow `docs/PLUGIN_GUIDE.md` (hard rules: never lose data, fix integrity, honest confidence labels,
  bounded views, stateless plugin, no I/O).
- **Research before you implement.** Use web search for existing format documentation, community
  tools (GitHub: save editors, extractors, 010 Editor templates, wikis), crypto keys and checksum
  algorithms for your game/engine. Port proven approaches instead of guessing. Cite every source in
  the plugin README. Treat downloaded code as untrusted: read it, do not execute it blindly.
- Verify claims against the actual sample bytes. Mark anything unconfirmed as `Inferred`/`Unknown`.
- Tests: the conformance suite must pass; add format-specific tests (hash vectors, known sample values,
  corruption rejection). Tests must skip, not fail, when samples are absent.
- C#: nullable enabled, file-scoped namespaces, `sealed` by default, small files (<400 lines),
  no `dynamic`, no reflection tricks, no unsafe code unless justified in a comment.
- Do not add repo tooling: no git hooks, build targets, `Directory.Build.*` files, lint scripts or
  `.gitattributes` in plugin folders. File size and style are checked in review.
- Git: conventional commits (`feat(pragmata): ...`). Commit on your branch only; never push.
  Never commit anything from `artifacts/` or extracted sample data.

## Forge

GitHub (`github`), private repo `JKamsker/PS5SaveGame-Toolkit`, api_base `https://api.github.com`.
Use `gh` for PRs, issues, releases and Actions.

## Subagents

Use by default: gpt-6.1 sol; high reasoning
Eg ``codex exec --yolo -m gpt-6.1-sol -c 'model_reasoning_effort="high"' "Your prompt here"``
If you have an internal tool or skill for that, prefer that over ``codex exec``.
