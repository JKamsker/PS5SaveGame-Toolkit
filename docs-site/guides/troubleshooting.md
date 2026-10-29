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

## A plugin scaffold test skipped

The synthetic tests should run without real saves. Sample-dependent conformance tests intentionally skip when their external archive is absent. A zero-failure run with skips proves the synthetic path only. See the [first plugin tutorial](../tutorials/first-plugin.md#run-the-tests-without-private-samples).

## The docs build fails

Use PowerShell 7, Python 3.10+, Node.js 24 and the .NET SDK selected by `global.json`. Run from the repository root with scratch outside the checkout. The build treats DocFX warnings, missing xrefs, broken internal links and contract drift as failures. See [building the documentation](../contributing/docs.md) for output locations and diagnostics.

An OpenAPI mismatch requires reviewing the API metadata and the existing snapshot guard. Do not copy the snapshot into the website or re-pin it just to make a build pass.
