---
title: Troubleshooting
---

# Troubleshooting

## The editor does not load

The editor is a static web app; nothing needs to stay running except, for a local release copy, the static file server. Serve the extracted `SaveToolkit-<version>-web` folder from its root and open its `http://127.0.0.1:<port>/` address; opening `index.html` from the file manager does not work. If the page reports that WebAssembly is unavailable, use a current desktop browser. If the runtime fails to start with a MIME-type error on Windows, the registry maps `.js` to the wrong type for Python's server; use another static server, for example `npx serve`. See [the user guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/USER_GUIDE.md#troubleshooting).

## My file is not recognized

Confirm that it is decrypted, and check the exact title, region and format/version in the plugin README. A matching title ID alone is not proof of format support. Keep the original intact. Share a description, error and version when reporting a problem; do not attach decrypted saves or confidential data to a public issue.

## A field is read-only or absent

The plugin may not understand that schema or may deliberately protect an unsafe dependency. Search the full collection, check filters, and read its warnings and documented write limits. A reference-pack entry describes known data and does not create a record in your save.

## The workspace disappeared or a request is stale

Workspaces live only in the browser tab: reloading or closing it discards them, and idle workspaces can expire. Reimport the original or a separately retained export. After a concurrent or structural change, refresh the document before submitting another request. Integrations should read the [revision and error contracts](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/API.md#shared-responses-and-errors).

## A preset cannot be applied or an import is stale

Expand **Compatibility boundaries** and read the reason. Stored compatibility and current owned
target resolution are separate checks. Missing or Unsupported targets block application; an
Ambiguous target needs a current choice and **Preview again**. A stale save preview needs document
refresh; a stale package import needs a fresh file preview, and a library changed on disk needs
**Reload library and preview again**. Follow [the preset journey](presets-and-transfer.md) before
confirming replacements. Importing a preset never applies it to your save.

## The review is empty or incomplete

Clear **Find a changed field** to check whether a filter hid the rows. If **Review coverage is
incomplete** appears, an empty list proves only that no changes were found in the reviewed
portion. Read the coverage warning, not just the count. See [reviewing changes](metadata-review.md)
for unavailable targets, aliases and per-field Revert limits.

## A caption does not fit or is unavailable

Read **Save caption unavailable: …**; this action needs an eligible Tsushima primary and its
existing valid `SUBTITLE`. It cannot add a key or resize a slot. Shorten over-capacity text using
the UTF-8 byte hint, and remove line breaks or NUL. See [the caption journey](manual-caption.md)
for exact eligibility, ownership conflicts and export scopes.

## A plugin scaffold test skipped

The synthetic tests should run without real saves. Sample-dependent conformance tests intentionally skip when their external archive is absent. A zero-failure run with skips proves the synthetic path only. See the [first plugin tutorial](../tutorials/first-plugin.md#run-the-tests-without-private-samples).

## The docs build fails

Use PowerShell 7, Python 3.10+, Node.js 24 and the .NET SDK selected by `global.json`. Run from the repository root with scratch outside the checkout. The build treats DocFX warnings, missing xrefs, broken internal links and contract drift as failures. See [building the documentation](../contributing/docs.md) for output locations and diagnostics.

An OpenAPI mismatch requires reviewing the API metadata and the existing snapshot guard. Do not copy the snapshot into the website or re-pin it just to make a build pass.
