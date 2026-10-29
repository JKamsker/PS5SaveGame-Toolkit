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
Semantic references retain `kind`, `stableId`, and optional `label`, `role`, `path`,
`build` and `provenance` exactly. Stable IDs, labels, roles and builds are bounded to
256 UTF-16 code units, paths and provenance to 512; non-null values must be
nonblank without control characters. These are public semantic metadata, never
opaque candidate IDs or transient resolution choices.

All compatibility fields must be explicitly present in a package, including null for
unknown build/variant/reference/schema/role/capability-version values. Missing fields
are rejected before record constructors can supply defaults. This does not turn null
into a wildcard: [the preset compatibility contract](SDK07-PRESETS.md) is enforced again
when applying, and the current plugin must prove every boundary it publishes.
The six nullable compatibility boundaries and semantic path/provenance reuse the
host's `PresetContractValidation.MaxMetadataLength` (512 UTF-16 code units). Oversized
metadata is rejected during package preview with the field name and limit, before a
preview token or choices are allocated. Values are never truncated. Unicode surrogate
pairs count as two units; independent UTF-8 package and durable-file byte caps still apply.
Preview also runs the common host staging validator without persistence, so intrinsic
record constraints cannot first fail after conflict decisions. Resolution repeats staging
against the complete resulting library, including its aggregate count and byte limits.
Unknown plugins/titles/builds may be stored for later. The import preview labels their
status relative to the current save using the server's `ToDocumentDto` compatibility
summary, including read-only, unsupported-action and `canPreview`/`previewReason` status.
A capture-only plugin is honestly marked as unable to apply the imported preset. This advisory check calls
no plugin planner and changes no document. Importing never offers a bypass or automatic apply.

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
Each preset has 1–32 actions, each action at most 128 parameters, and at most 256 semantic
targets across all its actions. The aggregate target count is checked before typed package materialization.
Title IDs are limited to 32 per preset; names to 100 characters; identifiers and field
text have explicit conservative limits in the codec. Arrays/arbitrary nested save
objects are not parameter values. Control requests are separately limited to 258 KiB/depth 8. This is derived from
256 records × (128 UTF-16 ID units × 6 maximum JSON-escape bytes + 256 choice syntax
bytes), plus 2 KiB for the escaped token/envelope and formatting. It covers every
maximum-length ID, including quotes, backslashes, Unicode and escaped surrogate pairs,
for full-library export and explicit replacement. The package limit remains 512 KiB.
The server checks declared length before reading and limits streamed/chunked reads to
the cap plus one byte. Record/action/parameter/instance counts are checked before
typed record materialization. Storage's separate whole-file/count limits also apply
to the resulting library before confirmation.

Transfer endpoints read bounded raw request streams directly. The host's route-local
`PresetRequestBodyLimitMetadata` applies to CRUD/capture/preview/apply JSON binding;
it does not add a global 256 KiB limit or a second buffering pass to transfer routes.
This preserves the distinct 264,192-byte control and 524,288-byte package limits,
including valid requests padded to their exact cap and chunked requests without a
declared length. An oversized transfer stream is rejected after at most cap + 1 bytes.

## Review and transaction

1. Upload to `POST /api/presets/transfer/preview`. This validates the complete package
   and returns an expiring read-only preview with every preset's boundary and conflicts.
   `POST /api/workspaces/{workspaceId}/documents/{documentId}/presets/transfer/compatibility`,
   `{token}`, returns indexed `PresetImportCompatibilityDto` summaries for that preview.
   This read-only request uses the same server mapper as stored document presets.
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
   whole-library identity against the fingerprint published with the live map, validates a staged dictionary, writes one atomic file
   replacement, then publishes the staged in-memory library. Persistence failure leaves
   both disk and memory unchanged; the same confirmation can be explicitly retried.

Successful CRUD, imports, reloads or changed durable file content invalidate old previews.
A successful reload increments the generation even when bytes are identical. A failed
reload preserves the last-good library, fingerprint and generation. The fingerprint
is derived from the same bounded file bytes that produced the staged map, or the exact
bytes of a successful atomic write. Snapshot/commit reject externally changed bytes
until a successful bounded reload; a fresh preview alone cannot acknowledge a new
file while retaining an older live map. The dialog offers **Reload library and preview
again**, preserves the selected file and filter, and refreshes server-owned list summaries. Reload and import share a
validated staged map and bounded storage helper under the store lock, preventing a
concurrent host CRUD operation from being lost during publication. Every durable array
must validate completely: null records, unsupported schemas, blank IDs and malformed
known records reject the whole reload without changing the last-good map or identity.
Direct bounded file opens classify malformed/disappeared storage as 422 and I/O or
authorization failures as retryable 503. Transfers also reject unsupported schemas.
Tokens expire after ten minutes, are single-use after successful commit, and the service
retains at most eight pending previews. `DELETE /previews/{token}` cancels read-only
staging; expiry also cleans it up. A failed/aborted confirmation should be checked by
refreshing the library before attempting a fresh import, because a response can be lost
after successful persistence. No automatic mutation retries occur.

Export uses `POST /api/presets/transfer/export`, `{ids:[...]}` with an explicit selection
of 1–256 distinct library IDs. The UI can select one, several or all records. A deleted
selection fails rather than producing an incomplete package. The human download name
is `Toolkit presets.toolkit-presets.json` and never derives from save paths.

## Published API contract

`PresetPackageDto` and its transfer-only record/compatibility/action/reference graph in
`src/SaveToolkit.Api/Contracts/PresetPackageDtos.cs` describe the stricter wire contract.
Every compatibility member is required, including the six explicitly nullable boundaries;
`instances` (including an empty array) and `scopeKind` are required for every action.
Legacy CRUD schemas keep their optional/default fields. Version, cardinality and text
bounds are described in OpenAPI; cross-field timestamp ordering, nonblank/no-control
text and exact typed parameter validation remain codec checks. Export success describes
an `application/json` binary download body, and cancellation explicitly describes 204.

## Failures and accessible recovery

Transfer failures return the normal typed API error envelope: 400 for malformed control
requests/choices, 409 for stale or consumed previews/selections or a library requiring reload, 413 for request size,
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
- [ASP.NET Core OpenAPI metadata](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata?view=aspnetcore-10.0)
  documents required/nullability metadata; [document transformers](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/customize-openapi)
  allow the transfer graph to reuse the existing recursive value component after export.
- `PresetPackageTests`, `PresetTransferTests`, `PresetTransferApiTests` and
  `web/e2e/preset-transfer.e2e.ts`, `preset-transfer-library.e2e.ts` and `preset-transfer-recovery.e2e.ts` cover synthetic
  transfer/transaction paths, including all 256 maximum-length Unicode IDs and explicit
  reload after an external library addition. This measures one conflict per imported
  record; the bounded worst-case 65,536 conflict matches is not a UI performance claim. Native-game
  semantic identity, actual title/build compatibility and game acceptance remain plugin
  adopter responsibilities; transfer alone establishes none of those claims.
- [ASP.NET Core middleware testing](https://learn.microsoft.com/en-us/aspnet/core/test/middleware?view=aspnetcore-10.0)
  documents direct `TestServer.SendAsync` request-stream control. The combined pipeline
  regression `PresetTransferRequestLimitTests` supplies explicit declared/chunked headers
  and observes consumed bytes; native browser tests separately exercise running Kestrel.
