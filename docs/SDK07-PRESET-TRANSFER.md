# Toolkit preset packages

The **Toolkit presets** panel offers **Import presets** and **Export presets** on the
current editor, including read-only saves. Transfer uses the existing host preset library.
It never changes a save document, revision, dirty flag, selection, undo/redo history or
workspace export. Applying a preset remains a separate, plugin-authoritative preview.

## Portable contract

`PresetPackage` in `src/SaveToolkit.Core/Presets/PresetTransferModels.cs` contains
`schemaVersion: "1"`, `provenance: { producer, exportedAt }`, and `presets`, an array of
full `ToolkitPreset` records. Every preset includes its schema, ID, human name,
created/updated timestamps, compatibility envelope and semantic actions. Every action
contains `actionId`, typed `parameters`, `instances` and `scopeKind`.

All compatibility fields must be explicitly present in a package, including null for
unknown build/variant/reference/schema/role/capability-version values. Missing fields
are rejected before record constructors can supply defaults. This does not turn null
into a wildcard: [the preset compatibility contract](SDK07-PRESETS.md) is enforced again
when applying, and the current plugin must prove every boundary it publishes.
Unknown plugins/titles/builds may be stored for later. The import preview labels their
status relative to the current save; importing never offers a bypass or automatic apply.

The producer is public toolkit provenance, not a machine/user identity. Export does not
add save paths, save bytes, session/document IDs, physical row keys, candidate choices,
plan tokens or credentials. Semantic target identity remains the plugin's contract;
an arbitrary loose save document is not a transfer format. Extra envelope fields,
duplicate JSON property names and unsupported versions are rejected.

## Exact values and limits

`PresetPackageCodec` retains parameter numbers as `JsonElement`. Untagged numbers keep
their original lexemes; strings, booleans, null, tagged `exactNumber` and tagged `union`
values roundtrip through the existing SDK value contract. For example:

```json
{"amount":900719925474099312345678901,"ratio":{"kind":"exactNumber","text":"1.2300e+120"}}
```

The browser uploads the original `File` and downloads the server's `Blob`; it never
parses and reserializes package JSON with JavaScript `Number`. JSON whitespace/escaping
may change, while typed values, numeric lexemes and Unicode text remain intact.

The whole package is at most 512 KiB, with 1–256 presets and a JSON depth of 16.
Each preset has 1–32 actions, each action at most 128 parameters and 256 semantic targets.
Title IDs are limited to 32 per preset; names to 100 characters; identifiers and field
text have explicit conservative limits in the codec. Arrays/arbitrary nested save
objects are not parameter values. Control requests are limited to 32 KiB/depth 8.
The server checks declared length before reading and limits streamed/chunked reads to
the cap plus one byte. Record/action/parameter/instance counts are checked before
typed record materialization. Storage's separate whole-file/count limits also apply
to the resulting library before confirmation.

## Review and transaction

1. Upload to `POST /api/presets/transfer/preview`. This validates the complete package
   and returns an expiring read-only preview with every preset's boundary and conflicts.
2. For each ID/name conflict, choose **Keep both**, **Replace existing** with a specific
   target, or **Cancel**. Name matching trims and compares ordinally ignoring case.
   Duplicate names within one package also need an explicit choice. No filename chooses
   a target. Two imports cannot replace the same record.
3. Send `PresetImportResolution` to `/resolve`: `{token,choices:[{index,decision,replaceId?}]}`.
   Decisions are `keepBoth` or `replace`. Unconflicted items use `keepBoth`. The response
   has a new token and shows every resulting name and every replacement before writing.
   Keep both retains unused IDs, generates a new ID on collision, and suffixes conflicting
   names with ` (2)`, ` (3)`, etc. Replace preserves the chosen target ID and replaces
   only that record; other name collisions still receive a distinct reviewed name.
4. Confirm with `POST /commit`, `{token}`. `PresetStore.ImportLibrary` rechecks the
   whole-library identity, validates a staged dictionary, writes one atomic file
   replacement, then publishes the staged in-memory library. Persistence failure leaves
   both disk and memory unchanged; the same confirmation can be explicitly retried.

Successful CRUD, imports, reloads or changed durable file content invalidate old previews.
Tokens expire after ten minutes, are single-use after successful commit, and the service
retains at most eight pending previews. `DELETE /previews/{token}` cancels read-only
staging; expiry also cleans it up. A failed/aborted confirmation should be checked by
refreshing the library before attempting a fresh import, because a response can be lost
after successful persistence. No automatic mutation retries occur.

Export uses `POST /api/presets/transfer/export`, `{ids:[...]}` with an explicit selection
of 1–256 distinct library IDs. The UI can select one, several or all records. A deleted
selection fails rather than producing an incomplete package. The human download name
is `Toolkit presets.toolkit-presets.json` and never derives from save paths.

## Failures and accessible recovery

Transfer failures return the normal typed API error envelope: 400 for malformed control
requests/choices, 409 for stale or consumed previews/selections, 413 for request size,
422 for invalid/future/domain-invalid packages or an overfull resulting library, and
503 for host storage failure or the bounded preview capacity. Capacity responses include
`Retry-After`. Invalid packages never partially persist. A stale import requires a fresh
file preview, not another write against the old identity.

Native modal dialogs use the existing focus helper, labelled headings, Escape/Cancel,
single failure alerts, explicit retries and focus restoration to the import/export
trigger. Import accepts reselecting the same file. The list filter stays mounted across
dialogs. Transfer controls remain independent from read-only document mutation gating.
Long Unicode names wrap; dialog content scrolls within narrow viewports. New code and
styles load only when the corresponding transfer dialog opens.

## Sources and evidence boundary

- [.NET JsonDocumentOptions](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsondocumentoptions?view=net-10.0)
  documents parser depth and duplicate-property controls.
- [Native dialog accessibility](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/dialog)
  and [showModal](https://developer.mozilla.org/en-US/docs/Web/API/HTMLDialogElement/showModal)
  document modal focus and Escape behavior.
- `PresetPackageTests`, `PresetTransferTests`, `PresetTransferApiTests` and
  `web/e2e/preset-transfer.e2e.ts` cover synthetic transfer/transaction paths. Native-game
  semantic identity, actual title/build compatibility and game acceptance remain plugin
  adopter responsibilities; transfer alone establishes none of those claims.
