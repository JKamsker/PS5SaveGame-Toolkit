# Documentation site

This is the source for <https://jkamsker.github.io/PS5SaveGame-Toolkit/>. It contains a curated public learning path, generated SDK and HTTP references, and a small theme for DocFX's built-in modern template. The production React app in `web/` is separate.

Use PowerShell 7, Python 3.10+, Node.js 24 and the .NET SDK in [global.json](../global.json). From the repository root:

```powershell
./scripts/build-docs.ps1 -ScratchDirectory D:/Temp/saveeditor/test-scratch/docs-site
$site = (Get-Content D:/Temp/saveeditor/test-scratch/docs-site/latest-site.txt -Raw).Trim()
python -m http.server 8080 --bind 127.0.0.1 --directory $site
```

Open `http://127.0.0.1:8080`. Builds, generated sources, empty schema-host data roots, downloads and temporary files stay in scratch. On other machines choose a writable scratch path outside the checkout and sample trees. Follow [disk and scratch](../docs/RELEASING.md#disk-and-scratch) for shared conventions. The build restores its environment after completion and never deletes earlier runs.

Read [the complete docs contributor guide](contributing/docs.md) for source checks, architecture, failure diagnostics and automatic deployment. The initial public publication is a maintainer action after review; adding the workflow does not enable Pages by itself.

Versions are pinned in [.config/dotnet-tools.json](../.config/dotnet-tools.json) (DocFX **2.81.0**, existing tools preserved) and [package-lock.json](package-lock.json) (Redoc CE **3.0.1**). The build stages only [public-files.json](public-files.json), generates scaffold excerpts from the supported templates, exports the current OpenAPI contract and validates it against the existing snapshot without rewriting it. The Redoc standalone bundle and MIT licence are copied from the locked npm package into the built site; no CDN, remote fonts or telemetry is required.

Repository guides remain authoritative and are linked on GitHub, with their anchors checked locally. Owner queues, raw research, private samples, application bundles and the repository as a whole are not site inputs.

Validation:

```powershell
python -B docs/tests/test_documentation.py --code-ref HEAD
python -B -m unittest discover -s scripts/docs/tests
./scripts/build-docs.ps1 -ScratchDirectory D:/Temp/saveeditor/test-scratch/docs-site
actionlint
git diff --check
```

[Documentation workflow](../.github/workflows/docs.yml): PRs validate; pushes to `main` and manual dispatches on `main` publish through `actions/configure-pages@v6`, `actions/upload-pages-artifact@v5` and `actions/deploy-pages@v5`. The build is read-only; only deployment receives Pages/OIDC write permissions. Administrators enable **Settings → Pages → Source: GitHub Actions** once and should restrict the `github-pages` environment to `main`. No custom secrets or PATs are needed. The expected base is `/PS5SaveGame-Toolkit/`; generated links and assets are relative and checked at both root and project URL bases.
