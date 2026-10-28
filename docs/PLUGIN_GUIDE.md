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

```powershell
./scripts/new-plugin.ps1 -Name Pragmata -Id pragmata -TitleIds PPSA02530 -GameName "Pragmata"
dotnet test --project plugins/Pragmata/SaveToolkit.Plugins.Pragmata.Tests
```

This creates `plugins/<Name>/SaveToolkit.Plugins.<Name>/` (picked up by the app automatically)
and `plugins/<Name>/SaveToolkit.Plugins.<Name>.Tests/` with the conformance suite wired to the samples.

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
See `tests/SaveToolkit.Core.Tests/Fakes/CounterPlugin.cs` for the smallest complete example.

## View vocabulary (src/SaveToolkit.Sdk/Views)

| Node | Use for |
| --- | --- |
| `SectionNode(title, children)` | Grouping. `LazyId` = load children on demand. `Badge` for counts. |
| `TabsNode([Tab])` | Top-level areas (Character / Inventory / World / Raw). |
| `FieldNode(valueId, label, spec, value)` | One value. `Confidence` (`Verified` / `Inferred` / `Unknown`) and `Warning` are shown to the user. |
| `TableNode(columns, rows)` | Homogeneous records. Cells are `FieldNode`s (editable) or `TextNode`s. The browser pages, searches and virtualises. Keep below ~5,000 rows per table; split or lazy-load beyond. |
| `StatsNode([Stat])` | Read-only headline figures (play time, level, slot). |
| `TextNode(text)` | Notes and warnings (`Tone`), `Monospace` for hashes/offsets. |
| `ActionNode(id, label)` | Plugin command, optional `Parameters` form and `Confirm` prompt. |
| `ImageNode(assetId, alt)` | Screenshots, thumbnails. |
| `HexNode(blobId, length)` | Raw bytes, paged. |

## Field specs (src/SaveToolkit.Sdk/Values)

| Spec | Value (`FieldValue`) | Notes |
| --- | --- | --- |
| `BoolSpec` | bool | |
| `IntegerSpec(min, max)` | Int128 (wire: decimal string) | Presets `IntegerSpec.UInt32` etc. Narrow the range with `with { Min =, Max = }` when the game has real limits. `Format = Hex` for ids/hashes, `Duration` + `UnitsPerSecond` for timers. |
| `FloatSpec` | double | `FloatSpec.Single` for float32 (values are rounded to float32 for you). |
| `TextSpec` | string | `MaxLength` in UTF-16 units. Check encoded byte length yourself in `validate`. |
| `EnumSpec(options)` | string (option value) | `AllowCustom` for partially-mapped ids (hashes). |
| `FlagsSpec(bits, width)` | integer bitmask | Unnamed bits are preserved. |
| `DateTimeSpec` | ISO-8601 string | `HasTimeZone = false` for console-local clocks. |

Value ids are opaque strings you choose. They must be **unique and stable for the lifetime of the
document** (the browser caches values by id, undo replays by id). Path-like ids work well:
`inventory/2227368435/quantity`, `tree/7c416083/7c018857[6]/d18b252d`.

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
- Actions that return `Undoable = false` still work: they clear history explicitly, as before.

**References between records:** until there is a dedicated node, expose a reference as an `EnumSpec`
field whose option values are the target records' stable keys. Labels are human names. The options are
rebuilt with the view after every structural change, so they never point at removed records.
`tests/SaveToolkit.Core.Tests/Fakes/RosterPlugin.cs` is a complete example with add, remove and
move actions.

## Hard rules

1. **Depend on `SaveToolkit.Sdk` only.** Never reference Core/Api. NuGet packages are fine when they
   are fully managed and permissively licensed; add the version to `Directory.Packages.props`.
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
8. **Reference data** (hash → name tables, item lists, descriptions, icons) goes into embedded resources
   inside the plugin, preferably as an embedded [reference pack](#reference-packs) so it carries its attribution.
9. **Attribute every reference at every place it shows up.** You may embed data from wikis, datamines and
   community tools, but each dataset needs full attribution in each of these places:
   - **Next to the data:** a header in the resource file, or a `*.source.md` sidecar. It gives the source
     URL, author or project, licence (with link), retrieval date, and what you changed or extracted.
   - **Plugin README:** a `## Sources and attribution` table with one row per dataset or code port.
   - **`IGamePlugin.Info.References`:** the upstream URLs.
   - **The UI:** the provenance or attribution text of every name, description or icon you show. Put it in
     the field `Description` (`ReferenceRecord.AttributionText`). Data from a reference pack is also listed
     in the document's **Sources** panel automatically.

   Honour licence terms. CC BY-SA data (e.g. most Fandom/wiki.gg wikis) stays under CC BY-SA in its
   derived form. Mark it so in the file header and keep the licence link. Code ports keep the upstream
   licence notice. If a source has no licence or forbids reuse, do not embed it. Cite it as research only.

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
- `IReferenceCatalog` offers `Find(key)` (O(1)), `Search(new ReferenceQuery { Text, Category, Offset, Limit })`
  (always paged, at most 200 per page), `Categories` and `Count`. There is deliberately no "all records"
  enumeration.
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
| `/references/{catalogId}?q=&category=&offset=&limit=` | A page of records |
| `/references/{catalogId}/record?key=` | One record with its provenance |
| `/reference-assets?id=` | Image bytes, size-limited and served with `nosniff` |

## Tests

The conformance suite (`SaveToolkit.Testing.PluginConformanceTests<T>`) checks, against the real samples:
plugin info shape, probing robustness against garbage, opening every claimed sample, view validity and
JSON-serialisability (incl. lazy sections), `GetValue` consistency, value/spec consistency,
byte-identical no-op serialisation and edit → serialise → re-open round trips.
Override `ChooseEdits` to exercise a meaningful field (e.g. an item quantity).

Add your own tests for: crypto/hash test vectors, known values read from specific sample files
(e.g. "slot 0 play time is 25.698 h"), multi-file edits, and rejection of corrupted input.

Samples live in `artifacts/<TITLE>_saves_dec.zip` (not committed). `SampleSaves.Load("PPSA02530")`
imports one; tests skip when it is missing. See `docs/SAMPLES.md`.

## Plugin README

`plugins/<Name>/README.md` must document: titles, file roles, binary layout, crypto/compression,
checksums, which fields are verified vs inferred, open questions, and all sources.
Add an `## SDK requests` section if the SDK lacked something you needed — do not edit `src/`.
