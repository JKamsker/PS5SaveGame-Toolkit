# Writing a game plugin

A plugin teaches the toolkit one game (or one engine format). It answers three questions:

1. **Is this my file?** `Probe`
2. **What is in it?** `Open` → an `ISaveDocument` that describes the data as an *abstract view*
3. **What are the new bytes?** `ApplyEdits` / `InvokeAction` → `Serialize`

The plugin never touches HTML, CSS, HTTP or the file system. It describes *what* the data is
(types, ranges, labels, grouping, confidence) and the framework decides *how* it looks.

```
browser (React)  ──JSON──▶  Api  ──▶  Core: DocumentSession ──▶  your ISaveDocument
   renders ViewNodes          validates every edit against the FieldSpec you published,
   picks controls by spec     keeps undo/redo, verifies Serialize() output by re-opening it
```

## Scaffold

Install the .NET 10 SDK selected by `global.json`, then run these commands in PowerShell
from the repository root (no solution-file edits are needed):

```powershell
./scripts/new-plugin.ps1 -Name ExampleGame -Id example-game -TitleIds PPSA00000 -GameName "Example Game"
dotnet build plugins/ExampleGame/SaveToolkit.Plugins.ExampleGame/SaveToolkit.Plugins.ExampleGame.csproj
dotnet test --project plugins/ExampleGame/SaveToolkit.Plugins.ExampleGame.Tests
python docs/tests/test_documentation.py
```

Replace the example name, ID and placeholder title with your new plugin's values. The name must
not already exist under `plugins/`; the scaffolder refuses to overwrite an existing plugin.
Names start with an uppercase ASCII letter and contain at least two letters/digits; IDs use
lowercase letters/digits separated by dots or hyphens. Title IDs have four uppercase ASCII letters
and five digits (normally `PPSA` followed by five digits). Omit `-TitleIds` for a generic format
plugin; its sample tests use `PPSA00000` until you choose a real sample title.

This creates `plugins/<Name>/SaveToolkit.Plugins.<Name>/` (picked up by the app automatically)
and `plugins/<Name>/SaveToolkit.Plugins.<Name>.Tests/`. The generated read-only plugin builds
without warnings. Its **synthetic demonstration format is not a real game format**: `STKDEMO1`,
a bounded little-endian record count and signed integer records, with unknown trailing bytes preserved.
The supplied title IDs are targets, not evidence of game support. Research the real format before
replacing the demonstration probe, parser, fixture and README claims together.

The initial test run exercises synthetic parsing, bounded collection queries, read-only bindings,
corruption rejection and byte-identical serialization. Sample-dependent tests, including conformance
and its inherited `MutationFuzz` fact, skip when the archive is absent; this is a bootstrap,
not real-save validation.
`<Name>SampleTests.cs` demonstrates `SampleSaves.Load`, which reports a real skip instead of silently
returning. Once you implement the real parser, supply samples as described under [Tests](#tests)
and check that those skips disappear.

The documentation command must pass, but its plugin inventory comes from `HEAD` (or `--code-ref`),
so it does **not** validate an uncommitted plugin's README or register it in the support/licence tables.
Review the [required README sections](#plugin-readme) yourself. A throwaway scaffold must remain
uncommitted and be removed after the exercise. For a plugin you intend to ship, request the central
support/licence documentation updates from the maintainer when they fall outside your allowed scope.

### Try the read-only SDK helper view

The generated `<Name>Document.cs` is a complete minimal integration of the
[shared SDK helpers](#shared-sdk-helpers), with the required namespaces already imported:

1. `SaveToolkit.Sdk.Binary.BoundedBinaryCursor` checks the signature, bounds the record count with
   `ReadCount`, and rejects truncated `ReadInt32LE` records with a contextual `SaveFormatException`.
2. `SaveToolkit.Sdk.Collections.InMemoryCollection<T>` receives the complete bounded record model,
   stable keys, one adapter per metadata column and a page-only row renderer. The metadata becomes
   a view through `ToTableNode`; `IQueryableDocument.Collections` and `Query` expose it to Core.
3. The renderer calls `BoundSaveDocument.Field` without a setter, registering read-only `GetValue`
   bindings. `CanEdit` is false, and `Serialize` returns the full original buffer and relative path.

To make one small change in `<Name>DocumentTests.cs`, change the count in `ExampleBytes` from
`3, 0, 0, 0` to `4, 0, 0, 0` and insert `30, 0, 0, 0` before the trailing `0xAA, 0xBB`.
Update the fixture comment and the `page.TotalCount` assertion from 3 to 4. In the existing
`SyntheticView_QueriesCompleteModelAndPreservesBytes` test, add:

```csharp
var added = queryable.Query(collection.Id, new() { Text = "30", Limit = 1 });
var addedField = Assert.IsType<FieldNode>(Assert.Single(added.Rows).Cells["value"]);
Assert.Equal(FieldValue.Of(30), document.GetValue(addedField.ValueId));
```

Rerun the build and test commands above. Keep numeric sorts on native values; formatting them
as strings would put `10` before `2`. The supplied test
already checks sorting before paging, search, edit rejection and preservation of unknown trailing bytes.
Offsets are suitable row keys only for this fixed read-only example; real structural formats need
native record identities that survive insertion, removal and reorder.

Scaffold regression coverage (including real generated-project builds and tests with absent and
synthetic samples) runs through `./scripts/self-test.ps1 -ScratchDirectory <scratch-path>`.
It needs the repository's .NET SDK and package restore access, but no private saves or Pester.
See [script parameters](RELEASING.md#script-parameters) and [disk and scratch](RELEASING.md#disk-and-scratch)
for shared runner options and temporary-storage setup.

## Contribution workflow

- Stay inside `plugins/<Name>/`; do not edit `src/`, `web/`, `tests/` or other plugins.
  Adding a `<PackageVersion>` to `Directory.Packages.props` is the exception. Work around missing
  SDK capabilities and record them under `## SDK requests` in the plugin README; consult
  [the triaged backlog](SDK_REQUESTS.md) before treating a shipped helper as missing.
- Research before implementation: search format documentation, community editors/extractors,
  templates, wikis, format keys and checksum algorithms. Reuse proven approaches within rule 9,
  cite sources in the README and verify claims against actual sample bytes. Treat downloaded code
  as untrusted: read it before use. Mark unconfirmed meanings `Inferred` or `Unknown`.
- Use nullable C#, file-scoped namespaces and `sealed` by default. Keep non-generated files under
  300 lines where possible and never above 500 without a documented justification. Prefer composition
  and reused helpers; do not split into partials merely to meet a line count. Avoid `dynamic`,
  reflection tricks and unsafe code (any exception needs a reason in a comment).
- Do not add repository tooling inside plugin folders: no hooks, build targets, `Directory.Build.*`,
  lint scripts or `.gitattributes`. File size and style are reviewed centrally.
- Follow [repository collaboration policy](../AGENTS.md#collaboration-policy) for commits and review
  scope, and [Tests](#tests) for conformance, format-specific cases and missing-sample skips.

## Contract (src/SaveToolkit.Sdk)

| Member | Rules |
| --- | --- |
| `IGamePlugin.Info` | Lower-case dotted `Id` (`pragmata`, `engine.unreal-gvas`), semver `Version`, every PS5 title id you support in `Games` (all regions you know of), `References` with upstream URLs. |
| `Probe(file)` | Cheap: use `file.ReadHeader(n)`, `file.FileName`, `file.Directory.TitleId`. **Never throw.** Return `ProbeScore.Title` (title id + layout/magic match), `Engine` (generic engine format), `Format` (generic container) or `ProbeResult.None`. Put a human label in `Label` ("Gameplay save, slot 3"). |
| `Open(file)` | Parse fully and validate (magic, checksums, decryption). Throw `SaveFormatException` with a user-facing message on bad data. Siblings are available through `file.Sibling(...)`/`file.Directory`. |
| `Open(file, references)` | Optional overload (default: calls `Open(file)`). The framework always calls this one, with the reference packs pinned for the document. Implement it when you use [reference packs](#reference-packs); let `Open(file)` forward with `IReferenceContext.Empty`. |
| `ISaveDocument.BuildView()` | Root `ViewNode`. Called again after `StructureChanged`. |
| `ExpandNode(lazyId)` | Children of a lazy `SectionNode`. Use for anything big (raw trees, long lists). |
| `GetValue(valueId)` | Current value of every field you ever exposed. |
| `ApplyEdits(edits)` | **Atomic.** Either all apply or none (return `Errors`). Values are already type- and range-checked against your spec. Report every changed value incl. derived ones in `Changes` with the old value. |
| `InvokeAction(id, params)` | Optional bulk operations ("Max all items"). Return `Changes` for undo; for structural actions return a `Revert` token (see [Structural edits](#structural-edits-add--remove--reorder)); `Undoable = false` only as a last resort (it clears history). |
| `RestoreMemento(token)` | Only for structural actions: go back to the state a `Revert` token describes and return the token that reverses it. |
| `Serialize()` | Complete new bytes for every file you rewrite. Without edits it **must** be byte-identical to the input. |
| `GetAsset` / `ReadBlob` | Images (`ImageNode`) and hex pages (`HexNode`). |

`BoundSaveDocument` implements the bookkeeping (binding registry, atomic apply with rollback,
`GetValue`). Prefer it: build views with `Field(id, label, spec, get, set, validate)`.
For storage-only registration, use the protected
`RegisterField(valueId, spec, get, set = null, validate = null)`, then render with
`Field(valueId, label)` when needed. Registration retains the spec/getter/setter/validator without
calling the getter or creating a node; a missing setter makes the rendered field read-only.
Structural changes and memento restores clear these bindings, so register them again against the
current model. This does not publish host metadata or bypass host spec validation; direct plugin
calls still own validation for unpublished IDs. See [SDK-02](SDK_REQUESTS.md#prioritized-inventory)
for the remaining metadata/primary-alias contract.
See `tests/SaveToolkit.Core.Tests/Fakes/CounterPlugin.cs` for the smallest complete example.
`BoundSaveDocument.ApplyEdits` rollback includes setters that throw after changing their bound value, failed
readbacks and failures while enumerating `DerivedChanges`. Setters must accept their previous value
for rollback; `DerivedChanges` reports consequences and must not independently mutate state outside
those bindings. Custom `ApplyEdits` and action implementations still own their atomicity.

## View vocabulary (src/SaveToolkit.Sdk/Views)

| Node | Use for |
| --- | --- |
| `SectionNode(title, children)` | Grouping. `LazyId` = load children on demand. `Badge` for counts. |
| `TabsNode([Tab])` | Top-level areas (Character / Inventory / World / Raw). |
| `FieldNode(valueId, label, spec, value)` | One value. `Confidence` (`Verified` / `Inferred` / `Unknown`) and `Warning` are shown to the user. |
| `TableNode(columns, rows)` | Homogeneous records with typed fields, text, images, actions and compact summaries; nested content opens in row details. Live search/filtering, confidence/errors, explicit selection and fixed-height virtualisation. Prefer F8 above about 100 inventory rows. |
| `QueryableTableNode(collectionId, title, columns, totalCount)` | Server-backed collection for inventories/catalogues larger than an eager view. The browser sends bounded text/filter/sort queries and receives stable-keyed `TableRow`s a page at a time. |
| `StatsNode([Stat])` | Read-only headline figures (play time, level, slot). |
| `TextNode(text)` | Notes and warnings (`Tone`), `Monospace` for hashes/offsets. |
| `ActionNode(id, label)` | Plugin command, optional `Parameters` form and `Confirm` prompt. |
| `ImageNode(assetId, alt)` | Screenshots, thumbnails. |
| `HexNode(blobId, length)` | Raw bytes, paged. |
| `GraphNode(title, items, edges, textAlternative)` | Read-only, bounded prerequisite DAG with states, costs, branches and links to existing controls. |
| `MapNode(title, bounds, markers, textAlternative)` | Read-only regional map or external-link-only location list. Use local art/coordinates permitted by rule 9, with provenance. |

### Maps and prerequisite graphs (F9)

These nodes are optional presentations of existing data, never new editing or gameplay contracts.
Use stable item/marker IDs independent of labels, array indexes and filter order. Both constructors
require a non-empty `TextAlternative` overview. The browser also **always offers a text list** with
the same labels, states, categories/branches, costs, prerequisite names, descriptions and navigation.
The list stays available when a background is absent or fails to load. It is paged at 50 items and
uses the same filters as the diagram. Do not put essential facts only in coordinates or artwork.

`GraphItem(id, label, state)` uses `Unlocked`, `Locked`, `Available` or `Unknown`. Optional `Cost`
is display text, not a framework-enforced budget; `Branch` groups items. `GraphEdge(from, to)`
means **from is a prerequisite of to**. The graph must be a DAG with unique edges and existing
endpoints. The browser computes a deterministic layered layout, shows state/cost, and filters by
status, branch and text. Graph cards and list links jump to controls; they never execute actions.

`MapMarker(id, label, status, category)` uses `Found`, `Missing` or `Unknown`. Keep discovered,
completed and unknown meanings honest in `Description` and optional `Confidence`; unknown is
not missing. Omit **both** `X` and `Y` when the location is unconfirmed or external-only. Positions
are native coordinates. Supply a descriptive `CoordinateSpace`, explicit `MapBounds`, and
`MapTransform(scaleX, scaleY, offsetX, offsetY)`:

```text
displayX = X * scaleX + offsetX
displayY = Y * scaleY + offsetY
```

Display X increases rightwards and display Y downwards. Negative scales can flip axes; no rotation
or projection is inferred from an image's dimensions. Transformed markers must be inside the bounds.
The renderer supports zoom, scroll/drag/keyboard pan, category/status/text filters, a legend and a
selected-marker detail panel. All markers, including those without positions, appear in the list.

**Use external links for material still refused under rule 9 or unavailable for import.** A missing
community-data licence alone does not require link-only treatment. Set `ExternalSource` on a map or
`ExternalLink` on a marker to `new ExternalViewLink(httpsUrl, label, attribution)`. For example:

```csharp
new MapNode("Region", new MapBounds(0, 0, 1, 1),
    [new MapMarker("cache", "Cache", MapMarkerStatus.Unknown, "Collectibles")],
    "Consult the external regional guide; no art or coordinates have been imported.")
{
    ExternalSource = new ExternalViewLink("https://example.org/map", "Open regional map",
        "Example community; NC terms. Link only."),
};
```

No artwork or community coordinates are imported in this example. Core and plugins never fetch
anything. The browser never loads external map images, tiles, scripts or iframes, and never
prefetches these links. External requests happen only when the user clicks a source link. Links
must be absolute HTTPS URLs without credentials, whitespace or backslashes; the UI shows the
parsed host and attribution, opens a new tab, and sets `rel="noopener noreferrer"`. This also
applies to source/licence links in map attribution. Invalid links are rejected by Core; the web
renderer additionally renders them as non-clickable text if given a malformed payload.

**Follow rule 9 for imports**, including the 2026-10-28/29 owner decisions on community facts,
rights-reserved guides and publisher assets. Record the source, rights holder and revision/date;
NC/no-redistribution restrictions remain refusal reasons. For a background allowed by that policy, resolve
a local F2 asset with `references.GetAsset(id)` and `references.AssetProvenance(id)`. If unavailable,
omit `Background`. Otherwise use
`new MapBackground("pack.id:maps/region.png", alt, provenance.Attribution, EmbeddingPermitted: true)`.
This is served through the existing local F2 reference-asset endpoint, with full attribution next
to the diagram and in Sources. Remote URLs and ordinary plugin asset IDs are not backgrounds.
`EmbeddingPermitted` defaults to false and is an explicit assertion by the plugin author; the
framework cannot decide reuse policy from a licence name. Set it only after recording the applicable
terms or owner-policy basis from rule 9, including its required rights-holder attribution for publisher artwork.

Embedding **any coordinates**, including save-derived positions and original schematics, similarly
requires `CoordinateAttribution` (`ReferenceAttribution`) and `CoordinateEmbeddingPermitted = true`.
Record original work as such rather than inventing an upstream source. Community coordinate
terms and provenance must be recorded separately; a permissive extractor's code licence does not
license publisher map art. Community coordinate facts follow rule 9 even without a published licence.
Use the link-only form for remaining refusals or unavailable data. Retain source sidecars,
README attribution tables and `Info.References` as required by rule 9.

`ViewTarget(kind, id)` links to an existing `Field` or `Action`; maps may also link to a table `Row`.
`Path` opens enclosing containers in order using `section:<lazyId or title>`, `tab:<title>` and
`row:<key>`. Include all ancestors from the root. Field/action targets use the existing value/action
ID, not the graph item ID. For a Row target, `Id` is the row key and `Path` ends at its parent table;
the browser adds the final row step. IDs are opaque strings, not selectors, URLs or commands.
For example, `new ViewTarget(ViewTargetKind.Field, "skills/dash") { Path = ["section:Character",
"tab:Skills"] }`. Targets in eager tables can clear filters, scroll and focus a virtualised row.
F8 targets work only for already-loaded rows; F9 does not search an unloaded collection by ID.
Keep the actual control/list available and lazy-expandable along that path. If an edit changes the
published map/graph state, return `StructureChanged = true` so the browser rebuilds the presentation.

Hard limits (`SpatialViewLimits`, enforced by Core on root, expanded and queried views): **512**
items/markers per node, **2,048** graph edges, **4,096** UTF-16 units per text property, **256** per
ID and **32** navigation path parts. Coordinates/transforms must be finite, have absolute values
at most `1e12`, and use nonzero scales and nonempty bounds. Split larger worlds/trees into bounded
regional/branch nodes or lazy sections. `SpatialViewValidation.Validate(node)` is also available
to plugins/tests. No changes to existing `ViewNode` constructors or plugin/document interfaces
are required. The complete synthetic example is `tests/SaveToolkit.Core.Tests/Fakes/SpatialPlugin.cs`.

## Field specs (src/SaveToolkit.Sdk/Values)

| Spec | Value (`FieldValue`) | Notes |
| --- | --- | --- |
| `BoolSpec` | bool | |
| `IntegerSpec(min, max)` | Int128 (wire: decimal string) | Presets `IntegerSpec.UInt32` etc. Narrow the range with `with { Min =, Max = }` when the game has real limits. `Format = Hex` for ids/hashes, `Duration` + `UnitsPerSecond` for timers. |
| `FloatSpec` | double | `FloatSpec.Single` for float32 (values are rounded to float32 for you). `Format = FloatFormat.Duration` + `UnitsPerSecond` for timers (defaults to seconds). |
| `ExactNumberSpec` | `FieldValue.ExactNumber(text)` | JSON numeric lexeme, with exact optional `Min`/`Max`, `MaxLength`, and `MaxAbsoluteExponent`. Wire: `{kind:"exactNumber",text:"1.2300e+20"}`; distinct from text. |
| `NullableSpec(inner)` | null or the inner spec's value | Explicit null is `FieldValue.Null`; it never inserts/removes a property or means empty text. |
| `UnionSpec(options)` | `FieldValue.Union(optionId, value)` | Each `UnionOption(id, label, spec)` retains its inner constraints. Wire: `{kind:"union",type:"number",value:{kind:"exactNumber",text:"1e20"}}`. |
| `TextSpec` | string | `MaxLength` in UTF-16 units. Check encoded byte length yourself in `validate`. |
| `EnumSpec(options)` | string (option value) | `AllowCustom` for partially-mapped ids (hashes). |
| `ReferenceSpec(catalogId)` | exact raw id as text | Paged attributed catalogue choice with server eligibility; also supported in planned-action parameters. Read-only fields use the browse-only picker. |
| `FlagsSpec(bits, width)` | integer bitmask | Unnamed bits are preserved. |
| `DateTimeSpec` | ISO-8601 string | `HasTimeZone = false` for console-local clocks. |

Exact numbers follow the [RFC 8259 section 6 grammar](https://www.rfc-editor.org/rfc/rfc8259#section-6):
no whitespace, leading `+`, leading zeros, NaN or Infinity. The original spelling (negative zero,
trailing fractional zeros, exponent case/sign/zeros) survives edits, protocol serialization and undo.
Use `AsExactNumber()` to retrieve it; `AsString()` and `AsDouble()` deliberately reject it. Plugins
must write that lexeme directly with their format serializer, never convert it to `double` or
`decimal`. The browser uses a text input and a tagged string value. Core also accepts a raw JSON
number for an exact-number field and reads its raw text before any numeric conversion; browsers
should always send the tagged form to avoid JavaScript parsing loss.

Default limits are 4,096 UTF-16 characters and an absolute explicit exponent of 1,000,000. Specs
may raise them up to 16,384 and 1,000,000,000 respectively, or narrow them. These limits apply to
the lexeme and explicit exponent, including insignificant zeros; they do not expand the number
into digits. Optional inclusive `Min`/`Max` are JSON numeric strings compared exactly, with no
floating-point conversion. Bounds must fit the hard limits. Invalid limits/bounds reject edits.
`FieldValue.ExactNumber` enforces grammar and hard limits; Core additionally enforces the spec's
limits/range. Defaults/steps/reset remain plugin annotations, validation and actions; the framework
does not silently insert defaults or infer gameplay-safe bounds.

For example, nullable JSON scalars can use:

```csharp
new NullableSpec(new UnionSpec([
    new UnionOption("number", "Number", new ExactNumberSpec()),
    new UnionOption("string", "String", new TextSpec { Multiline = true }),
    new UnionOption("boolean", "Boolean", new BoolSpec()),
]))
```

Union option ids must be stable and unique; incoming edits explicitly name one and are validated
against its inner spec. There is no implicit text-to-number type selection. Integer values inside
a union still use the existing integer-string wire representation: consumers deserializing
`FieldValue` directly must normalize against the known spec, just as for ordinary integer fields.
Wrap a union in `NullableSpec` to allow null independently of its chosen option. Null, empty text
and a missing member remain distinct: publish fields only for present members; missing edit values
are rejected, and insertion/removal remains a structural plugin operation.

The browser offers explicit null/type selectors and remembers each previous non-null/type value
while the control stays mounted. Selecting a previously unused type initializes a scalar value
(numeric bound or zero, empty text, false, first enum option, or an epoch timestamp) and submits
it through normal server/plugin validation. Rejected selections retain the current value.
Enum fields support free entry when `AllowCustom` is true; long fixed lists have a search field,
keep unlisted current values visible, and show the selected option's description.

Value ids are opaque strings you choose. They must be **unique and stable for the lifetime of the
document** (the browser caches values by id, undo replays by id). Path-like ids work well:
`inventory/2227368435/quantity`, `tree/7c416083/7c018857[6]/d18b252d`.

Float duration hints display hours, minutes and seconds to millisecond precision, including a
fractional second and a negative sign when needed. For example, `FloatSpec.Single with {
Format = FloatFormat.Duration, UnitsPerSecond = 1000 }` displays `61250` as `1 min 1.25 s`.
The editor and protocol retain the raw numeric value; formatting never changes saved precision.
Invalid/nonpositive rates fall back to raw display. Publish units only when supported by evidence.

### Read-only inspection actions (F13)

Actions remain mutating by default. Opt into inspection explicitly:

```csharp
new ActionNode("inspect-item", "Inspect item") { Mutates = false }
// InvokeAction returns new ActionOutcome { Message = "...", Warnings = [...] };
```

Core permits these actions when `CanEdit == false` and uses the normal published parameter
validation and revision checks. Inspection runs on a **new document opened from the current
serialized save**, including current edits and the same pinned reference context. Root bindings
and previously expanded lazy sections are rebuilt on the copy. Keep action dispatch independent
of transient UI/query caches; queries and other unsaved presentation state are not replayed.
Results are the existing `Message` and `Warnings`, not changes to a live view.

Core rejects any reported `Changes`, `Revert`, `StructureChanged`, or `Undoable = false`, and
compares exposed field values and serialized file paths/content before and after invocation.
Mutations, exceptions and verification failures discard the copy; live values, bindings, dirty
state, undo/redo and revision stay unchanged. `SupportsPlanning` must be false. Inspection
requires deterministic, lossless `Open`/`Serialize` and snapshots no larger than 256 MiB; an
unverifiable action fails with an inspection error. This is isolation for plugin mistakes, not
a substitute for the plugin purity rules. Never put inspection state in shared mutable objects.

## Document groups (coordinated multi-file transactions)

`Serialize()` already supports multiple `FileOutput`s. Use the optional `IDocumentGroupPlugin`
capability when separately opened character/profile, gameplay/common or primary/backup files need
one owner, one reviewable action and one undo history. Existing single-file plugins need no changes.
No production plugin is opted in automatically. The runnable example is
`tests/SaveToolkit.Core.Tests/Fakes/GroupPlugin.cs` and its composed `GroupDocument` model.

1. **Propose exact members.** `ProposeGroups(anchor)` returns `DocumentGroupProposal`s with a title,
   stable proposal id, explicit `(Role, RelativePath)` members and any missing/unsupported/ambiguous
   partner errors. Include the anchor. Offer alternatives for the user to select; never silently pick
   a matching-looking profile or another generation. Core requires 2–32 unique roles/paths, the same
   title and imported generation, and existing game files (not `sce_sys` metadata). Imported generation
   is archive occurrence identity, not evidence of chronological or gameplay compatibility.
2. **Validate the complete immutable input.** `ValidateGroup(input, references)` checks supported
   title/build/roles, container/serial relationships, capacities and any cross-file invariants. It runs
   before ownership, at opening, before each publication and at export. Return `FieldError`s; malformed
   formats may throw `SaveFormatException`. The same pinned F2 reference context is used throughout.
3. **Open independent models.** `OpenGroup(input, references)` returns `IGroupedSaveDocument`, an
   `ISaveDocument` with `CaptureMemento()`. Compose your member models behind one aggregate view.
   All field/row/action/lazy/collection ids must be unique across the group; role prefixes work well.
   The group session uses one ordinary `DocumentSession` around a staging adapter, so F5 history,
   F6 plans and semantic review, F8 query scopes, assets and reference pickers retain their normal APIs.
   There are no competing writable member sessions.
4. **Capture complete state, not just persisted bytes.** The F5 `DocumentMemento` must include every
   member, stable duplicate identities, allocation counters and any state needed to replay lazy/query
   bindings (including opaque cursors). It must restore into a *fresh* `OpenGroup` instance. Capture
   immutable copies, report retained size honestly, and copy again on restore. Inverse-only tokens
   depending on the original instance cannot implement this stronger group snapshot contract.
5. **Stage and publish together.** Core opens a private candidate from pinned original inputs, restores
   the current whole-group snapshot, rebuilds exposed bindings and executes the request there. Failed
   edits, exceptions, rejected plans, invalid outputs and failed restores discard the candidate. They
   never mutate the live group. A successful mutation publishes one candidate with one whole-group
   undo token. Every group mutation/undo/redo is a structural revision boundary: both members' pages,
   lazy views, explicit edits and F6 previews become stale together. Existing single-file scalar/page
   `_structuralRevision` behavior is unchanged. A failed F5 history replay may conservatively invalidate
   the view while preserving the live bytes/history.
6. **Serialize every owned member exactly once.** Paths outside the selection, duplicates and omitted
   members are rejected. Core copies output bytes, validates the combined candidate set, and reopens
   **each** output with `IGamePlugin.Open(file, references)` in its own original path/role. That method
   must therefore understand each selected role, not just the character/anchor. Only a fully verified
   candidate is published. Workspace export repeats validation and overlap checks before returning its
   complete plan/ZIP; imported blobs are never overwritten. ZIP HTTP responses are buffered before send.
7. **Own backups explicitly.** Group-owned outputs never trigger automatic backup mirroring, even when
   workspace export requests it. Include backups as named members if they must change; their roles and
   relationship must pass your validation. Independent open backup sessions are also protected from
   ordinary export mirroring. Distinct generations are never treated as backups of one another.

Opening reserves all selected paths under the workspace ownership gate and checks their imported
SHA-256 fingerprints. Another group, any open member session, or a dirty session already emitting a
selected path is a conflict. Users must explicitly close conflicts. Confirmation may explicitly replace
the selected anchor's **clean** session; the clean check and disposal are atomic with respect to edits.
Fingerprints are checked again before staging/restoring and export. A changed imported blob requires
reimport. Future opaque outputs from an independent document that overlap a group are rejected at export.

The framework retains at most 128 proposal offers, 32 alternatives per request, and 256 distinct queried
pages between group publications, to bound candidate rebinding. A publication releases remembered query
pages; reopening a clean group also releases them. F5's existing history/snapshot budgets still apply;
groups reject a mutation before publication if either its undo or redo snapshot would exceed the
single-token budget, preserving the undo/redo promise. Older entries may still be evicted under the
normal F5 limits. `Workspace.OpenGroup` accepts optional `DocumentSessionOptions` for host configuration.
F6's query-row and preview budgets remain unchanged. Keep groups as small as the proven dependency requires.

API flow (all paths under `/api/workspaces/{workspaceId}`):

| Route | Behavior |
| --- | --- |
| `GET /files/{fileId}/group-proposals?pluginId=...` | Non-owning proposals with opaque confirmation tokens, role/path/file/fingerprint metadata and errors. |
| `POST /document-groups` | `{ proposalToken, releaseCleanDocumentId? }`; confirms the exact selection and returns an ordinary document DTO with `groupMembers`. Tokens are workspace-local and single-use after success. |
| Existing `/documents/{id}` routes | Group view, edits, plan/apply, changes/revert, queries, undo/redo and close use one session id/revision. `groupMembers` also appears on workspace document summaries; every owned file points at that session. |
| `GET /documents/{id}/download` | Group sessions return 409 directing the caller to workspace export, which includes all members together. |

The editor's **Choose related files** control lists the exact proposed paths before **Open group**.
It reports partner/ownership errors, shows all owned members afterwards, and routes rail clicks on any
member back to the coordinated editor. Use normal F6 planned actions for transfers and reconciliation;
there is no second output or transaction API to bypass review. Console/game validation is still the
plugin author's responsibility.

## Structural edits (add / remove / reorder)

Actions that add, remove, reorder or replace records can't be undone by replaying `ValueChange`s.
Make them undoable with a **revert token** instead:

1. **Give records stable logical ids.** Derive `TableRow.Key`, value ids and lazy ids from the record's
   identity (stored id, GUID, handle, or a counter you keep in the model), **never from its position**.
   `inventory/7/quantity` must still mean record 7 after record 3 was removed. If the format has no
   ids, assign them when parsing and keep the counter in the model, so the snapshot restores it too.
2. **Capture state before mutating.** With `BoundSaveDocument`, override `CaptureSnapshot()` /
   `LoadSnapshot()`. Then wrap the action in `Structural(...)`:

   ```csharp
   protected override DocumentMemento CaptureSnapshot()
   {
       var bytes = Encode();                          // or an immutable copy of your model
       return DocumentMemento.Snapshot(bytes, bytes.Length);
   }

   protected override void LoadSnapshot(DocumentMemento s) => Parse(((SnapshotMemento<byte[]>)s).State);

   public override ActionOutcome InvokeAction(string id, IReadOnlyDictionary<string, FieldValue> p) => id switch
   {
       "add" => Structural(() => AddItem(p["item"].AsString()), "Item added."),
       _ => base.InvokeAction(id, p),
   };
   ```

   `Structural` captures a snapshot, runs the mutation and returns
   `ActionOutcome { StructureChanged = true, Revert = snapshot }`. If the mutation throws, it loads the
   snapshot back first, so a failed precondition changes nothing. Still validate before mutating where
   you can. Afterwards it drops all bindings, and the framework rebuilds the view, which registers the live ones.
3. **Restore exactly.** `RestoreMemento` (implemented by `BoundSaveDocument` on top of the two hooks)
   must bring back the same bytes **and** the same ids. Core replays later value edits by id after a
   structural undo/redo. Undoing everything must serialise to the original bytes exactly; test it.
   Tokens are reused (undo → redo → undo), so `LoadSnapshot` must **copy** from the token and never
   adopt its state object as the live model.
4. **Report an honest size.** `SizeBytes` counts against the session's history budget
   (`DocumentSessionOptions.MaxSnapshotBytes`, 256 MiB by default). Core evicts the oldest history
   first. A single change larger than the budget is applied without undo, and the user is told.
5. **Optional optimisation: inverse operations.** For very large saves, return your own
   `DocumentMemento` subclass describing the inverse ("remove record 7"). Override
   `RestoreMemento` to apply it and return the opposite token ("re-insert record 7 with these bytes").
   Core treats both kinds the same way.

What the framework does for you:

- One undo/redo history mixes value edits and structural steps. It can group several steps into one
  undo unit (`DocumentSession.RunGroup`).
- After every structural change, Core rebuilds its view index and re-expands lazy sections that were
  open. It also retires ids that vanished and bumps `DocumentState.Revision`.
- Requests that use a retired id or an old revision are rejected as **stale** (HTTP 409). The browser
  then refetches the view.

### Inventory tables and empty slots (F14)

Use **one table per inventory**. Do not split a fixed slot array into “Slots 0–127”, “Slots 128–255”
sections or linked eager pages. For more than about **100 rows**, use an F8 `QueryableTableNode` so
the browser can search and sort the entire inventory while fetching bounded pages.

Mark a confirmed vacant slot with `TableRow.IsEmpty = true`, and set
`EmptyRows = new EmptyRowPresentation("name")` on its `TableNode`, or on **both** the
`QueryableCollection` and its `QueryableTableNode`. `LabelColumnKey` identifies the compact item-name
cell to replace with “Empty slot”; its original node and all bindings remain available in row details.
`PluralLabel` defaults to “empty slots” and `HideByDefault` defaults to `true`. Tables without this
metadata keep their existing visibility. For example:

```csharp
var row = new TableRow($"inventory/{slot.Identity}", cells) { IsEmpty = slot.IsConfirmedEmpty };
var table = new TableNode(columns, [row])
{
    EmptyRows = new EmptyRowPresentation("name") { PluralLabel = "empty slots" },
};
// For F8, publish the same EmptyRows settings on collection metadata and QueryableTableNode.
```

Keep every underlying slot and its original **slot identity as the row key**, including vacant slots.
Do not remove, compact or reorder saved records when hiding rows. Editing, undo and F6 scopes still
address those identities. If an edit changes a slot's emptiness, return `StructureChanged = true` so
the marker and counts are rebuilt. The browser hides empty rows by default, offers
“N empty slots hidden · Show”, and dims them when shown. Hiding clears empty-slot selections;
select-all applies only to the current visibility. F8 visibility changes reset paging and selection.

Do not render known empty sentinels such as `0xFFFFFFFF`, `-1` or `0` as unknown items. Confirm the
format's empty-slot rule before flagging: an unmapped ID or zero quantity alone does **not** prove
emptiness. Keep the raw ID/quantity for inspection, no-op serialization and undo.

F8 providers must resolve `query.IncludeEmpty ?? !metadata.EmptyRows.HideByDefault` when called
directly (Core resolves it before calling `Query`). Apply text/column filters to the complete model,
count the matching empty slots, exclude them when `IncludeEmpty` is false, then sort and page.
`TotalCount` includes every slot; `FilteredCount` counts all matching **visible** rows before paging;
`HiddenEmptyCount` counts matching empty rows excluded **only** by visibility, also before paging.
Return zero hidden count when including empties. Do not filter an already-built page: that breaks
page offsets and F6 “select all matching”. Core rejects hidden queries returning flagged empty rows
and inconsistent hidden counts. The F6 scope retains `IncludeEmpty` and row-key exclusions across
all pages. SaveDiff explicitly requests `IncludeEmpty = true`; presentation must never hide data
from a comparison. See `EmptySlotTests` and `EmptySlotApiTests` for a synthetic complete query path.

### Paged/queryable collections

Large collections implement the optional `IQueryableDocument` interface from
`SaveToolkit.Sdk.Collections` and publish a `QueryableTableNode` from `BuildView()`. The interface is
deliberately read-only at the collection boundary: returned rows still contain ordinary `FieldNode`,
`TextNode`, `ImageNode` and `ActionNode` cells, so the normal controls, validation and edit routing apply.

```csharp
public sealed class InventoryDocument : BoundSaveDocument, IQueryableDocument
{
    public IReadOnlyList<QueryableCollection> Collections =>
        [new("inventory", "Inventory", [new("id", "Id", IntegerSpec.UInt32), new("name", "Name", new TextSpec())], items.Count)];

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) =>
        BuildInventoryPage(collectionId, query); // filter and sort the complete model, then take the page
}
```

Collection ids, column keys and row keys are opaque strings and must be stable for the document's
lifetime. Never use a page offset as a row key. A query has optional `Text`, exact `Filters`,
`SortColumn`/`Descending`, and either an `Offset` or an opaque `Cursor`; the framework caps page size at
200 rows, search text at 256 characters, filters at 32 entries of 256 characters, and cursors at 4 KiB.
The plugin must return no more than the requested limit, preserve stable keys across pages, and return a
deterministic tie-break (usually the row key) when sorting. `TotalCount` is the complete collection size;
`FilteredCount` is the count after the query. Unknown columns, duplicate/empty row keys, and malformed
pages are rejected. The API also caps one JSON page at 2 MiB.

The `QueryableTableNode` contains column types and the unfiltered total so the browser can render a
bounded shell immediately. Core registers bindings only for rows returned by a page. An edit from a
page sends that page's `revision`; after a structural edit, old pages receive the same stale 409 used by
ordinary views and must be refetched.

The web selection context exposes `queryScope` when the user chooses “select all filtered rows”. It is a
serialisable object containing `collectionId`, the normalized query (`q`, `filters`, `sort`,
`descending`, `includeEmpty`, with `offset` reset), the F5 `revision`, `totalCount`, and `excludedRowKeys`. Bulk action
preview/apply consumers (including F6) must send this scope rather than pretending that loaded rows are
the whole result; exclusions are explicit when the user unchecks a row.
- Actions that return `Undoable = false` still work: they clear history explicitly, as before.

**References between records:** until there is a dedicated node, expose a reference as an `EnumSpec`
field whose option values are the target records' stable keys. Labels are human names. The options are
rebuilt with the view after every structural change, so they never point at removed records.
`tests/SaveToolkit.Core.Tests/Fakes/RosterPlugin.cs` is a complete example with add, remove and
move actions.

## Planned actions and semantic review

Existing actions keep their current behavior. For a bulk action or preset that needs review,
implement the optional `IActionPlanningDocument` interface and set
`ActionNode.SupportsPlanning = true`. Core rejects direct invocation of opted-in actions, so a
caller cannot bypass the preview. No change to `IGamePlugin.Open` or the value-spec vocabulary is needed.

The two methods are:

- `PlanAction(actionId, parameters, scope)` returns an `ActionPlan` **without mutating anything**.
  Include every named direct and derived effect in `PlannedChange`, and include skipped row keys
  with reasons, warnings and validation errors. `OldExists` / `NewExists` distinguish an absent
  field from a real null value. Labels must reflect what is actually known about the field.
- `ApplyActionPlan(actionId, parameters, scope, plan)` returns an `EditOutcome`. It must be atomic:
  an error or exception leaves the document unchanged. Return every scalar change, including
  derived values, or a structural `Revert` memento as described above. Structural planned actions
  must support mementos; this interface has no non-undoable opt-out.

`ActionScope.RowKeys == null` with no `QueryScope` means document scope. An **empty list means no selected rows**;
never reinterpret it as all rows. Keys are the stable `TableRow.Key` values from F3 selection,
not visible positions. A table-cell action takes the enclosing table's selection, including
selected rows hidden by filters. Outside a table, the dialog explicitly shows document scope.
Validate that every requested key belongs to the intended collection and is eligible. Do not
retain or mutate the supplied parameters, scope or plan.

For a server-backed table's “select all filtered rows”, the preview sends `ActionScope.QueryScope`
instead of loaded row keys. `ActionQueryScope` carries `CollectionId`, the existing F8
`QueryableCollectionQuery`, the selection's `Revision`, and `ExcludedRowKeys`. Clients send either
explicit row keys or a query scope, never both. The browser maps its `q` / `sort` query properties
to the SDK wire names `text` / `sortColumn`; its estimated count is not trusted.

Core normalizes offset/cursor back to the beginning, copies filters/exclusions, and resolves every
matching page through `QueryCollection` under the session lock. Plugins receive **both** the
normalized query metadata and the resolved `RowKeys`; use those keys for the operation and validate
the collection ID. The preview's `Scope.RowKeys.Count` is the server-resolved selection count,
including unloaded pages and excluding deselected keys. Exclusions outside the result are ignored.
Zero matches or fully excluded results produce an empty key list, never document scope.

`DocumentSessionOptions.MaxQueryActionRows` defaults to 5,000 and must be positive. The limit applies
to matching rows **before** exclusions, so excluding rows cannot turn a huge scan into a permitted
operation. Oversized queries are rejected with a clear planning error; narrow the query to proceed.
Normal F8 filter/search/page limits still apply. Repeated keys across pages, inconsistent counts,
non-progressing pages and malformed continuations are rejected instead of planning a partial result.
The scope's revision must equal the current document revision even if the outer request has a newer
revision; stale scopes return the existing HTTP 409 response. Apply resolves the query again and
requires the same ordered keys before revalidating effects and recording one undo group.

For a value-only action, the apply implementation can pass its planned direct changes through
`ApplyEdits`; the document's normal derived-value logic still runs:

```csharp
public EditOutcome ApplyActionPlan(string actionId,
    IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) =>
    ApplyEdits(plan.Changes.Where(c => !c.Derived)
        .Select(c => new ValueEdit(c.ValueId, c.NewValue)).ToArray());
```

Use this only when every direct effect is an existing scalar; additions/removals require the
structural path. The test fixture `tests/SaveToolkit.Core.Tests/Fakes/PlanningPlugin.cs` exercises
both kinds of action, selected-row scope and a derived total.

Core retains only the latest preview per document. The response includes an opaque `PlanId` and
`DocumentState.Revision`; apply sends both, never client-supplied consequences. Any intervening
scalar edit, action, structural change, undo or redo invalidates the revision. A stale apply
returns HTTP 409 with `stale: true` and `code: "stale"`. Tokens are single-use, including failed
applies, and previews in another tab replace the previous token. Apply reruns `PlanAction` under
the document lock and requires the same effects, exclusions, warnings and errors. Planning must
therefore be deterministic for the same request and document. A successful apply runs inside
`DocumentSession.RunGroup`, giving one undo entry (subject to the existing undo-memory budget).
Planning works on read-only documents, but applying does not. The retained request plus preview
is limited to 4 MiB and explicit selection to 5,000 distinct keys; query scopes use the configurable
matching-row limit above. Ask users to narrow larger operations.
Review lists page at 100 entries to keep the browser DOM bounded.

The API adds `POST /actions/plan`, `POST /actions/apply`, `GET /changes` and
`POST /changes/revert` below each document route. Revert requires the change log's revision.

The Changes panel compares the current semantic field values with a separate original baseline,
not with the top undo entry. Core captures that baseline before the first mutation (or on the
first changes query), including lazy views and derived read-only fields. The baseline walk uses
a separate disposable document opened from the original file, so registering lazy aliases cannot
change the live document's legacy edit/action behavior. The optional `DocumentSession` constructor
argument `openOriginal` can supply a factory that preserves reference-pack or other opening context;
it must return a new document with the same initial identities. The default calls `plugin.Open(file)`.
The live current-view walk occurs only when review is requested, and registers the reviewed fields
like normal view expansion. Scalar changes reported for unexposed fields also retain their first
old value where original identity can be proven; these use their raw ID and have no jump target until exposed. The baseline survives undo-history
eviction. `DocumentSessionOptions.MaxOriginalBytes` defaults to 16 MiB of estimated retained
field data; walks are also bounded to 100,000 nodes/rows/tabs and depth 128. Exceeding a bound,
failure to open the original, failing lazy expansion, or unmatchable internal identities yields
`Complete = false` with a visible warning. Missing coverage is never reported as a proven addition
or removal. The current walker does not page `QueryableTableNode` collections (see
[SDK-01](SDK_REQUESTS.md#prioritized-inventory)); `Complete = true` describes that walk, not coverage
of every F8 field. This is a field-level review; it does not describe binary differences or record
order. Use structural undo for reorder-only changes.

Jump opens parent sections/tabs and lazy views, clears table filters, scrolls virtual rows and
focuses the field. Individual revert restores an **existing editable field** through normal
validation, so dependent values are recomputed and the revert itself can be undone. Added,
removed, read-only and unexposed fields require undo rather than guessed inverse edits.

## Shared SDK helpers

These additive helpers remove common mechanics without changing document contracts. Existing plugins
can adopt them independently; retain game-specific validation, evidence labels and byte coverage.

- **`Collections.InMemoryCollection<T>`** queries a complete, bounded in-memory model from a source
  factory. Supply stable native keys, one `InMemoryColumn<T>` per metadata column, and a row renderer.
  Each column explicitly opts into `SearchText`, `FilterText` and a typed `IComparer<T>`; decorative
  columns can opt into none. Compare native integers/nullable values directly to retain precision and
  your chosen null ordering. Search defaults to ordinal case-insensitive substring matching; filters
  default to ordinal case-insensitive equality. Options control normalization (no trimming by default),
  matching, extra search text and default ordering. Equal sort values always use ascending ordinal
  native keys, including descending sorts. Without a comparer the default is ordinal key order;
  `ReverseDefaultOrderWhenDescending` controls reversal of the default order. Paging clamps by default;
  set `ClampPaging = false` to reject invalid bounds. Cursors and unsupported column operations fail.
  `IsEmpty` is required when metadata declares `EmptyRows`: query matching precedes empty-row exclusion,
  counting and paging, and rendered rows retain their native keys/cells with the correct `IsEmpty` flag.
  Only the selected page invokes the renderer or registers field bindings. `metadata.ToTableNode()`
  copies descriptions and empty-row settings into the view shell. Keep SQL-backed or deliberately
  bounded partial-model queries on their existing engines.
- **`ActionPlans.ContentEquals(reviewed, fresh)`** compares ordered changes, skipped rows, warnings and
  errors, including exact values, labels, existence and derived flags. `Revalidate(reviewed, replan,
  mismatchError)` returns an `ActionPlanValidation` containing the fresh plan and either fresh errors
  or your supplied mismatch diagnostic. Optional `ApplyScalar` runs that check, `prepareBindings`,
  `selectEdits` and atomic `applyEdits`, with an explicit `ActionPlanWarningPolicy` (`Merge`, `UsePlan`
  or `PreserveApplication`). The plugin chooses actual edits: linked derived fields may be real writes,
  while other derived effects are preview-only. Bind unopened fields before applying so undo can
  replay them. Binding/selection must not change saved state; the existing apply implementation owns
  rollback and complete undo reporting. Null document scope and explicit empty selections remain
  distinct in the plugin's replan closure. These helpers do not replace Core's revision, single-use
  token or query-selection gates, nor implement structural transactions.
- **`Binary.BinaryRange.Slice(data, offset, length, context)`** checks spans using subtraction to avoid
  overflowing untrusted offsets/lengths. **`BoundedBinaryCursor`** is a stack-only span reader with
  absolute `Position`, `Start`, `End`, `Remaining`, `Take`, little-endian primitive methods such as
  `ReadInt32LE`, and bounded `ReadCount`. `Subrange(length)` consumes a parent range and returns a
  child that cannot seek/read outside it. Invalid input throws contextual `SaveFormatException`.
- **`Binary.FStringCodec`** reads signed-length UTF-8 / little-endian UTF-16 FStrings through
  `Read(ref cursor, limits)` or a `BinaryReader` adapter; `ByteLength` and `Decode` also work separately.
  `FStringLimits(MaxBytes, MaxUtf16CodeUnits)` counts the serialized body **including the terminator**,
  excluding the four-byte prefix. `MaxUtf8Bytes` can separately cap positive-length strings. For
  AtomicHeart-style N-unit bounds use `new(2 * N, N) { MaxUtf8Bytes = N }`; for Gearbox-style byte
  bounds use `new(N, N / 2)`. UTF-16 limits count code units, so supplementary characters consume two.
  Both encodings are strict; truncated bodies, missing terminators and overflowing lengths fail.
  `Encode(value, wide, limits)` uses the caller's explicit width and rejects embedded NUL/unpaired
  surrogates. Zero-length input and terminated empty strings both decode to empty text; retain original
  bytes/width for byte-identical no-op serialization. Layouts, alignment and format varints stay local.
- **`References.ReferenceCatalogReader.ReadBounded(catalog, query, maxRecords)`** reads all matches
  from the query's initial offset, retaining text/category/sort and using its page size (1–200).
  A missing catalog yields an empty list. The result is complete or throws `InvalidOperationException`
  on budget exhaustion, duplicate/empty keys, inconsistent totals/offsets/counts or a page making no
  progress. Short pages advance by actual returned count. Budgets use filtered query totals rather
  than unfiltered catalog size. Use this when building an index; keep direct `Find`/`Search` lookups.
- **`References.ReferenceViews.Icon(context, record, label, fallback)`** resolves local image bytes
  and image provenance, registers the image dataset in Sources and includes separate record/image
  attribution in the alt text. Missing records/images/provenance use the supplied fallback (or a text
  fallback). Supply evidence caveats such as whole-item-versus-part appearance in `label`.
  The additive five-argument overload accepts **`ReferenceIconOptions`**; pass `label` and `fallback`
  explicitly (null is allowed). Null/default options preserve the original overload's output.
  `ExactAltText` overrides the label and composed attribution verbatim, including empty text and
  whitespace; the caller must retain all required publisher/source notices. Null keeps composed alt text.
  `Availability` defaults to `RequireBytesAndProvenance`; `RequireProvenance` preserves an image when
  provenance exists but bytes are unavailable (BG3 compatibility). `AssetIdOnly` preserves an image for
  any non-null asset ID even without bytes/provenance (Tales of Arise compatibility with exact alt text).
  Composed alt text still needs provenance under every policy; otherwise the helper uses the fallback.
  Every policy requests provenance and resolves permitted assets through `GetAsset` to register available
  image datasets in Sources, including with exact alt text. Missing records/asset IDs always use the
  supplied fallback or `new TextNode("No reference icon")`. For example:

  ```csharp
  // BG3: retain composed, separate record/image attribution, including missing-byte images.
  ReferenceViews.Icon(context, record, label: null, fallback: null,
      options: new() { Availability = ReferenceIconAvailability.RequireProvenance });

  // Tales of Arise: retain exact role/publisher text and the historical em-dash fallback.
  ReferenceViews.Icon(context, record, label: null, fallback: new TextNode("—"), options: new()
  {
      Availability = ReferenceIconAvailability.AssetIdOnly,
      ExactAltText = record is null ? null
          : $"{record.Name}: {record.Attributes.GetValueOrDefault("iconRole") ?? "game icon"}. {MediaLicense}",
  });
  ```

  `MediaLicense` above is the caller's existing publisher notice (`AriseAssets.MediaLicense`).
  **`ReferenceInspection.Create`** creates an attributed `Mutates = false` action with one `ReferenceSpec`
  parameter; absent/empty catalogs yield null. Pass an explicit initial key for a fixed record, and
  `fixedRecord: true` for a read-only picker. `ResolveSelection` accepts exactly one text parameter and
  checks pinned catalog membership; pass `expectedKey` to enforce fixed-record identity for direct
  callers too. The plugin supplies action descriptions, result messages and warnings; reference names
  do not establish ownership, saved upgrades or writer confidence.
- **`Binary.Crc32Ieee.Compute(bytes)`** returns finalized CRC-32/ISO-HDLC with reflected polynomial
  `0xEDB88320`, all-one initialization and final complement. An instance supports `Append`,
  non-consuming `GetCurrentHash` and `Reset` for discontiguous coverage without concatenation. Keep
  covered ranges, sentinels, 16-bit folding and storage byte order in the plugin. CRC32C uses a different
  polynomial and must keep its existing implementation; a matching probe alone does not prove integrity.

The extraction APIs were checked against Cyberpunk2077/FinalFantasy7 collection queries,
AstroPlayroom/TalesOfArise/EldenRing action application, AtomicHeart/UnrealGvas/Gearbox string readers,
BaldursGate3/Cyberpunk2077 binary ranges, EldenRing/FinalFantasy7 catalog traversal,
BaldursGate3/Gearbox icons, AtomicHeart/TalesOfArise inspection, and FinalFantasy16/Gearbox/Ragnarok
IEEE CRC implementations. Migration should retain each caller's policies through the explicit adapters;
the helper introduction does not migrate plugins.

## Hard rules

1. **Depend on `SaveToolkit.Sdk`, optionally `SaveToolkit.Codecs`.** Never reference Core/Api. Prefer
   established libraries over hand-written implementations. NuGet packages are fine, including ones with
   native components (e.g. `Microsoft.Data.Sqlite`) and GPL-compatible licences. Add the version to
   `Directory.Packages.props`. For Oodle/Kraken and SQLite, use the shared bounded wrappers in
   [`SaveToolkit.Codecs`](../src/SaveToolkit.Codecs/README.md); plugins still do no file or network I/O.
   Note third-party code and licences in the plugin README.
2. **Never lose data.** Preserve unknown fields, padding, trailing data and metadata you don't understand.
   Prefer in-place, fixed-size patching over re-serialising whole object graphs. If you must
   re-serialise, prove byte-identical no-op round trips on every sample.
3. **Fix integrity.** Recompute every checksum/hash/signature you can, re-compress/re-encrypt, then
   re-parse your own output inside `Serialize()` and throw if it does not validate.
   (The framework also re-opens the output before exporting, but fail early with a precise message.)
4. **Be honest about meaning.** Label fields with `Confidence`. Unknown semantics → `Unknown` and a raw
   name like the hash or offset. Never invent item names; use dictionaries only from sources you cite.
5. **Bound the view.** No eager view with 100k nodes. Use lazy sections for raw trees and large lists.
6. **Stateless plugin, isolated document.** The plugin instance is shared across sessions and threads;
   keep all state in the document. No static mutable state, no file system, no network.
7. **Maturity.** `ReadOnly` until the conformance suite passes with edits; then `Experimental`.
   Only a human sets `Stable` after a successful in-game test.
8. **Reference data** (hash → name tables, item lists, descriptions, icons) comes from embedded
   resources or host-imported [reference packs](#reference-packs), so provenance is retained and plugins do no I/O.
9. **Attribute every reference at every place it shows up.** You may embed data from wikis, datamines and
   community tools, but each dataset needs full attribution in each of these places:
   - **Next to the data:** a header in the resource file, or a `*.source.md` sidecar. It gives the source
     URL, author or project, published licence (with link) or owner-decision notice when none is
     published, retrieval date, and what you changed or extracted.
   - **Plugin README:** a `## Sources and attribution` table with one row per dataset or code port.
   - **`IGamePlugin.Info.References`:** the upstream URLs.
   - **The UI:** the provenance or attribution text of every name, description or icon you show. Put it in
     the field `Description` (`ReferenceRecord.AttributionText`). Data from a reference pack is also listed
     in the document's **Sources** panel automatically.

   **Owner licence policy:** this repository is **GPL-3.0-or-later** (see [LICENSE](../LICENSE)).
   MIT, BSD, Apache-2.0, LGPL, GPL-2.0-or-later, GPL-3.0 and CC BY / CC BY-SA sources are usable
   with attribution and their required notices/terms. Do not refuse these merely because they are
   copyleft or require share-alike. CC BY-SA adaptations retain their own licence, attribution and
   modification notice; code ports retain upstream notices. Record modifications in
   [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES) as well as the plugin README.

   **Owner decision, 2026-10-28: no licence is not a blocker.** Import data and facts from unlicensed
   community sources, including ID tables, names, offsets, hash tables and format layouts, with full
   attribution at every place listed above. Record the licence field exactly as
   `No licence published; reused with attribution per owner decision 2026-10-28`.
   This also applies to community sources previously left external solely for an all-rights-reserved
   notice. Reimplement code from unlicensed repositories from its documented behaviour and credit
   the source; do not copy the implementation. Record the source/revision and implementation approach.

   **Owner decision, 2026-10-29: "all rights reserved" and game assets are not blockers either.**
   Community or commercial guide sites with an all-rights-reserved notice (e.g. Guides4Gamers) and
   publisher game assets (art, icons, screenshots, text extracted from the game) may be used.
   **Always name and attribute them**: the source, the rights holder (publisher/site) and the
   revision or retrieval date, at every place listed above. Use the licence field
   `Rights reserved by <holder>; used with attribution per owner decision 2026-10-29`.
   Keep embedded binary assets small: prefer compact formats, size-bound packs, and loading once.

   The only remaining licence-based refusal reasons are **GPL-2.0-only code and NC or
   no-redistribution terms** (e.g. a licence that explicitly forbids bundling or redistribution).
   Missing captures, an unavailable build-matched pack or unproven game semantics are separate
   evidence/implementation gaps, not licence incompatibility.

   **Every licence-based refusal must be written up** in the plugin README's `## Licence exclusions`
   table **and** [docs/LICENCE_EXCLUSIONS.md](LICENCE_EXCLUSIONS.md). Use one row per source with:
   source URL and revision; exact licence found (or `none found`); refused material/reason; what was
   done instead; and what would unblock reuse. The central table also names the plugin. Record
   link-only and research-only decisions when licence restrictions caused them. If no specific
   source or revision was identified, say so; do not invent a licence or a rights-holder decision.
   Keep previously refused sources in the register with their current status. Mark no-licence-only
   refusals `Unblocked by owner decision 2026-10-28`; track pending imports/reimplementations separately
   from completed work. External source links remain useful for attribution, remaining refusals and
   unavailable data; they are not a substitute for importing usable community facts solely because
   no licence was published.

## Catalog-valued fields and searchable pickers

Use `new ReferenceSpec("items")` for a catalog-backed choice instead of embedding a large `EnumSpec`.
The `FieldNode.Value` is the **exact raw id as text** (not a JavaScript number); convert it to the save's
native representation only inside your getter/setter. Resolve the catalog with `references.Catalog`
when opening, including the game's build when known, so host lookups use the same pinned dataset.

```csharp
var spec = new ReferenceSpec("items")
{
    AllowCustom = false,
    Assess = record => record.Category == "weapon" && !canEquipWeapons
        ? new ReferenceEligibility { Eligible = false, Reasons = ["This slot accepts consumables only."] }
        : new ReferenceEligibility(),
};
```

`Assess` is optional server-only plugin policy (`JsonIgnore`), called under the document session lock.
It must be cheap, deterministic, and perform no I/O. `Available` describes availability in this
build/save, `Eligible` describes suitability for the field, and `Reasons` explains restrictions.
The host publishes the results and rechecks them on edits and action parameters. `AllowCustom`
permits unknown ids; it does not bypass restrictions on known records. Plugins still validate native
id ranges and other save invariants. Mark changes `StructureChanged` when replacement changes field
specs, descriptions or other view metadata, so undo/redo rebuilds the view too.

An existing unknown or ineligible id is preserved, displayed as its raw text, and may be replaced.
It is never silently converted to the first candidate. Unknown incoming ids are rejected unless
`AllowCustom` is enabled; a missing pack therefore allows replacements only with that opt-in.
Undo can restore an original unknown or ineligible value without treating it as a new selection.

The browser shares one picker between field rows, table cells and action parameters. It supports
name/id search, categories, declared sort keys/direction, pages, arrow/Home/End navigation, Enter to choose, Escape/Cancel to
preserve the current value, exact raw-id preview, and catalog/icon attribution in the detail preview.
Labels, descriptions, categories, asset ids and provenance travel as `ReferenceCandidate` responses,
keeping the spec/view small. Supply the current record's `AttributionText` as the field description
when displaying catalog names elsewhere (rule 9).

Read-only fields and read-only documents open the same picker in **browse-only** mode. Search,
filters, sorting, paging, keyboard detail navigation, raw-ID preview and attribution remain
available, but there is no selection/commit button and Enter cannot write. Closing restores
focus to the field trigger; the saved raw ID remains unchanged.

Per-document field-aware API (only previously published fields/parameters are queryable):

- `GET .../reference-fields/candidates?valueId=...&q=...&category=...&sortBy=Name&descending=false&offset=0&limit=30&revision=0`
- `GET .../reference-fields/record?valueId=...&rawId=...&revision=0`

For action parameters also pass `actionId`. Paging clamps offset to zero and limit to 1–200.
Candidate pages include the view revision. Pass it with subsequent queries/edits; stale queries return
HTTP 409 with `code: "stale"`. Exact lookup returns unknown ids with no label/dataset and an explicit
eligibility reason. F2's catalog-wide endpoints remain unchanged. The attributed embedded test pack
and `tests/SaveToolkit.Core.Tests/Fakes/ItemsPlugin.cs` demonstrate the complete contract.

The shipped F13 scope is read-only inspection, browse-only reference pickers, catalogue query
filter/sort and float duration hints. Storage-only `RegisterField` is also shipped; publishing
host field metadata independently of rendered pages remains a separate proposal. See the
[partially delivered field-spec design](plans/F13-FIELD-SPEC-PUBLISHING.md). Unknown action-reported
IDs already replay during undo, but that alone does not establish primary bindings, alias policy,
complete semantic review or safe unloaded-page preflight. Retain plugin guards until that
host metadata capability is implemented.

## Reference packs

A reference pack is a folder (or zip) of names, categories, limits, descriptions and icons, with
attribution for each dataset. The framework loads, validates and caches packs. When a document opens,
the packs that match the plugin and title are **pinned** to it. The plugin reads them through
`IReferenceContext` and does no I/O. Packs come from two places, in the same format:

- **Embedded:** shipped inside the plugin assembly (see below).
- **User:** imported as a zip through `POST /api/reference-packs` or the "Import reference pack" button
  in the Sources panel. They are stored under `Toolkit:PackRoot`, which defaults to
  `%LOCALAPPDATA%/SaveToolkit/reference-packs`. They are never part of a save export.

A pack imported later only affects documents opened after the import. Re-open a document to pick it up.

### Layout and manifest

```
ReferencePacks/pragmata.names.en/
  pack.json
  items.json
  icons/1001.png
```

`pack.json` (unknown properties are rejected, so typos such as `licence` fail loudly):

```json
{
  "schema": 1,
  "id": "pragmata.names.en",
  "version": "1.0.0",
  "title": "Pragmata item names (English)",
  "plugins": ["pragmata"],
  "titleIds": ["PPSA02530"],
  "gameBuilds": [],
  "locale": "en",
  "datasets": [
    {
      "id": "items",
      "kind": "records",
      "title": "Item names",
      "files": [{ "path": "items.json", "sha256": "<64 hex digits>" }],
      "attribution": {
        "sourceUrl": "https://example.org/wiki/Items",
        "author": "Example wiki contributors",
        "license": "CC-BY-SA-4.0",
        "licenseUrl": "https://creativecommons.org/licenses/by-sa/4.0/",
        "retrieved": "2026-10-28",
        "modifications": "Extracted id/name columns; trimmed whitespace."
      }
    },
    {
      "id": "icons",
      "kind": "assets",
      "files": [{ "path": "icons/1001.png", "sha256": "<64 hex digits>" }],
      "attribution": { "...": "same six fields, all required" }
    }
  ]
}
```

| Field | Rules |
| --- | --- |
| `id` | Lower-case `a-z0-9._-`, max 64, unique across all packs. Ids of embedded packs cannot be reused by user packs. |
| `plugins` | Plugin ids that may use the pack (at least one). |
| `titleIds` | Titles the pack applies to; empty means every title the plugin opens. |
| `gameBuilds` | Builds the data was made for; empty means build-agnostic. |
| `locale` | BCP-47 (`en`, `de-DE`), or omit for language-neutral data. |
| dataset `id` | Lower-case `a-z0-9_-`. For `records` datasets this is the **catalog id** the plugin asks for. |
| `files[].path` | Relative, forward slashes, no `..`, no `\`, no `:`, max 200 characters. |
| `files[].sha256` | SHA-256 of the file. Records files are hashed **after converting CRLF to LF**, so git's `autocrlf` cannot break them. Images are hashed as stored. A mismatch rejects the whole pack, and the error shows the actual hash. |
| `attribution` | Required for every dataset. `sourceUrl` and `licenseUrl` must be http(s). `retrieved` is `yyyy-MM-dd`. `modifications` is `"None"` when the data is used verbatim. |

Records files (`kind: "records"`, JSON):

```json
{ "records": [
  { "id": "1001", "name": "Potion", "category": "consumable", "limit": 99,
    "description": "Restores a little health.", "assetId": "icons/1001.png",
    "source": "https://example.org/wiki/Potion", "attributes": { "internalName": "itm_potion" } }
] }
```

`id` and `name` are required. `id` is the raw key as the save stores it, compared ordinally, so
normalise hex/decimal yourself. `assetId` must name an image in the same pack. `attributes` holds up
to 32 extra string columns.

Assets (`kind: "assets"`) must be PNG, JPEG or WebP. The type is detected from the bytes and must match
the extension. SVG and anything else is rejected.

**Limits** (`ReferencePackLimits`):

| Limit | Value |
| --- | --- |
| Archive | 64 MiB |
| All files together | 128 MiB |
| One records file | 32 MiB |
| One image | 1 MiB |
| Image size | 2048 × 2048 |
| Files | 20,000 |
| Records per catalog | 250,000 |

### Embedding a pack in a plugin

```xml
<ItemGroup>
  <EmbeddedResource Include="ReferencePacks\**\*" LogicalName="refpacks/%(RecursiveDir)%(Filename)%(Extension)" />
</ItemGroup>
```

Every `refpacks/<folder>/pack.json` in a plugin assembly is loaded at startup. The conformance suite
test `EmbeddedReferencePacks_AreValid` fails with the loader's message if a pack is broken, for example
on a hash mismatch. Keep a `*.source.md` next to the data and the README table from rule 9 as well.
The pack attribution does not replace them.

### Reading the context

```csharp
public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

public ISaveDocument Open(ISaveFile file, IReferenceContext references)
{
    var save = Parse(file);
    // null when no pack provides "items" for this build: fall back to raw ids / embedded tables.
    var items = references.Catalog("items", gameBuild: save.Build);
    return new MyDocument(save, items, references);
}

// In the document:
var record = items?.Find(rawId.ToString(CultureInfo.InvariantCulture));
var field = Field(id, record?.Name ?? $"Item {rawId}", IntegerSpec.UInt16 with { Max = record?.Limit ?? ushort.MaxValue }, get, set)
    with { Description = record?.AttributionText, Confidence = record is null ? Confidence.Unknown : Confidence.Verified };
var icon = record?.AssetId is { } asset ? new ImageNode(asset, record.Name) : null;
public override AssetContent? GetAsset(string assetId) => references.GetAsset(assetId) ?? OwnAssets(assetId);
```

- `Catalog(id, gameBuild)` picks one pack per catalog. Packs made for another build are skipped. An
  exact build match wins over build-agnostic packs. Ties go to the preferred locale (`Toolkit:Locales`,
  default `en`), then to user packs over embedded ones, then to the higher version.
- `IReferenceCatalog` offers `Find(key)` (O(1)), `Search(new ReferenceQuery { Text, Category, SortBy, Descending, Offset, Limit })`
  (always paged, at most 200 per page), `Categories`, `SortKeys` and `Count`. There is deliberately no "all records"
  enumeration.
- Category filtering is exact and case-insensitive, and combines with substring search. Set
  `SortBy` to a declared `ReferenceSortKey` (`Key`, `Name` or `Category`); loaded F2 packs support
  all three. Text sorting is ordinal, case-insensitive, with an ascending ordinal raw-key tie
  breaker in both directions. Null categories sort first ascending and last descending.
  Omit `SortBy` to preserve file order. Filters run before sorting, then paging; `Total` is the
  number of matches across all pages. Unknown keys are rejected. Custom catalog implementations
  default to an empty `SortKeys` list and must declare/enforce any sorting they implement.
- `ReferenceRecord.Dataset` resolves a record's provenance: the pack id, dataset and
  `ReferenceAttribution`. `AssetProvenance(assetId)` does the same for images.
- Asset ids from records are pack-qualified (`packId:path`). Pass them through unchanged.
- The context is immutable and safe to keep in the document. It records which datasets you resolved,
  so the Sources panel lists exactly what the document uses.
- Always handle `null`. Tests and `Open(file)` see an empty context, and users may have no pack.
- If `Open(file)` forwards to `Open(file, IReferenceContext.Empty)`, you **must** implement the two-argument
  overload. Otherwise the default overload calls back into `Open(file)` and recurses forever.

See `tests/SaveToolkit.Core.Tests/Fakes/ItemsPlugin.cs` and its pack under `Fakes/ReferencePacks/`
for a complete example.

**API** (for the UI and F4's picker), all relative to `/api/workspaces/{ws}/documents/{doc}`:

| Endpoint | Returns |
| --- | --- |
| `/sources` | Pinned datasets with attribution and a `used` flag |
| `/references/{catalogId}?q=&category=&sortBy=Name&descending=false&offset=&limit=` | A page of records, categories and declared sort keys |
| `/references/{catalogId}/record?key=` | One record with its provenance |
| `/reference-assets?id=` | Image bytes, size-limited and served with `nosniff` |

## Differential-save tooling (F1 / F12)

`tools/SaveToolkit.SaveDiff` is the shared, read-only byte/semantic diff and fixture tool.
See [SAMPLES.md](SAMPLES.md#differential-save-tool-savediff) for capture, compare and fixture commands.
`savediff list-decoders` lists built-in modes, plugin views and explicit decoder hooks; use
`--format json` for the versioned `savetoolkit-decoders/1` envelope.

### Decode through the view

`savediff compare before.zip after.zip --decoder <plugin-id>` uses `PluginViewDecoder`.
`--semantic <plugin-id>` remains supported. Both modes retain `FieldNode.ValueId` as the semantic
path and expand lazy sections, then page through encountered F8 `QueryableTableNode`s. Duplicate
appearances of a field/collection are visited once. Action parameters are never decoded as saved data.

F8 queries have no search, filters or requested sort. **Keep the default query order deterministic**
with stable native identities as the tie-break; never derive keys from page offsets or localized
labels. The visitor supports offset and cursor paging, sorts each page's rows and columns ordinally,
and compares fields by value id. Direct `TextNode` cells and rich `SectionNode` cell titles also
become semantic values, at
`collections/<collection-id>/rows/<row-key>/cells/<column-key>`. These path components use JSON-pointer
escaping (`~` → `~0`, `/` → `~1`); fields retain their existing ids without a collection prefix.
Text cells can contain presentation churn, so use a native hook when a view is unsuitable evidence.

`DecodeLimits` bounds **all collections together per file**: `MaxQueryRows` (100,000),
`MaxQueryPages` (1,000) and `MaxQueryBytes` (32 MiB). `QueryPageSize` defaults to 200 (SDK maximum),
and must be 1–200. The CLI exposes `--max-query-rows`, `--max-query-pages` and `--max-query-bytes`;
zero disables that traversal budget. The byte cap counts complete serialized UTF-8 response pages,
including metadata and read-only cells; an over-budget page contributes no semantic values.
The existing value, value-length, view-depth and lazy-expansion limits still apply. Duplicate row
keys, repeated cursors, invalid metadata and unexpectedly empty pages stop that collection with an
incomplete-input warning. Caps set `SemanticTree.Truncated` / report `inputTruncated` and explain
which bound stopped traversal. Missing paths in an incomplete tree are **not proof of removal**.

### Publish a native decoder hook

An existing catalogued `IGamePlugin` may additionally implement
`SaveToolkit.Sdk.Diff.ISaveDecoderProvider`. Its `Decoders` property returns an explicit
`IReadOnlyList<ISaveDecoderHook>`; no decoder assemblies or types are scanned separately.
SDK-only production plugins do **not** need a dependency on Testing:

```csharp
// On the plugin class: IGamePlugin, ISaveDecoderProvider
public IReadOnlyList<ISaveDecoderHook> Decoders => [new MyEvidenceDecoder()];

// On MyEvidenceDecoder: ISaveDecoderHook
public string Id => "mygame.native-v1";
public bool CanDecode(ISaveFile file) => new MyPlugin().Probe(file).IsMatch;
public void Decode(ISaveFile file, ISaveDecodeSink sink)
{
    var model = MyFormat.Parse(file.ReadAllBytes());
    foreach (var item in model.Items)
    {
        if (!sink.Add($"inventory/{item.StableId}/quantity", "integer",
            item.Quantity.ToString(CultureInfo.InvariantCulture))) return;
    }
}
```

Use stable native record identities independent of offsets, physical order and translated labels.
Keep hooks stateless, read-only, bounded and free of I/O. The host checks `MaxFileBytes` before
calling an SDK hook and supplies a bounded sink; stop when `Add` returns false. Honor its
`MaxDepth`/other limits while parsing or traversing, and call `MarkTruncated(reason)` for any
additional bound or unproven identity fallback. `Warn` records other evidence limitations.
Never emit account/credential values into committed evidence; use opaque fingerprints where needed.

Existing `SaveToolkit.Testing.Diff.ISaveDecoder` implementations also implement the SDK hook through
a default adapter, so they can be returned unchanged by a provider in a project that already depends
on Testing. `HookSaveDecoder` adapts an SDK-only hook to the F1 engine. Test/research assemblies are
not discovered automatically; register hooks on a plugin that is actually present in `PluginCatalog`.

Hook ids must be globally unique (case-insensitive), 1–128 ASCII letters/digits or `.`, `_`, `-`.
They cannot replace `none`, `json`, `auto` or any plugin id. Collisions produce a CLI usage error.
Select a hook with `savediff compare before.zip after.zip --decoder mygame.native-v1`, or use its id
as a fixture manifest's `decoder`. `--decoder` and `--semantic` are mutually exclusive.
`auto` retains its existing best-plugin-view-then-JSON behavior; hooks require explicit selection.

## Tests

The conformance suite (`SaveToolkit.Testing.PluginConformanceTests<T>`) checks plugin info shape and
probing robustness against garbage, then uses selected real samples for opening, view validity and
JSON-serialisability (incl. lazy sections), `GetValue` consistency, value/spec consistency,
byte-identical no-op serialisation and edit → serialise → re-open round trips.
Override `ChooseEdits` to exercise a meaningful field (e.g. an item quantity).

Default coverage is bounded and can be overridden by each test class:

- `MaxFilesPerTitle`: **6** distinct-content, probed files per title, ordered by generation and
  primary before backup within each generation.
- `LazyExpansionBudget`: **150** lazy sections per walk.
- `QueryPageBudget`: **12** F8 pages across collections per walk.

This is not an exhaustive corpus or view traversal. Add explicit all-file/variant tests when making
broader coverage claims. The default edit chooser handles writable integer/bool/float fields;
tests for references, exact numbers, structures and grouped writes need deliberate cases.

Add your own tests for: crypto/hash test vectors, known values read from specific sample files
(e.g. "slot 0 play time is 25.698 h"), multi-file edits, and rejection of corrupted input.

Keep samples outside the checkout and set `SAVETOOLKIT_ARTIFACTS` to their directory.
`SampleSaves.Load("PPSA02530")` imports one matching `<TITLE>_saves_dec*.zip`; if several match,
`FindArchive` orders by full path length and selects the first, not every archive or the newest.
An ancestor `artifacts/` directory is a fallback; never copy private samples into git. Tests skip
when no matching archive is found. See [SAMPLES.md](SAMPLES.md).
For a complete sample-backed run in PowerShell, set the sample location in the same shell:

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/path/to/artifacts'
./scripts/test.ps1
```

Check each project's summary for zero skipped tests. `MemorySaveSet` copies input file buffers;
mutating the original byte array after creating a synthetic file does not edit that file.

### Deterministic mutation fuzzing (#40)

Every conformance subclass inherits `MutationFuzz`, a correctness/robustness check for malformed
saves. By default it runs **200 mutations per selected file**, with fixed seed **`0x40C0FFEE`** and zero-based
indices. Each mutation starts from the original bytes, using a specified xorshift32 sequence;
`SaveMutation.Create(original, seed, index)` reproduces a case independently of earlier cases.
The six mutation kinds cycle through bit flips, byte insertion, byte deletion, random truncation,
2/4/8-byte length-style overwrites (zero, all ones and huge values, both byte orders), and bounded
block duplication. Overwrites alternate between header and arbitrary payload locations. This is
bounded sampled coverage, not exhaustive validation or a security review.

For each available title, the harness uses `SelectFiles`, orders eligible files by size then ordinal
relative path, and takes up to `MaxMutationFuzzFilesPerTitle` (**1**) distinct file. Small files keep
the default run fast while exercising real format bytes. This independent cap keeps existing exhaustive
`MaxFilesPerTitle` overrides from expanding the fuzz run without limit. Missing titles are ignored
without hiding available titles. Every selected file gets the full mutation count; increase the
file cap for a broader local corpus run. Built-in synthetic seeds cover GenericJson (one) and
XorJson (two); other plugins must supply valid fixtures to run without private samples.
Override `MutationFuzzSyntheticSeeds` to supply small valid
`ISaveFile`s, including required siblings, for sample-free CI. With neither samples nor synthetic
seeds the fact reports a real skip. Environment overrides can select a different seed/count and
an overall time cap; see the single source of truth for
[nightly configuration and local reproduction](RELEASING.md#nightly-mutation-fuzz). For example:

```csharp
protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
    [MemorySaveSet.SingleFile("save.dat", CreateSmallValidSave(), "PPSA12345")];
protected override int MutationFuzzCount => 200;
```

Mutations use an in-memory overlay, preserving file names, title, generation, backup status and
unmodified siblings; no mutated sample is written to disk. Each case calls `Probe`, and on a
match calls `Open(file, pinnedReferences)`, `BuildView`, the first 50-row page of **every** collection
(including empty collections), and `Dispose`. Nonmatches, success, `InvalidDataException` and the
SDK's documented `SaveFormatException` are accepted. `MutationFuzzRejectionTypes` can declare
additional exact documented rejection types. Broad exception classes, argument/bounds errors,
null dereferences, overflow and out-of-memory errors cannot be declared clean rejections.
Lazy expansion, later pages, edits and serialization remain covered by the other conformance facts.

`MutationFuzzAllocationBudget(file)` defaults to **64 MiB + 32 × original file length**, capped at
**1 GiB** per case. It measures managed allocations on the synchronous plugin thread, from probe
through disposal, including rejected cases; fixture construction and reference loading are excluded.
It does not count native allocations or work a plugin starts on other threads. The default
`MutationFuzzCaseTimeout` is a generous **30 seconds**. One isolated child xUnit process per plugin
lets the parent kill a stuck parser and its process tree, rather than leave background tasks running.
Worker/sample preparation has a separate two-minute guard. A completed case also checks elapsed time,
so a timeout cannot be missed between progress polls. Allocation accounting is checked after a case;
it is a regression budget, not an allocator limit. Keep plugin parsers' own pre-allocation bounds.

Run the inherited check alone with the normal .NET 10 MTP runner:

```powershell
$env:SAVETOOLKIT_TEST_TEMP = 'D:/Temp/saveeditor/test-scratch/conformance-fuzz'
$env:TEMP = $env:TMP = $env:TMPDIR = $env:SAVETOOLKIT_TEST_TEMP
dotnet test --project plugins/ExampleGame/SaveToolkit.Plugins.ExampleGame.Tests --filter-method '*MutationFuzz'
```

Keep `SAVETOOLKIT_ARTIFACTS` pointed at external private samples. The worker checkpoints and result
JSON use `TestScratch` and are cleaned after the run. Failure reports include the seed, smallest
observed index for each sample/error/stack signature, relative sample path, original SHA-256,
mutation description, exception type and stack. They omit exception messages and sample values.
The test output and `mutation-fuzz.json` attachment record case counts, parser work time and expected failure counts; the test's wall
time additionally includes worker startup, fixture loading and mutation generation. Re-run the
same fact with the same sample hash, or call `SaveMutation.Create` with the reported coordinates.

`ExpectedFuzzFailures` is a virtual list of `ExpectedFuzzFailure(exceptionType, stackFrame,
trackingNote)`. It quarantines only an observed exception/stack signature, still runs every case,
and fails on other errors. Entries need a tracking note and should be removed when fixed. The
central `MutationFuzzKnownFailures` registry can seed per-plugin defaults while plugin folders are
outside the harness task's scope. Add entries only after reproducing a real failure and recording
its lowest observed index, stack top and analysis on the tracking issue. This list is temporary
debt, never a broader definition of acceptable parser behavior.

## Plugin README

`plugins/<Name>/README.md` must document: titles, file roles, binary layout, crypto/compression,
checksums, which fields are verified vs inferred, open questions, and all sources.
Keep these headings even when the answer is "None":

- `## Enabled writes and their evidence`: exact writable scope and sample/in-game evidence, or read-only.
- `## Framework feature adoption`: the F1–F13 table below, reassessed for the actual format.
- `## Sources and attribution`: source/revision, author/rights holder, licence/policy basis and use/modifications.
- `## SDK requests`: concrete missing capabilities, or none; do not edit `src/`.
- `## Licence exclusions`: the five-column table below, even when there are no refusals.

| Source URL / revision | Exact licence found | Refused material / reason | Alternative used | What would unblock reuse |
| --- | --- | --- | --- | --- |

Leave the exclusions table empty when no source was refused; never invent a refusal to fill a row.
Actual source decisions must also be recorded in the central register as required by rule 9.

### Framework feature adoption table

Each plugin README uses a `## Framework feature adoption` table with one row for **F1–F13** and
columns for **Feature**, **Status**, and **Concrete use / evidence or reason** (combined columns
are fine). Use `Used` for an implemented integration, `Added this round` for a newly implemented
one, and `N/A` only with a format-specific reason. A shipped framework feature is not pending
because it was absent on an older starting commit. Record an actual remaining adoption task or
SDK limitation precisely, link its issue, and update the row when resolved.

| Feature | What the row should identify |
| --- | --- |
| F1 | Fixture/byte/semantic evidence and the decoder used |
| F2 | Pinned embedded/imported datasets, attribution and missing-pack fallback |
| F3 | Rich cells/details, live filters and selection |
| F4 | Reference fields and action parameters, eligibility and raw unknown IDs |
| F5 | Stable identities, structural mementos and undo; or why no structural writer exists |
| F6 | Planned actions, semantic review and supported explicit/document/F8 query scopes (#27) |
| F7 | Actual coupled file roles/invariants and whole-group history; or no proven coupling |
| F8 | Complete-model search/filter/sort, stable row keys and bounded paging |
| F9 | Maps/graphs/text alternatives and provenance; separate reference state from saved state |
| F10 | Exact numeric lexemes/null/union where applicable; distinguish existing integer-string transport |
| F11 | Shared Kraken/SQLite use; or why the format has no applicable codec |
| F12 | Production decoder hook ID and/or shared bounded F8 view traversal; no Testing dependency required |
| F13 | Inspection `Mutates=false`, browse-only picker, catalogue filter/sort and evidenced float durations; explain each inapplicable part |

Framework adoption is not a console-validation or full-roadmap completion claim. Keep confidence,
write scope, unsupported dependencies and the linked owner checklist explicit. Add `## Licence exclusions`
with the rule 9 columns for every licence refusal and keep the central register synchronized.
