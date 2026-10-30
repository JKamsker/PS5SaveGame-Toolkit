# HTTP API reference

The locally hosted API uses `/api` (default origin `http://127.0.0.1:5180`).
JSON request bodies require `Content-Type: application/json`; successful JSON
responses and errors use `application/json`. Property names are camelCase, null
properties are omitted, and enums serialize as strings. Identifiers are opaque,
case-sensitive unless stated otherwise; URL-encode them. Use query-string forms
for IDs containing `/`, because Kestrel preserves encoded slashes in route values.
There is no version prefix or authentication handshake. This is a local,
single-user application; workspaces are transient, not persisted across restarts.

The endpoint headings below are an executable inventory:
`ApiDocumentationTests` compares them in both directions with ASP.NET Core's real
`EndpointDataSource`, including aliases. SPA/static-file routes are outside this API.

## OpenAPI contract and regression guard

`GET /openapi/v1.json` publishes the machine-readable **OpenAPI 3.1** contract using
the first-party `Microsoft.AspNetCore.OpenApi` package. It is available in Development,
Staging, Production and other hosting environments. It describes the `/api` endpoints,
including route aliases, request bodies, parameters, response statuses/media types,
and named schemas. Static files, SPA navigation and the document-serving endpoint are
outside that inventory. Unknown `/api/*` paths retain JSON 404 responses.

`OpenApiContractTests` generates the actual HTTP document in three environments and
compares it with [OpenApi.v1.json](../tests/SaveToolkit.Api.Performance.Tests/OpenApi.v1.json).
The snapshot keeps the entire contract, including every method, route, status and
schema name. Object keys are sorted ordinally; line endings (including descriptions)
are normalized to `\n`. Only host-dependent `servers` are excluded; arrays retain their
order. Schema tests also check references/discriminators, custom binders, converted
values, binary/multipart media and structured error envelopes. The Python documentation
suite compares endpoint headings and snapshot operations in both directions, while the
HTTP test independently checks the runtime endpoint inventory.

With the [scratch environment configured](RELEASING.md#disk-and-scratch), review an
intentional contract change, then re-pin from the repository root in one PowerShell line:

```powershell
$env:SAVETOOLKIT_REPIN_OPENAPI = '1'; try { dotnet test --project tests/SaveToolkit.Api.Performance.Tests -c Release --filter-method '*Document_is_available_and_matches_the_contract' } finally { Remove-Item Env:SAVETOOLKIT_REPIN_OPENAPI }
```

Review the resulting JSON diff, update the endpoint documentation, then run the same
test without the variable and `python -B docs/tests/test_documentation.py --code-ref main`.
Ordinary tests never rewrite the baseline and require no samples for these checks.
The explicit re-pin switch adds no skipped test to ordinary runs. Shared middleware
errors such as unknown-path 404 and unsupported-method 405 remain described below;
host/proxy failures before application execution are outside generated operations.

## Shared responses and errors

Unless an endpoint states otherwise, success is **200**, resource removal is
**204** with no body, and unknown workspace/document/file/plugin/catalog/record
resources are **404**. A clean workspace may have expired; reimport it. The default
idle timeout is six hours; cleanup runs periodically and active requests retain leases.
Automatic cleanup retains workspaces with dirty independent documents or document groups,
including irreversible edits with no remaining undo. Undoing all reversible edits makes a
workspace eligible for expiry again. The decision reads host history only, without
serializing documents; busy operations are skipped and lease/access state is rechecked.
Export and direct download leave documents dirty. Explicitly close finished documents or
DELETE the workspace to release retained resources. Document close requires clean state;
workspace DELETE, disposal and server shutdown still discard edits. This retention provides
no restart persistence.

All API error responses include these ProblemDetails fields and legacy extensions:

```json
{
  "type": "about:blank",
  "title": "Not Found",
  "status": 404,
  "detail": "Document not found. Open the file again.",
  "error": "Document not found. Open the file again."
}
```

`title` is the standard HTTP reason phrase; `status` equals the HTTP status;
`detail` is a diagnostic for this occurrence; `error` duplicates `detail` for
existing clients. `type` is consistently `about:blank`. There is no required
`instance` property. Diagnostics may change; branch on status and optional
machine-readable `code`, not message text. The legacy JSON media type is retained
deliberately; these fields also appear on structured edit and preview failures.

| Status | Meaning |
| --- | --- |
| 400 Bad Request | Malformed JSON/form/query, missing required body members, invalid transport shape, negative body revision, unknown numeric export/sort enum, invalid collection query, malformed `If-Match`. |
| 403 Forbidden | Reference-pack mutation rejected by the same-origin check. |
| 404 Not Found | Missing resource, asset/icon, or unknown API path. |
| 405 Method Not Allowed | Path exists but does not support the HTTP method. |
| 409 Conflict | Stale JSON/query revision or retired ID (`code: "stale"`), dirty/owned document, invalid group confirmation, immutable embedded-pack removal, or other conflicting domain state. |
| 412 Precondition Failed | Document `If-Match` does not strongly match (`code: "stale"`). |
| 413 Content Too Large | Server/request size rejection, reference batch exceeds 256 pairs, or serialized collection page exceeds 2 MiB. Host/proxy rejections before application execution may have their own error body. |
| 415 Unsupported Media Type | Non-JSON content type on a JSON-body endpoint. |
| 422 Unprocessable Entity | Valid request with invalid save/archive/pack contents, export verification failure, rejected field/action/preview/history operation, or nothing to export. |
| 499 Client Closed Request | Cancelled operation; connected callers receive the shared error envelope, while aborted connections may receive no body. |
| 500 Internal Server Error | Unexpected failure; generic diagnostic, details logged on the server. |

Request aborts cancel import, export planning/verification/spooling, semantic review and collection
traversal at host checkpoints. Temporary files are cleaned and cancelled preflight retains the
document and its preview. Individual synchronous plugin calls finish before cancellation is
observed. The final check immediately before atomic mutation is the point of no return: apply,
undo/revision bookkeeping and any rollback finish together, even if the client disconnects.
Import similarly checks immediately before publishing a complete workspace. Refetch state after
an aborted mutation; a disconnected client cannot infer whether that final check was passed.

An **EditResult** has `ok`, `errors: [{valueId,message}]`, `changes:
[{valueId,oldValue,newValue}]`, `warnings: string[]`, optional `message`,
`structureChanged`, `stale`, optional `code`, and `state`. These fields remain
present on 409/422 alongside problem metadata. **DocumentState** is
`{canUndo,canRedo,isDirty,revision}`. A failed preview likewise retains its whole
`{revision,scope,plan,stale,code?}` envelope; `planId` is absent on rejection.
UI clients must inspect structured failures instead of discarding their fields.

Field values follow the published `FieldSpec`. Integers are decimal **strings**
on the response wire (Int128 precision); floats are numbers, booleans booleans,
and text strings. Exact numbers use `{kind:"exactNumber",text:"1.2300e+20"}`;
unions use `{kind:"union",type:"optionId",value:...}`. JSON null is an explicit
field value where permitted and is different from an absent edit `value`.
`ReferenceRecord.limit` is an Int64 JSON number and may exceed JavaScript's exact
integer range. See [the plugin guide](PLUGIN_GUIDE.md) for all view/field shapes.

## Revisions and conditional document operations

`revision` is a nonnegative document counter, not a workspace revision. Mutations,
including successful undo/redo, advance it. A no-op need not advance it. Refetch
the document after a structural change or stale rejection.

Successful document GET and edit/action/preview/history responses expose a strong revision
`ETag`, for example `"7"`, matching the revision in their response snapshot.
Send it as `If-Match: "7"` on document routes for an exact precondition. Lists
match if any strong tag matches; weak tags never match. `If-Match: *` requires an
existing document (normal resolution still returns 404 when absent). Invalid
header syntax returns 400; valid nonmatching tags return 412 without execution.
No `If-None-Match`/304 cache protocol is implemented.

JSON revisions remain optional for edits, direct actions, previews and history;
omitting them preserves unconditional legacy behavior. Plan apply and individual
change revert require an explicit revision. If a header and JSON revision are
both supplied, both must match: the header is checked first (412), then the body
(409). Document HTTP handlers share an operation gate so another document HTTP
operation cannot interleave between precondition check and execution. Core
callers outside HTTP must use Core's own revision-aware operations.

Collection query revisions deliberately tolerate value-only edits: an older
revision is accepted until structure changes; future or structurally stale
revisions return 409. Reference-field queries require exact revisions. An
`If-Match` header on either route always requires the exact current revision.
`If-Match` has no workspace-wide meaning on export/import or workspace routes.

## Discovery and workspaces

### `GET /api/version`

No parameters. Returns `{version:string}` from the API assembly informational
version (tag-derived version plus possible build metadata).

### `GET /api/plugins`

No parameters. Returns `PluginDto[]`, ordered by name: `{id,name,version,
description?,maturity,games:[{titleId,name}],references:string[],supportsGroups}`.
Plugin versions are independent of the application version.

### `GET /api/titles`

No parameters. Returns `{titleId,name,support,pluginIds}[]`, ordered by title ID.
`support` is `None`, `View`, or `Edit`; includes known unsupported titles.
It is derived from registered title IDs and plugin maturity, rather than probing each file or
checking its writer. Content-based JSON overlays do not register extra title IDs, so a title
listed with `None` can still contain editable JSON. Consult the
[user guide's per-file limits](USER_GUIDE.md#supported-titles-and-write-limits).

### `GET /api/workspaces`

No parameters or pagination. Returns currently retained workspace summaries:
`{id,name,createdAt,directoryCount,fileCount,titleIds}[]`.

### `POST /api/workspaces`

Multipart form containing one or more file parts; field names of file parts are
not significant. Optional text field `name` overrides the first filename stem.
Accepts decrypted ZIP dumps and loose files; loose filenames may carry relative
paths. Returns a **Workspace** (200, not 201); no Location header. The default
request/multipart limit is 8 GiB; imported expanded data is capped at 8 GiB and
50,000 files. HTTP form field count is capped at 100,000. Invalid/incomplete
multipart or no files: 400; malformed archive, invalid entry, failed CRC, excessive
expanded data/file count, or no save files after import: 422. Failed imports do
not publish a workspace.

### `GET /api/workspaces/{workspaceId}`

Returns **Workspace**: `{id,name,createdAt,titles,documents}`. Each title has
`titleId`, optional `gameName`, and `directories`. Directories expose
`id,name,rawName,isBackup,generation,twinId?,twinIdentical,hasIcon,sfo?,files`;
files expose `id,path,length,matches,openDocumentId?`. Matches identify plugin,
score, optional label and maturity. Documents are summaries with
`id,fileId,filePath,title,pluginId,isDirty,groupMembers?`.

### `DELETE /api/workspaces/{workspaceId}`

No body. 204 removes the workspace and schedules cleanup after active response
leases finish. 404 if absent. Deleting again returns 404.

### `GET /api/workspaces/{workspaceId}/directories/{directoryId}/icon`

No query parameters. Returns `image/png` streamed from `sce_sys/icon0.png`,
without an attachment filename. Missing directory/icon: 404.

### `POST /api/workspaces/{workspaceId}/files/{fileId}/open`

Optional JSON body `{pluginId?:string|null}`; null/omitted plugin chooses the best
probe match. Returns **Document**: `{id,fileId,filePath,title,plugin,canEdit,state,
view,groupMembers?}`. `view` is the abstract ViewNode tree. Opening the same file
with the same plugin returns its existing session; switching requires a clean
owner. Unknown file/plugin: 404; another dirty owner/group ownership: 409; failed
format parsing: 422. Does not require a revision or `If-Match`.

### `GET /api/workspaces/{workspaceId}/files/{fileId}/group-proposals`

Required query `pluginId`. Returns `{token,pluginId,title,members,errors}[]`.
Offers include explicit members (`fileId,relativePath,role`) and diagnostics;
do not confirm offers with errors. At most 32 members/alternatives and 128 retained
offers per workspace. Unknown anchor/plugin: 404. A plugin without group support returns **200
with an empty array**. Invalid member/validation results can appear in each offer's `errors`
while the listing still returns 200; format exceptions or too many alternatives return 422.
Proposals do not open a document or edit file bytes.

### `POST /api/workspaces/{workspaceId}/document-groups`

JSON `{proposalToken:string,releaseCleanDocumentId?:string}`. Returns **Document**
with `groupMembers`. Confirmation is fingerprint-bound and owns all members
atomically. An existing clean owner may be explicitly released. Missing token:
400; expired/changed offer or ownership conflict: 409; invalid group: 422.

## Documents, editing and binary reads

All document routes below resolve both path IDs, returning 404 when either is absent.
Views, lazy expansions and collection pages containing canonical field references are normalized
to ordinary fields before delivery. Their complete normalized JSON is bounded to 2 MiB; oversized
canonical values, annotations or combined projections return 413 without partial output. Opening
an independent or grouped document can likewise return 413 for an oversized reference-bearing
root view. Existing views without references keep their behavior. See [the field-reference contract](FIELD_REFERENCES.md).

### `GET /api/workspaces/{workspaceId}/documents/{documentId}`

No parameters. Returns **Document** and its revision ETag. View, metadata and
state are captured together. Root views may contain lazy sections and collections.

### `DELETE /api/workspaces/{workspaceId}/documents/{documentId}`

No body. 204 atomically closes a clean document and releases its pinned references and
all group-member ownership. Dirty documents return 409 ProblemDetails and retain their
live data, history, references and ownership. An edit or redo committed before close
executes prevents closure; a close that wins prevents queued mutations from succeeding.
Missing documents return 404, including requests bound before close that were waiting
on the document operation gate. Imported files are unchanged. Optional `If-Match`
applies in addition to the clean-state requirement. Export/download leave edits dirty;
undo to clean before closing the document, or DELETE the workspace to explicitly discard
its edits.

### `GET /api/workspaces/{workspaceId}/documents/{documentId}/nodes`

Required query `id` (opaque lazy ID). Returns expanded `ViewNode[]`, registering
their field/action bindings. Unknown ID: 404; retired ID: 409 with `code:"stale"`.

### `GET /api/workspaces/{workspaceId}/documents/{documentId}/collections`

Required query `collectionId`; safe alias for slash-containing opaque IDs. Other
parameters and responses are identical to the route alias below.

### `GET /api/workspaces/{workspaceId}/documents/{documentId}/collections/{collectionId}`

| Query | Default / bound |
| --- | --- |
| `offset` | Integer 0, minimum 0, up to Int32 maximum. |
| `limit` | Integer 50, inclusive 1–200; invalid values rejected, not clamped. |
| `q` (alias `text`) | Optional substring, maximum 256 UTF-16 code units; `q` takes precedence. |
| `filters` | Optional JSON object of string values; null values rejected; empty-string values mean no filter. |
| `filter.<column>` | Optional per-column string; overrides the same JSON filter, last value wins; an empty string clears it. |
| `sort` | Optional published column key; an empty string uses default ordering. |
| `descending` | Boolean false. |
| `includeEmpty` | Optional boolean; omission uses collection policy. |
| `cursor` | Optional opaque cursor, length 1–4096; cannot combine with nonzero offset. |
| `revision` | Optional Int64; structural staleness rules above. |

At most 32 filters, nonempty keys up to 128 units, values up to 256 units. Invalid
syntax, bounds, sort/filter columns, or cursor: 400. Missing collection: 404.
Empty-string filters are removed after per-column overrides and bounds checks,
before querying the document. Whitespace-only values remain literal filters.
Returns `{collectionId,columns,rows,totalCount,filteredCount,offset,limit,
nextCursor?,revision,hiddenEmptyCount}`. Rows have stable keys and ViewNode cells.
Pages over 2 MiB serialized JSON: 413, with no partial page emitted.

### `POST /api/workspaces/{workspaceId}/documents/{documentId}/edits`

JSON `{edits:[{valueId:string,value:any}],revision?:number}`. `edits` must be an
array with non-null entries and explicit IDs/values. Applies atomically. Returns
**EditResult** and revision ETag; semantic/spec/read-only rejection: 422; stale:
409. Fields can be indexed through the root view, lazy expansion, collection page or semantic-review
walk. Documents implementing `IFieldMetadataDocument` can also resolve unloaded IDs on demand;
see the [SDK-02 metadata contract](PLUGIN_GUIDE.md#field-metadata-without-rendering-a-page-sdk-02).
Storage-only SDK registration without that opt-in does not publish a field to HTTP clients.

### `POST /api/workspaces/{workspaceId}/documents/{documentId}/actions`

JSON `{actionId:string,parameters?:object|null,revision?:number}`. Parameters map
parameter IDs to field values; null/omission means none. Returns **EditResult**
and ETag. Unsupported action/invalid parameter/read-only mutation: 422; stale:
409. Inspection actions may run on read-only documents and do not create history.
Actions marked `supportsPlanning` must use the preview/apply workflow.

### `POST /api/workspaces/{workspaceId}/documents/{documentId}/undo`

Optional JSON `{revision?:number}`; no body remains supported. Returns
**EditResult** and ETag. Stale body revision: 409; nothing to undo: 422. Header
preconditions and history execution are serialized with other document HTTP calls.

### `POST /api/workspaces/{workspaceId}/documents/{documentId}/redo`

Same optional body and result as undo. Stale revision: 409; nothing to redo: 422.

### `GET /api/workspaces/{workspaceId}/documents/{documentId}/assets`

Required query `id` for a published asset. Returns raw bytes with the plugin's
content type; no attachment filename. Missing/unpublished asset: 404.

### `GET /api/workspaces/{workspaceId}/documents/{documentId}/blobs`

Required query `id`, Int64 `offset`, Int32 `count`. Negative offset: 400. Count is
clamped to 0–65,536, including negative values clamped to zero. Returns
`application/octet-stream` bytes, possibly shorter/empty at EOF, without a
filename or HTTP range protocol. Unknown blob: 404.

### `GET /api/workspaces/{workspaceId}/documents/{documentId}/download`

No parameters. Returns current independent document bytes as
`application/octet-stream` with `Content-Disposition: attachment`; filename is
the original file basename (`filename*` carries Unicode). Clean documents stream
the imported bytes (including read-only documents). Dirty documents serialize once;
all captured outputs are checked for valid imported paths and group ownership, then
reopened as a staged overlay using the document's pinned reference packs. The primary
bytes returned are the same snapshot that passed verification. Failed verification
returns 422 ProblemDetails with plugin/file diagnostics and no attachment. Group
documents return 409: use workspace export for atomic multi-file output. For independent
documents that rewrite siblings, use workspace export to download all outputs together.
Download does not clear dirty state/history or change the revision.

## Preview and semantic review

### `POST /api/workspaces/{workspaceId}/documents/{documentId}/actions/plan`

JSON `{actionId:string,parameters?:object|null,scope?:object|null,revision?:number}`.
Null/omitted scope means the whole document. `scope.rowKeys` is either null
(document scope) or at most 5,000 distinct nonempty stable keys; `[]` selects none.
Alternatively supply `scope.queryScope:{collectionId,query,revision,
excludedRowKeys?:string[]}`. `query` uses the collection query model (`text`,
`filters`, `sortColumn`, `descending`, `includeEmpty`, `offset`, `limit`, `cursor`);
Core resolves all matching rows, bounded to 5,000 before exclusions, rather than
only the visible page. Do not supply both row keys and a query scope.
View actions can bind browser selection through `selectionTableId`, matching an F8 `collectionId`
or eager table `selectionId`. The [selection-table contract](PLUGIN_GUIDE.md#selection-table-binding-and-advisory-conformance-40)
owns binding, fallback and advisory-conformance rules. This metadata adds no request field or
server ownership check: callers still supply the explicit `scope` above.

Returns `{planId?,revision,scope,plan:{changes,skippedRows,warnings,errors},stale,
code?}` and ETag; scope is normalized. Each consequence includes ID/label,
old/new value, derived and existence flags. No document edits occur. Invalid
plans: 422 with the same envelope, no `planId`; stale: 409. A preview/request is
bounded to 4 MiB retained data, and only the latest successful preview is retained.

### `POST /api/workspaces/{workspaceId}/documents/{documentId}/actions/apply`

Required JSON `{planId:string,revision:number}` (revision must be explicitly
present). Returns **EditResult** and ETag. Plans are document-specific and
single-use, including failed domain validation; re-preview after a failure.
Stale/replaced/reused plan or changed consequences: 409; rejected effects: 422.
Effects are recomputed and checked before atomic application as one undo group.
Transport/header rejection happens before invoking the domain operation.

### `GET /api/workspaces/{workspaceId}/documents/{documentId}/changes`

No parameters. Returns `{changes,complete,warnings,revision,coverage?}`. Each change has
 `valueId,label,path,originalValue,currentValue,originalExists,currentExists,
 canRevert`, plus optional `target` for fields reached through current F8 queryable
 rows. Eager/lazy changes retain their existing JSON shape. Metadata-backed changes may
 additionally carry `canonicalValueId`, `bindingKind`, `presentation`, `aliases`,
 `member`, `navigationTarget`, `canNavigate`, and `revertReason`. The effective
 canonical ID is emitted only when the server has an explicit published identity;
 aliases contain label/path/raw ID projections without duplicate value payloads.
 `member` is authoritative group context (`role`, `relativePath`, optional `groupTitle`
 and `provenance`) returned by the host's bounded `IGroupedFieldContext` resolver.
 `navigationTarget` and `canNavigate` are server-owned; clients must not infer a target
 from an ID, caption or path. Eager/lazy changes retain their existing JSON shape. `target` contains
`{collectionId,rowKey,query}`: the exact unfiltered page query (including
`includeEmpty:true`, `limit`, and either `offset` or `cursor`). Open the enclosing
sections/tabs from `path`, query the opaque `collectionId` through the query-string
collection endpoint with the log's `revision`, then locate `rowKey` and `valueId`
in that page. Translate query `text`/`sortColumn` to endpoint `q`/`sort` if present;
encode all identities and cursors. Navigation never edits the save. A removed
field has no current target; refresh the review after any mutation or stale page.

F8 labels include the collection title, stable row key and field label. Review
scans the original and current documents separately in deterministic native order,
including empty slots. Global limits **per snapshot**, across all collections:
10,000 query rows, 100 pages of at most 200 rows, and 16 MiB of complete serialized
UTF-8 pages (including read-only cells). Retained field/navigation data also uses
the existing 16 MiB original/current memory cap; the shared view walk remains
bounded to 100,000 nodes and depth 128. Hosts can configure query budgets with
`DocumentSessionOptions.MaxReviewQueryRows`, `MaxReviewQueryPages`, and
`MaxReviewQueryBytes`. No collection-dirtiness capability exists, so exposed
collections are scanned to discover changes; only changed fields are returned.
The navigation target is a Core/HTTP review contract, separate from SDK `ViewTarget` and field
metadata discovery. Clients must not synthesize a target from an ID prefix or reuse its page query
after the log revision changes.

`coverage`, when present, contains `{examinedCount,omittedCount,budgetBytes,usedBytes,
 omittedReasons,omittedCountIsLowerBound}`. The lower-bound flag is true when the server
 could not establish an exact omitted set, including bounded group-context resolution or
 truncated traversal. `complete:false` means bounded traversal/baseline coverage is partial; do not
infer missing changes or field additions/removals from unvisited data. F8 budget
cuts and invalid/failed paging add explicit **truncated** warnings identifying the
collection/reason. Display these warnings even when `changes` is empty; never show
“no changes” or “complete review” for partial coverage. Baseline survives normal
history eviction. Existing `canRevert` governs individual revert, including F8
fields; derived/read-only fields and structural additions/removals cannot revert
individually.

The framework's Generic JSON browser fixture provides synthetic protocol coverage for
canonical/alias search, member context, unavailable navigation, incomplete coverage,
retry, revert focus handoff and responsive keyboard access. It does not prove that a
Pragmata or Gearbox/XorJson production adapter has implemented `IGroupedFieldContext`;
those adapters must publish their real member role and relative path before production
member provenance can be shown.

### `POST /api/workspaces/{workspaceId}/documents/{documentId}/changes/revert`

Required JSON `{valueId:string,revision:number}`. Restores a revertible field's
original value as an undoable operation. Returns **EditResult** and ETag.
Missing shape/revision: 400; stale: 409; absent/non-revertible change: 422.

## Workspace export

### `POST /api/workspaces/{workspaceId}/export/plan`

JSON `{scope?:"ChangedFiles"|"ChangedDirectories"|"Everything",mirrorBackups?:boolean}`;
default scope `ChangedDirectories`, mirrors true. Body `{}` is valid; body is
required. Returns `{files:[{relativePath,length,reason}],changedCount}`. This
verifies staged output, including integrity and group ownership, without editing
workspace state. `changedCount` counts overridden files, including mirrored backups.
An unchanged workspace can return an empty plan (200).

### `POST /api/workspaces/{workspaceId}/export`

Same required JSON/options as export plan. Returns `application/zip` with
`Content-Disposition: attachment` and `<safe-workspace-name>-edited.zip`.
Safe names retain letters/digits/hyphen/underscore and replace other characters
with `_`. Entries follow the toolkit's imported relative paths. Standard `<TITLE>/sdimg_<directory>/...`
layouts are retained; additional generations are prefixed `genN/`. Wrapper prefixes before the
recognized title/save directory are not retained. Unknown-title layouts keep their normalized
imported path without an `UNKNOWN/` prefix: for example `unrecognized-layout/mystery.dat` exports as
`unrecognized-layout/mystery.dat`. `ChangedFiles` includes only
rewritten files; `ChangedDirectories` includes entire changed save directories
and system files; `Everything` includes the imported set. Mirroring replaces
identical original console backups unless separately owned/open.

Nothing changed: 422 except with `Everything`. Verification failure: 422;
conflicting ownership/rewrites: 409 or export-domain 422 as appropriate. Export
is idempotent with respect to workspace/document state and exported paths/bytes:
it does not write back to imports, consume history, clear dirty state or change
revision. Repeating an unchanged request produces the same entry contents; ZIP
entry timestamps may differ, so whole-archive bytes are not a stable checksum.
There is no workspace ETag or cross-document revision snapshot guarantee.

## Reference packs and document lookups

### `GET /api/reference-packs`

No parameters. Returns `{packs:ReferencePackInfo[],errors:[{origin,message}]}`.
Load errors are diagnostics in a successful listing. Pack information includes
identity/version/title/origin and declared datasets/compatibility metadata.

### `POST /api/reference-packs`

Multipart form with exactly one ZIP file; file-part name is unrestricted. Returns
imported `ReferencePackInfo` (200). Default archive limit 64 MiB, request envelope
limit 65 MiB, expanded total 128 MiB, manifest 256 KiB, records file 32 MiB,
asset 1 MiB, 20,000 files, 250,000 records per catalog; image dimensions at most
2048 each. Invalid envelope/file count: 400; invalid pack/format/size: 422 (host
request limit may return 413). Cross-site mutation: 403. Later imports affect
newly opened documents; existing documents keep pinned pack versions.

### `DELETE /api/reference-packs/{packId}`

No body. 204 removes a user pack, 404 if absent, 409 for embedded packs. Same-origin
check as import (403); `Sec-Fetch-Site` must be absent, `same-origin` or `none`,
and any Origin must match the request host authority. Existing pins survive removal.

### `GET /api/workspaces/{workspaceId}/documents/{documentId}/sources`

Returns `{datasets:[{packId,packTitle,packVersion,origin,locale?,datasetId,kind,
title?,itemCount,attribution,used}]}` for pinned sources. Attribution includes
source/license/author/retrieval date/modifications. This is provenance, not a remote fetch.

### `GET /api/workspaces/{workspaceId}/documents/{documentId}/references/{catalogId}`

Optional query `q` (substring key/name), `category` (exact case-insensitive;
empty means all), `sortBy` (`Key`, `Name`, `Category`, only if published),
`descending` (false), `offset` (0), `limit` (50). Integer/boolean/sort binding
errors: 400. Offset is clamped to at least zero; limit to 1–200. No additional
explicit text-length cap beyond server request limits. Use the catalog's declared
sort choices. Returns `{catalogId,packId,items,total,offset,limit,categories,
sortKeys}`. Missing catalog: 404. Records contain `{key,name,category?,limit?,
description?,assetId?,source?,attributes?,packId,datasetId}`.

### `GET /api/workspaces/{workspaceId}/documents/{documentId}/references/{catalogId}/record`

Required query `key`. Returns `{record,packTitle,packVersion,dataset}` with the
full dataset provenance. Missing catalog/record: 404. Does not edit the document.

### `GET /api/workspaces/{workspaceId}/documents/{documentId}/reference-fields/candidates`

Required query `valueId`; optional `actionId` resolves an action parameter instead
of a document field. Document fields can resolve through SDK-02 metadata without a
rendered page; action parameters require a published action. Optional exact `revision`.
Other queries match catalog search (`q,category,sortBy,descending,offset,limit`),
with the same clamping bounds.
Returns `{catalogId,items,total,offset,limit,categories,revision,sortKeys}`. Items
are candidates with `rawId,label?,description?,category?,assetId?,source?,dataset?,
assetDataset?,eligibility:{available,eligible,reasons,selectable}}`. Policies
may mark a displayed candidate unselectable. Missing field: 404; stale: 409.

### `GET /api/workspaces/{workspaceId}/documents/{documentId}/reference-fields/record`

Required query `valueId,rawId`; optional `actionId,revision`. Returns one candidate
including policy/provenance. Unknown raw IDs still return 200, with absent catalog
metadata and eligibility governed by custom-ID policy. Missing field: 404; stale:
409. This differs intentionally from a missing catalog record lookup.

### `POST /api/workspaces/{workspaceId}/documents/{documentId}/reference-fields/records`

JSON `{pairs:[{fieldId:string,recordKey:string,actionId?:string}],revision?:number}`.
`fieldId` is the published `valueId`; `actionId` identifies its owning action for
parameters. At most **256 pairs**, including duplicates; exceeding this returns
**413** ProblemDetails with the limit in `detail`. Missing/null pairs, null entries,
missing/blank field IDs, non-string/null keys, blank supplied action IDs or negative
revisions return 400. Empty keys are exact raw IDs; an empty array is valid.

Returns `{revision,results:[{fieldId,recordKey,actionId?,status,candidate?,error?}]}`
with exactly one result per pair in input order, preserving duplicates. Each pair
uses its own published field's catalog, custom-ID policy and eligibility assessment;
results are never shared by raw key alone. A known reference field returns
`status:200,candidate` with the same policy/provenance as the single-record route,
even for an unknown raw key. Missing/unpublished/non-reference fields or missing
action parameters return `status:404,error` for that pair, without aborting others.
An unknown workspace/document returns a whole-request 404. An exact revision
mismatch or retired binding rejects the whole request with 409 and `code:"stale"`;
`If-Match` applies as usual (412). No partial response is emitted. The document
HTTP operation gate covers the entire batch. This read does not edit the document.

### `GET /api/workspaces/{workspaceId}/documents/{documentId}/reference-assets`

Required query `id` (`packId:asset/path`). Returns only declared, pinned assets
within the configured asset bound; missing/unavailable/oversized: 404. Uses the
asset's validated content type, no attachment filename. Headers:
`X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'`,
`Cache-Control: private, max-age=3600`.
