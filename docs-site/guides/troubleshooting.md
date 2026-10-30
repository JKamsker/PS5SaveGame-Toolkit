---
title: Troubleshooting
---

# Troubleshooting

## The browser cannot connect

Keep the server terminal open. Use `http://127.0.0.1:5180`, rather than this documentation site's address. Extract the entire release into a writable folder and launch from its inner bundle folder. If port 5180 is occupied, follow [use another port](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/USER_GUIDE.md#use-another-port); the bundle launchers do not forward arguments.

## My file is not recognized

Confirm that it is decrypted, and check the exact title, region and format/version in the plugin README. A matching title ID alone is not proof of format support. Keep the original intact. Share a description, error and version when reporting a problem; do not attach decrypted saves or confidential data to a public issue.

## A field is read-only or absent

The plugin may not understand that schema or may deliberately protect an unsafe dependency. Search the full collection, check filters, and read its warnings and documented write limits. A reference-pack entry describes known data and does not create a record in your save.

## The workspace disappeared or a request is stale

Workspaces do not persist across server restarts and can expire while idle. Reimport the original or a separately retained export. After a concurrent or structural change, refresh the document before submitting another request. HTTP clients should read the [revision and error contracts](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/API.md#shared-responses-and-errors).

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
