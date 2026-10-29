---
title: Build and publish the docs
description: Reproduce the documentation site locally and understand the automatic GitHub Pages workflow.
---

# Build and publish the docs

The site uses **DocFX 2.81.0**, its built-in modern template and a small local theme. SDK pages come from the current public assembly and XML comments. **Redoc CE 3.0.1**, pinned by the docs-only npm lockfile and served locally with telemetry disabled, renders the current OpenAPI 3.1 contract. No frontend application bundle, private save or reference pack is published.

## Build from a clean checkout

Install PowerShell 7, Python 3.10+, Node.js 24 and the .NET SDK selected by `global.json`. The first build needs NuGet and npm restore access. From the repository root, choose a scratch directory outside the checkout and your original saves:

```powershell
$scratch = Join-Path ([IO.Path]::GetTempPath()) 'save-toolkit-docs'
./scripts/build-docs.ps1 -ScratchDirectory $scratch
$site = (Get-Content -LiteralPath "$scratch/latest-site.txt" -Raw).Trim()
python -m http.server 8080 --bind 127.0.0.1 --directory $site
```

On the owner's Windows workstation, use the task-specific scratch convention in the repository's [disk and scratch guidance](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/RELEASING.md#disk-and-scratch). The script sets and restores temporary-storage variables, directs local downloads and build outputs to scratch, and preserves existing CI NuGet-cache configuration.

Open `http://127.0.0.1:8080`. Stop the preview with **Ctrl+C**. The server serves only static docs. Each build gets a new `build-<id>` directory; `latest-site.txt` points to the last **successful** output. Retained logs, restores and builds remain under scratch for inspection. Never recursively clean a sample directory or someone else's scratch.

## What the build verifies

1. Stage only `docs-site/public-files.json` and generated scaffold excerpts. It never copies the whole repository.
2. Restore the pinned local .NET tools and locked docs-only npm dependencies.
3. Build the current API and SDK in Release into scratch. The production frontend is not built.
4. Start a short-lived loopback API on an ephemeral port with fresh workspace and pack roots; request only `/openapi/v1.json`, then stop it.
5. Compare that schema with the existing reviewed contract using its normalization rules. A difference fails the build and never updates the snapshot. Only the variable server origin is replaced with `http://127.0.0.1:5180` for documentation.
6. Generate SDK metadata from the current DLL and XML, then build DocFX with warnings as errors. Missing xrefs fail the build.
7. Check all local HTML links, assets and fragments, required SDK symbols, HTTP renderer resources and search data. HTTP smoke checks exercise the same files at `/` and `/PS5SaveGame-Toolkit/` and confirm application API paths return 404.

To preview the project base path as well as root without copying output or creating a junction:

```powershell
python -B scripts/docs/serve_site.py --site $site --port 8080
```

Open `http://127.0.0.1:8080/PS5SaveGame-Toolkit/` and follow a deep link or search result. Redoc's two attribution logos are preserved locally and inlined into its generated bundle; its upstream attribution link remains. No CDN, remote fonts or telemetry is needed to read either reference.

If the schema changes intentionally, review the [existing OpenAPI regression guard](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/API.md#openapi-contract-and-regression-guard). Preserve its tests and reviewed snapshot. Do not hand-maintain generated API pages.

## Edit content

Edit tutorial and navigation sources in `docs-site/`; edit public C# XML comments in their owning source project for SDK descriptions. Add every new public source to `public-files.json`. Generated metadata, exported JSON and HTML stay in scratch and are not committed.

The repository [plugin guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/PLUGIN_GUIDE.md), [user guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/USER_GUIDE.md) and [HTTP behavior guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/API.md) remain authoritative. Link them for complete policy and behavior instead of maintaining a second exhaustive contract. Scaffold code excerpts are generated from the supported templates.

Run the source checks and rebuild after a content change:

```powershell
python -B docs/tests/test_documentation.py --code-ref HEAD
python -B -m unittest discover -s scripts/docs/tests
./scripts/build-docs.ps1 -ScratchDirectory $scratch
```

The source checks also verify the allowlist, tutorial links into authoritative documents, version pins and deployment policy. The rendered-site check is part of every build.

## Automatic publication

The **Documentation** workflow validates pull requests, pushes to `main` and manual dispatches. Pull requests only build and check the site. Pushes to `main` rebuild and deploy the verified static output through the official GitHub Pages artifact/deployment actions. A manual dispatch deploys only when run on `main`.

Repository administrators must select **Settings → Pages → Build and deployment → Source: GitHub Actions** once. The `github-pages` environment should permit deployment only from `main`. No PAT or custom secret is needed. The expected project URL is [jkamsker.github.io/PS5SaveGame-Toolkit/](https://jkamsker.github.io/PS5SaveGame-Toolkit/).

The deployment job alone receives `pages: write` and `id-token: write`; the build has `contents: read`. No fork or pull-request event can deploy. The workflow does not enable Pages, change visibility or provision an external service itself. See the repository's [release guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/RELEASING.md) for the CI job inventory.

## Upstream documentation

- [DocFX assembly and XML generation](https://dotnet.github.io/docfx/docs/dotnet-api-docs.html)
- [DocFX modern-template customization](https://dotnet.github.io/docfx/docs/template.html)
- [DocFX REST input limitations](https://dotnet.github.io/docfx/docs/rest-api-docs.html)
- [Self-hosting Redoc CE](https://redocly.com/docs/redoc/deployment/html)
- [Disabling Redoc telemetry](https://redocly.com/docs/redoc/telemetry)
- [GitHub Pages custom workflows](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages)
