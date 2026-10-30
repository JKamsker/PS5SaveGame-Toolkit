# Sucker Punch save plugin (`suckerpunch`)

Titles: **PPSA02225** (Ghost of Tsushima, Director's Cut) and **PPSA26344** (Ghost of Yōtei).
Version 0.10.0 is **Experimental**: Tsushima inventory/play-time and Yōtei existing-quantity edits pass
conformance and exact byte-difference tests, but have not been tested in-game. Yōtei now decodes
48 inventory records, 33 statistics and a bounded 1,037-name gear registry. No sample bytes,
extracted saves, or third-party implementation code are included in this plugin.

## Enabled writes and their evidence

The 2026-10-28 owner decision, **"Enable writes. Keep warnings."**, replaces console-evidence gates.
Yōtei enables **14 existing quantity types** and one F6 target-quantity workflow using the
expanded eleven-generation evidence. Tsushima scalar quantities, timer and all three F6 actions are enabled.
Console evidence is tracked separately in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31);
no write is advertised as console-validated or Stable.

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| Tsushima existing resource quantities | Inferred | Named five-word records and consistent values across all 25 Tsushima entries | Known additive checksum repaired; output reparsed; unrelated bytes preserved | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): +1 resources, labels, carry limits, load/resave/reload |
| Tsushima existing ammo quantities and derived count copies | Verified byte structure; console effect unverified | Apollo named count-word targets; both copies agree in every sample | Both words updated together; checksum repaired; output reparsed; copies are not independently editable | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): fire/pick up ammo, capacity/clamping, load/resave/reload |
| Tsushima F6 `inventory/set-quantity` and `inventory/set-goods-quantity` | Same Inferred/Verified byte evidence as individual fields | Uses the same existing-record eligibility and fixed-width writer; all 25 samples tested | Preview lists primary counts, derived ammo copies and checksum, skips and warnings; apply revalidates; one undo/redo group | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): validate a small multi-resource/ammo edit in each intended build/mode |
| Tsushima F6 `inventory/restore-imported-ammo` (#40 shortlist #7) | Verified byte structure; console effect unverified; Experimental | Per-row opening counts captured once; existing paired-count writer and eligibility; all 25 Tsushima files tested | Pure preview lists both copies, checksum changes and skips; shared `ActionPlans` revalidation, atomic scalar apply and one undo/redo group; higher counts preserved | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): fire/pickup/reload and multiple ammo kinds; capacity/clamping, load/resave/reload |
| Tsushima v48/v49 play time | Inferred | Four-byte float at difficulty-table + 231; whole minutes match all non-empty slot subtitles; 23 supported entries including backups | Only timer plus repaired checksum change; output reparsed; finite bounded values only | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): small timer edit, load/resave/reload, inspect per-difficulty counters and stale subtitle normalization |
| Yōtei existing Mon/Flowers, seven ingredients and five arrow/ammo quantities | Inferred | Same bounded 48-name, 21-byte-trailer layout across all 11 nonempty saves; changing quantities and matching ammo copies; eligibility word/byte retained | Recompute additive candidate at 0x0C and reparse; checksum tail coverage unknown: **game may reject the edited save**; no-op byte-identical | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): +1 pickup/edit/load/resave/reload, labels/caps, file roles and coverage; [exact checklist](YOTEI_FORMAT.md#exact-owner-captures-for-31) |
| Yōtei F6 `yotei/inventory/set-quantity` | Inferred | Same eligible existing records; per-row conservative bounds | Pure preview with primary/derived changes, skips and warnings; stale-plan rejection; atomic apply and one undo/redo group | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): validate a small multi-row edit, record normalization and sibling-file changes |

Quantity fields, action descriptions, previews and apply outcomes retain **not validated on console**
warnings. Tsushima quantity bounds remain 0–32767 (an upstream edit value, not a game capacity). Timer bounds
are 0–198318.828125 seconds, the highest observed v48/v49 value; a larger original value extends that
document's bound so undo can restore it. This is a conservative editor range, not a game limit.
The timer warning explicitly says the game may normalize or reject the edit: per-difficulty counters
and `param.sfo SUBTITLE` remain unchanged and may disagree. No v47 or unknown-version timer writer exists.

Yōtei bounds are per-record observed maxima (Mon 112099, Flowers 22, other rows listed in
[Yōtei format evidence](YOTEI_FORMAT.md#enabled-quantities-and-bounds)), not game capacities.
Yōtei editable quantity fields, action descriptions, previews and successful changing outcomes retain
the checksum-coverage and **game may reject** warning. The F6 parameter is capped at 112099 even if
an imported record extends its scalar field's bound; rows whose individual bound is lower are skipped.

Tsushima scalar eligibility requires a supported raw identifier, flag word 1 or 3, an existing count
at most 32767, and equal ammo copies. Resource IDs start with `Ingred` or are `Supplies`, `Hide`,
`Flowers` or `NGPFlowers`; ammo IDs start with `Arrow`, `Dart` or `Ammo`. Friendly-name mapping
confidence does not determine write eligibility. All three F6 actions additionally require version 47–49;
the scalar quantity writer has no separate version gate. Play-time editing requires the recognized
inventory and a finite nonnegative timer in a uniquely recognized v48/v49 progress block.

Still disabled for structural safety:

- **Yōtei unmapped/locked records, gear flags, technique/character/world state and empty placeholders:**
  only existing proven fixed-width quantity slots are writable. Invariant registry bytes do not prove
  ownership; progression relationships, structural lengths and side effects are unknown. Guessing such
  writes could corrupt definitions/references. A mismatching additive candidate is inspection-only.
- **Equipment, techniques, character progression and world state:** the scanner identifies strings,
  not writable state fields or ownership/selection/reward relationships. Replacing them could damage
  definitions or references. No gated writer for these was implemented in the earlier handoffs.
- **Difficulty and structural add/remove:** general difficulty changes alter string lengths; enclosing
  sizes, offsets and references cannot be updated safely by the current fixed-width model. Item
  insertion/removal likewise has no understood enclosing structure or acquisition side effects.
  No structural inventory writer or F7 group is exposed; the independent manual caption uses whole-byte F5 history below.

### Experimental manual Tsushima save caption (SDK-10)

`metadata/set-save-caption` accepts exactly one typed user-authored `caption` string and document
scope. It is available only for **PPSA02225**, recognized editable inventory, observed container
**v47–v49**, and root `dis.dat` / `memory.dat`. Yōtei and nested game-state roles are deferred.
This is a metadata-only override: no timer, difficulty, title, locale or filename derives the text.
Game acceptance and the native layout/timer relationship remain **Inferred/Unknown**.

The same directory must contain exactly one existing `sce_sys/param.sfo` and a unique existing
`SUBTITLE` in strict UTF-8 format `0x0204` or `0x0004`. The shared SDK `ParamSfoDocument` parses,
plans and applies every patch. No binary parser, complete SFO writer, new key, resize or plugin I/O
was added. Missing, duplicate, malformed, unsupported, oversized, invalid-encoding and already
writer-over-limit sources remain untouched with a bounded unavailable reason. Existing CR/LF
captions also disable the action, writable caption metadata and additional SFO ownership;
their source bytes are preserved without stripping or normalization. Display-only
`Directory.ParamSfo` never grants write authority or causes another directory to be selected.

The text hint is `min(entry.MaximumLength, MaxEncodedValueBytes) - 1`: the SDK default global
encoded cap is **4096 including storage NUL**, so the maximum logical hint is **4095 bytes**.
The entry may impose a smaller bound. Empty text is allowed; embedded NUL, lone surrogates,
CR/LF and over-capacity values reject without mutation. Literal U+FEFF consumes three payload
bytes. `MaxLength` is not supplied: UTF-16 length is a separate concept, not the SFO capacity.
The existing shared action UI can show used/capacity/remaining; server validation remains authoritative.

Preview publishes exact canonical `metadata/sce_sys/param.sfo/SUBTITLE` old/new values, the exact
SFO path, encoded byte usage, Experimental/native-rejection warnings and source/entry SHA-256
identities. The fingerprints participate in deterministic replanning, including when the text
returns to its former value but reserved bytes differ. No IDs or saved state are allocated on
preview. The lazy **Caption value and provenance** section publishes the real canonical field
through the existing binding path, so semantic review/Changes and individual value reversion can
find it; its scalar edits use the same whole-byte transaction as the F6 action. This is not parsed
action-payload metadata. Existing inventory primaries and derived ammo/checksum metadata are
also explicitly registered through `IFieldMetadataDocument`, preserving host authority for those actions.

Every real caption edit returns a bounded memento containing **all primary and SFO bytes**. Undo
and redo reparse the SFO, refresh bindings and restore reserved suffix/tail bytes exactly. A text
inverse alone cannot restore bytes overwritten by a longer caption. Snapshot restoration preserves
fixed-width gameplay offsets and immutable opening-ammo/timer bounds. Mixed direct gameplay/caption
batches reject atomically; this action deliberately changes metadata alone. An exact no-op has no
revision, dirty flag or history step.

`IAdditionalOutputOwner` declares only the exact existing directory-relative SFO path. The opened
primary is implicitly owned by the host; `Serialize()` returns that primary and the full-path SFO
**exactly once each**, even clean or after no-op/undo/redo. Unchanged primary bytes are retained.
The host separately filters clean sessions to zero export overrides. ChangedFiles carries both
owned outputs; ChangedDirectories includes remaining directory files; Everything includes the
whole imported set. Host ownership collisions, immutable imported-source fingerprints, strict
overlays, and identical/unowned console-twin mirroring retain the published ownership rules.
Unrelated files, other directories and different-title twins are never selected by this action.

Evidence for this adoption is synthetic exact-byte testing, not a new private-sample or console
claim: [`CaptionPlanningTests`](SaveToolkit.Plugins.SuckerPunch.Tests/CaptionPlanningTests.cs),
[`CaptionMementoTests`](SaveToolkit.Plugins.SuckerPunch.Tests/CaptionMementoTests.cs),
[`CaptionEligibilityTests`](SaveToolkit.Plugins.SuckerPunch.Tests/CaptionEligibilityTests.cs),
[`CaptionSessionTests`](SaveToolkit.Plugins.SuckerPunch.Tests/CaptionSessionTests.cs) and
[`CaptionOwnershipTests`](SaveToolkit.Plugins.SuckerPunch.Tests/CaptionOwnershipTests.cs).
The underlying SDK contract is pinned by
[`ParamSfoPatchTests`](../../tests/SaveToolkit.Core.Tests/ParamSfoPatchTests.cs),
[`SystemOutputOwnershipTests`](../../tests/SaveToolkit.Core.Tests/SystemOutputOwnershipTests.cs), and
[`TextCapacityTests`](../../tests/SaveToolkit.Core.Tests/TextCapacityTests.cs).
Actual browser/keyboard/screen-reader/responsive acceptance is separately owned; unit tests do
not establish it. Automatic synchronization stays blocked on the unchanged-control, timer-only,
difficulty-only, native-resave/reload, role/sibling, independent-repeat and reversible owner captures
in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).

## Supported behavior

* Yōtei uses a dedicated parser/writer for its extra inventory byte and different header. The Inventory
  table has 48 browse-only F4 references and eligible Inferred quantities; Statistics shows 33 read-only
  named counters. Equipment uses F8 `yotei/gear-registry` with 1,037 raw names, two opaque bytes, stable
  IDs, pages capped at 200, search/filter/sort and Unknown ownership. Techniques shows ten attributed
  unjoined F9 guide nodes with no invented edges; World shows sixteen reference-only hot springs,
  three compact screenshot symbols and a Horizon View map excerpt with one image-pixel pin. All saved
  unlock/discovery/completion/reward states remain Unknown. See [evidence, limits and captures](YOTEI_FORMAT.md).

* Tsushima: edit quantities in existing unlocked resource and ammunition records. Ammunition edits
  update both count words targeted by the Apollo / Save Wizard refill codes. The checksum is repaired
  immediately after an atomic edit batch, exposed as a derived change, and checked again during export.
* Attributed, revision-pinned wiki names accompany defensible raw-ID mappings. `IngredCoreOre`, raw
  `Hide`, numbered charms and armor slots retain their raw names and **Unknown** mapping confidence.
  Resource quantity meanings remain **Inferred**. Ammo count meanings remain **Verified** against
  source anchors and both count copies; this is separate from name mapping and console validation.
* An F6 preview/apply action sets a target quantity for existing Supplies, Flowers and raw Hide records
  in observed container versions 47–49. Document scope covers those three records; an explicit empty
  row selection changes nothing. Locked/oversized/already-matching rows are skipped; other selected
  IDs are excluded. The preview includes count/checksum changes and warnings, is pure, and is rechecked
  on apply. A second action, `inventory/set-quantity`, extends the same workflow to all existing
  supported resource/ammo records, with derived ammo copies shown explicitly. Both actions use
  ordinary validated scalar edits and one Core undo/redo group. Neither unlocks items or claims a capacity.
  SDK-41 binds both target controls to eager table selection ID `tsushima/inventory`; Yōtei's existing
  `yotei/inventory/set-quantity` binds to `yotei/inventory`. Selected raw row keys include rows hidden
  by filters. **With nothing selected, these sibling target controls use document scope**, even if a
  filter is active. An explicit API `rowKeys: []` remains a no-op. Select-all/deselect in these eager
  inventories sends explicit row keys; neither ID is an F8 collection, and query scopes (including
  exclusions or a fully excluded query) are rejected. The read-only equipment collections remain
  `equipment/identifiers` and `yotei/gear-registry` and cannot scope a quantity action.
* `inventory/restore-imported-ammo` previews each eligible ammo row's immutable opening count,
  restoring only rows reduced during this workspace session. Document scope covers existing ammo;
  explicit selections exclude non-ammo with reasons, and an empty selection changes nothing.
  **Restore selected ammo counts** is rendered inside the inventory table so the F6/#27 browser
  selection context supplies the selected row keys, including selections hidden by filters.
  The separate **Restore imported ammo counts (whole document)** control retains document scope
  regardless of table selection. With no rows selected, use that control to restore all eligible ammo;
  an explicitly empty table selection still changes nothing. Inventory query scopes remain unsupported.
  Flags must remain 1 or 3, both counts must agree, and both imported/current counts must be within
  0–32767. Locked, inconsistent, oversized and unsupported records stay unchanged. Counts already
  at or above their opening value stay unchanged; no capacity, unlock or missing record is granted.
  The baseline survives view rebuilds, intermediate edits, undo and redo. It is not the previous edit's
  value and cannot recover ammo lost before import; re-importing an edited save starts a new baseline.
  Preview/cancel changes no bytes. Apply uses shared `ActionPlans` revalidation and the existing atomic
  scalar writer, updates both copies/checksum, and records one undo/redo step. Console/clamping warnings
  appear in the control, preview and outcome; the operation remains Experimental.
* Difficulty is **Inferred** and read-only. The **Inferred** v48/v49 play timer is writable, with the
  explicit metadata/counter warning and observed bound above. Every unedited slot's whole play-time
  minutes and difficulty match its `param.sfo SUBTITLE`. The timer publishes F13
  `FloatFormat.Duration` with one stored unit per second; raw float32 editing and fractions are retained.
  The v47 timer layout is different, so no timer is published for it. No console metadata or difficulty counters are rewritten.
* Locked records, unmatched ammo copies, unsupported item kinds and existing quantities above 32767 are
  inspection-only. The editable range 0–32767 follows the upstream refill value, **not a proven game
  capacity**. The UI warns that quantities can be clamped. Ownership/upgrade flags are never changed.
* The embedded F2 pack `suckerpunch.tsushima` contains attributed raw-ID catalogs for the 48 inventory
  records plus observed equipment and technique identifiers. F4 `ReferenceSpec` fields preserve each
  exact ID and expose the catalog picker metadata; they are read-only because no safe ID replacement
  mapping has been proven. F13 opens these fields in browse-only mode, including category filters,
  declared Key/Name/Category sorting, paging, raw-ID previews and attribution. Each catalog field carries
  its dataset attribution and an Inferred/Unknown confidence label.
* Tsushima has read-only Equipment, Techniques and Character tabs. They show first observed offsets and
  explicit Unknown ownership/equipped/progression relationships. Identifier presence is not treated as
  ownership, a loadout, a prerequisite, or an editable value. Raw record words remain read-only.
* Inventory retains a visible raw-ID column alongside friendly reference labels, categories and mapping
  confidence. Equipment uses the F8 `equipment/identifiers` collection with stable row IDs, server-side
  text/category/state filtering, deterministic sorting and bounded pages; Techniques remains a small
  eager table. Both retain Unknown ownership/state. Lazy reference guides cover resources, charms, armor,
  sword kits and techniques. The Techniques tab adds an F9 read-only `GraphNode` over observed identifiers;
  every item links back to its reference field, and no prerequisite edge is invented while the state is
  unproven. The World tab adds an F9 `MapNode` over 17 region-scoped Shinto shrine
  references plus an eager text table for row navigation. It includes attributed whole-island artwork;
  shrine alignment is unverified, so all markers stay unpositioned and the text list stays available.
  Discovered/completed/rewarded state is Unknown for every entry. The external PowerPyx guide remains linked. Edits invalidate raw block snapshots. Guide views are capped at 200 rows and report
  truncation if a larger pack is used. Guide names are also read-only `ReferenceSpec` fields: their
  pickers browse the complete pinned catalog even when the eager guide page is capped. Guide tables
  use shared category sorting, while wiki IDs remain separate from saved IDs and cannot be committed.
* Opening copies the input; exports and hex pages are isolated copies. No-op exports are byte-identical,
  including unknown data, console metadata and padding. Only the selected document's file is returned;
  sibling saves and backup slots are preserved. `param.sfo` changes only through the explicit manual caption workflow below; host export may mirror an identical unowned console twin.

## Malformed-input handling

Unsupported physical sizes or magic, out-of-file declared content ends, and malformed recognized
Tsushima inventory or Yōtei named tables reject with a contextual `SaveFormatException`. Table counts
are fixed by the recognized layout; name lengths must be 1–64 bytes and identifiers must be unique ASCII letters, digits or
underscores. Traversal stays inside the declared content, including complete record trailers. No
allocation or loop count comes from an unchecked file length/count field.

Tsushima checksum mismatches reject. A valid Tsushima container without its recognized inventory
anchor remains inspection-only. Missing, incomplete or ambiguous Tsushima progress evidence omits
difficulty/time fields while retaining any valid inventory edits; it does not reject the container.
Yōtei requires its recognized tables; its inferred checksum candidate
may mismatch, in which case every quantity and action remains inspection-only and no-op output
preserves the mismatch. A zero-magic Yōtei file is accepted only when the entire full-size file is zero.
Opaque sections and bytes after the declared end remain preserved; their meaning is unconfirmed.

The malformed-input review extends the existing synthetic and sample-backed corruption tests:
all five named-table counts, every Yōtei table family's non-anchor names, oversized declarations
with a 4 MiB parser-allocation budget, every byte boundary in the final record's length/name/trailer,
and physical prefixes at header/name/data boundaries through both Open and the native decoder.
Checksum tests cover the first/last content byte, excluded tail, header terms and full-file declared
end. Existing sample mutations now cover every claimed Tsushima file and all eleven current nonempty
Yōtei files; physical truncation and invalid zero-magic checks also cover both Yōtei placeholders.
All mutations are made in memory; no sample bytes are embedded in tests. The review found no
additional production correctness defect requiring a parser change.

## Framework feature adoption

| Feature | Status | Concrete adoption or reason |
| --- | --- | --- |
| F1 SaveDiff | Used | `fixtures.json` records the uncontrolled Tsushima generation pair and selects `suckerpunch.native-v1`; bounded native and F8 view decoding are tested alongside `ByteDiffer`. No fixture embeds save bytes. Yōtei tests compare all 55 nonempty pairs; ten native CLI comparisons cover every slot. |
| F2 reference packs | Used | Embedded `suckerpunch.tsushima` v3.0.0 catalogs and four art datasets are loaded through `IReferenceContext`; per-record and UI descriptions carry attribution and the Sources panel exposes each dataset. Yōtei pack adds 48 raw item references, 10 technique guides, 16 location guides and four attributed assets. |
| F3 rich table cells | Used | Inventory, equipment, technique and world tables expose raw IDs, descriptions, mapping-confidence cells, warning tones and F6 action controls. SDK-41 gives both eager inventories stable selection IDs and binds their sibling target controls. Tsushima inventory cells publish selection-scoped ammo restoration alongside an explicitly document-wide standalone control. Equipment/technique tables and lazy guides show local attributed ImageNode cells (106 unique icons/thumbnails, 158 catalogue bindings); the SDK has no separate detail-cell node. Yōtei adds inventory symbols, read-only reference cells, raw statistics, warning/confidence metadata and F6 controls. |
| F4 reference pickers | Used | Every catalog-backed inventory, equipment and technique ID is a read-only `ReferenceSpec` field with raw-ID fallback, category/description metadata and attribution. Yōtei item/technique/location catalog IDs are browse-only with raw fallback and provenance; no gear-name catalogue join is invented. |
| F5 structural edits / mementos | Used | The manual caption captures complete primary/SFO bytes for one-step exact undo/redo, including overwritten reserved suffixes. No safe add/remove/reorder writer exists: enclosing sizes, references, ownership and acquisition side effects are unproven, so a plugin-local structural substitute would risk data loss. |
| F6 planned actions | Used | The SDK-10 substrate supports the independent document-scoped `metadata/set-save-caption`, one typed `caption` parameter, exact canonical old/new text, source/entry fingerprints, byte capacity and Experimental warnings. Inventory/query scopes reject. Existing resource/ammo target actions and Tsushima restore-imported-ammo provide pure previews, skipped rows, derived ammo/checksum changes, stale-plan rejection and one Core undo/redo group. Target controls use SDK-41 selected rows (including hidden selections), falling back to document scope when nothing is selected; explicit empty API scopes remain no-ops. Restore uses each eligible row's opening count only when lower and retains its distinct cell/document controls. Query scopes are rejected because the writable inventory is a bounded fixed table and the equipment collection is read-only. Yōtei adds one target-quantity action with per-row observed bounds and the same atomic preview/apply guarantees. |
| F7 document groups | N/A | No controlled capture proves that `memory.dat`, `dis.dat`, `param.sfo` or a backup must change with a supported edit; gameplay edits preserve metadata. Manual caption edits explicitly own the same directory’s existing SFO, retain the selected primary bytes, and do not imply gameplay coupling. |
| F8 queryable collections | Used | The per-save equipment identifier view is `equipment/identifiers`, with stable IDs, bounded pages, search, filters and deterministic sorting. Small inventory/technique/world views remain eager because they are below the paging threshold. Yōtei adds `yotei/gear-registry` (1,037 entries); its 33 statistics remain a bounded eager table. |
| F9 maps and graphs | Used | Techniques publish a validated read-only `GraphNode` with field targets and a text alternative; World publishes a validated `MapNode` with attributed whole-island art, row targets, Unknown statuses and unpositioned markers because alignment is unverified; the schematic/list fallback remains. Yōtei adds a 10-node Unknown technique graph and 16-location map/table; only Horizon View is placed on its local screenshot excerpt. |
| F10 exact numbers and nullable/union specs | N/A | Supported edits are fixed-width uint32 quantities and an observed float32 timer; no save field carries an exact JSON number lexeme, nullable value or union that this plugin could preserve. |
| F11 shared codecs | N/A | Tsushima/Yōtei samples are fixed-size plaintext containers; no Kraken, Oodle or SQLite codec is involved. |
| F12 SaveDiff traversal/decoder hooks | Used | `ISaveDecoderProvider` registers SDK-only `suckerpunch.native-v1`; the F1 fixture uses the hook through `HookSaveDecoder`. Separate `PluginViewDecoder` tests traverse every F8 equipment row across small pages and verify incomplete-input limits. Yōtei hook now emits 48 quantities/eligibility, ammo copies, 33 statistics, 2,074 gear bytes and 192 secondary-registry words. |
| F13 inspect actions/picker filtering/duration | Used | The float32 timer declares Duration/seconds. Inventory, equipment, techniques and all six guide catalogs use browse-only read-only references with shared category filters and declared sorting; guide tables use catalog category ordering. Inspection actions are N/A: existing actions change quantities and there is no message-only inspection operation to opt in. Yōtei references use browse-only pickers with catalogue category/search/sort; its play timer is unknown so no duration is guessed. |
| F14 inventory slots / empty rows | N/A | Both inventories contain fixed named definitions, not proven occupied/empty slots. All-zero Yōtei files are whole-file placeholders. No `EmptyRows` metadata or `TableRow.IsEmpty` semantics are published. |

Shared SDK helper adoption: both documents use `BoundSaveDocument` for scalar bindings, validation
and rollback. Tsushima planned actions use shared `ActionPlans.ApplyScalar` for ordered revalidation,
binding preparation and primary scalar application, retaining the fresh plan's warnings.
The optional `InMemoryCollection<T>`, bounded binary cursor/range,
`ReferenceCatalogReader`, `ReferenceViews` and `ReferenceInspection` helpers are available but not
adopted here. Collections retain the plugin's one-result query cache and title-specific matching/sort
rules; Yōtei plans retain local ordered comparison and both titles use byte-sum checksum deltas; parsers and icon/guide readers
remain local. Neither format uses FStrings or IEEE CRC32, so those shared codecs are inapplicable.
These are adoption choices, not missing SDK capabilities.

The merged F4/F6 parameter fix (`63580dc`) is N/A here: target actions accept only a
bounded integer quantity and ammo restoration takes no parameters, with no `EnumSpec` ID fallback or reference replacement parameter.

The explicit F12 hook emits stable native paths for four header words, inferred difficulty/play
seconds, all 48 existing Tsushima quantities and mirrored ammo counts. It reuses the validated
container/inventory/progress parsers, stops when the sink declines a value, honors file/depth limits,
and retains evidence warnings. It deliberately omits labels, offsets, reference-guide selections,
account data and unknown payloads. Yōtei adds all observed named quantities/eligibility, ammo copies,
statistics, gear bytes and the secondary registry with explicit semantic/coverage warnings. Neither
hook claims a complete payload decode. The F8 view decoder remains
available as `--decoder suckerpunch` for view-oriented inspection.

```powershell
dotnet run --project tools/SaveToolkit.SaveDiff -- list-decoders
dotnet run --project tools/SaveToolkit.SaveDiff -- compare before.zip after.zip --decoder suckerpunch.native-v1
dotnet run --project tools/SaveToolkit.SaveDiff -- verify plugins/SuckerPunch/fixtures.json
```

`CanEdit` is per document: a validated Tsushima container with the recognised inventory is writable;
containers without its anchor can be inspected without edits. Yōtei can edit when the candidate
checksum matches and at least one supported record meets fixed-width eligibility; placeholders and
mismatching candidates remain inspection-only.
An ambiguous anchor or a malformed anchored table is rejected with `SaveFormatException`.

## File roles and sample coverage

`dis.dat` contains individual slots; `memory.dat` contains state with the same Tsushima payload layout.
These are fixed-size decrypted files, with zero padding after the declared content in the supplied dumps.
No additional encryption or compression layer was found; runtime plugin code performs no file/network I/O.

| Title | File size | Supplied files | Distinct byte contents |
| --- | ---: | ---: | ---: |
| PPSA02225 | 555,008 | 25 | 12 |
| PPSA26344 | 616,448 | 13 | 12 (eleven nonempty, one all-zero pattern) |

The Tsushima archive includes primary/backup twins and two `sce_sdmemory` generations. The older
`memory.dat` is **v47**; the remaining content uses v48/v49. PS5 samples themselves contain v49, so
upstream descriptions of v49 as PC-only must not be used to classify these dumps' platform.

## Tsushima container and writes

All header words are little-endian uint32:

| Offset | Meaning | Confidence |
| ---: | --- | --- |
| 0x00 | Magic `0x14E` | Verified |
| 0x04 | Content end offset / content size | Verified |
| 0x08 | Additive checksum | Verified |
| 0x0C | Format version (47, 48, 49 observed) | Verified as stored version |

```text
checksum = (sum(bytes[0x10:contentSize]) + contentSize + 0x14E + version) modulo 2^32
```

The range is half-open; trailing padding is excluded and preserved even if non-zero. Header values and
content bytes are not re-encoded. After edits, only selected count words/the play-time float and the checksum at 0x08 change.
`Serialize()` validates the emitted container, inventory structure and recognised progress block before
returning it. Invalid batches leave every byte unchanged. Undo replays the primary quantity values and
recomputes the checksum, restoring the original bytes. Timer undo/redo likewise restores the exact float bits.
Timer assignments that round to the imported value preserve its original float32 representation,
including the sign of zero; entering numeric `0` cannot silently normalize an accepted `-0` timer.

## Inventory structure and anchor evidence

Apollo's CUSA11456 patch and the SaveWizard archive use this sequence:

1. `80010015 ...`: find the 21-byte pattern `11 00 00 00` + ASCII `LegendsInvestment`.
2. `8801000F ...`: search forward from that pointer for `0B 00 00 00` + ASCII `ArrowNormal`.
3. `92000000 0000000F`: advance past the length-prefixed name.
4. `28000004` / `28000008`: write little-endian uint32 counts at +4 and +8 from the data start.

This interpretation was checked against Apollo's patch-engine source: type 8 searches, subtype 8
starts at the current pointer, type 9/2 advances it, and type 2/8 writes a pointer-relative uint32.
Type 4 multi-writes combine a starting value, repetition count, address stride and value increment;
they are not executed by this plugin. This is a targeted parser, not a cheat-code interpreter.

In every Tsushima payload the unique equipment-name anchor is followed, within 512 bytes, by:

```text
uint32 recordCount = 48
repeat recordCount times:
    uint32 nameByteLength
    ASCII  name[nameByteLength]       // no terminator
    uint32 word0                     // ownership/upgrade bits inferred: 0, 1, 3 observed
    uint32 word1                     // quantity
    uint32 word2                     // matching quantity for ammo; zero for resources observed
    uint32 word3                     // unknown
    uint32 word4                     // unknown, often 0xFFFFFFFF or small integers
```

Names are bounded to 64 ASCII identifier characters and unique. The parser requires the first
`ArrowNormal`, the trailing `NGPFlowers`, and named `Supplies`/`Flowers` records. All 48 records are
traversed with bounds checks; record sizes, flags and unknown words are preserved. No new items are
inserted. Ammo words 1 and 2 match in every supplied record; the plugin refuses to edit a mismatch.

The PS4 unlock anchors `5F502EE24A20`, `5F502EE24B20`, and rank/stance anchor
`FFFFFFFF000A000000` do **not** match these samples. Their relative unlock offsets are not applied.
The alternative Google Docs code mirror and Apollo patch were checked; no matching rank anchor emerged.

### Slot evidence

Values below are direct byte readings, **not independently observed in-game balances**. They are
asserted by format tests, while conformance and edit/difference tests cover all snapshots and backups.
The Supplies column includes its absolute count offset only to document the evidence; code uses anchors.

| Slot | Supplies (offset) | Bamboo (`IngredCoreBamboo`) | Ore (`IngredCoreOre`) | Flowers | Normal arrows |
| --- | --- | ---: | ---: | ---: | ---: |
| User0 | 199 (`0x4BFCC`) | 46 | 30 | 19 | 0, locked |
| User1 | 85 (`0x4C20C`) | 67 | 59 | 25 | 6 |
| User2 | 89 (`0x4C210`) | 79 | 59 | 28 | 0 |
| User3 | 156 (`0x4C209`) | 86 | 59 | 30 | 8 |
| User4 | 1339 (`0x4C487`) | 152 | 97 | 47 | 6 |
| User5 | 1357 (`0x4C492`) | 152 | 98 | 47 | 6 |
| User6 | 1540 (`0x4C4C8`) | 156 | 98 | 47 | 8 |
| User7 | 1855 (`0x4C6D9`) | 192 | 123 | 56 | 8 |
| User8 / first memory | 8250 (`0x566FD`) | 339 | 500 | 197 | 11 |
| User9 | 7499 (`0x562D1`) | 278 | 493 | 173 | 12 |
| Auto0 | 7605 (`0x5650C`) | 288 | 500 | 179 | 12 |
| second memory, v47 | 0 (`0x4B995`) | 0 | 0 | 0 | 0, locked |

### Difficulty and time correlation

A second structure contains four length-prefixed names, each followed by a float32:
`Easy`, `Medium`, `Hard`, `Nightmare`. The parser checks the complete ordered sequence, rather than
accepting a lone `Easy` string. Within the preceding 80 bytes, a length-prefixed current difficulty is
followed immediately by the length-prefixed string `Ship`. Its `Easy` / `Medium` value agrees with all
slot subtitles (`Einfach` / `Mittel`). Difficulty remains read-only because changing the string's length
would move data and may also require changing other difficulty state.

The v48/v49 float32 at **table start + 231** correlates exactly with subtitle minutes:
User0 `9576.2958984375` seconds → `02:39`, User1 `15127.5546875` → `04:12`, User6
`40487.40234375` → `11:14`, User9 `185140.953125` → `51:25`, Auto0 `186975.4375` → `51:56`,
first memory/User8 `198318.828125` → `55:05`. The automated test compares every non-empty subtitle.
These correlations establish an **Inferred** play timer, not a confirmed field name from game code.
The four per-difficulty counters grow plausibly but their sum is lower than total play time, so they
are described only as raw float counters; their exact accounting semantics are unknown.

## Yōtei research status

The expanded corpus replaces the earlier two-zero-counter evidence. All eleven nonempty payloads
share a bounded primary inventory, a separate zeroed secondary registry, 33 named statistic counters
and a 1,037-name/two-byte gear registry. Mon/Flowers and several resource/ammo quantities now vary;
14 eligible quantity types and one F6 workflow are enabled with candidate integrity repair and warnings.
The registry itself never changes and is not treated as gear ownership or equipped state.

[YOTEI_FORMAT.md](YOTEI_FORMAT.md) records every header/quantity vector, table layout, observed bounds,
F1/F12 differential method, uncertainty, performance measurements and exact #31 capture checklist.
Play time, health/spirit, active difficulty, techniques, saved collectibles and quest/world progress
remain unknown; attributed reference guides never claim saved completion or unlock state.

### Restore imported Yōtei quantities — deferred (#40)

The integrity-first review on **2026-10-29** did **not** establish exact game checksum/tail coverage,
so no restore-imported Yōtei action was implemented. This does not change the existing Experimental
scalar quantities or target action and their checksum-coverage/game-rejection warnings.

[`YoteiIntegrityCoverageTests`](SaveToolkit.Plugins.SuckerPunch.Tests/YoteiIntegrityCoverageTests.cs)
pins all eleven nonempty sample header vectors and the two all-zero placeholders. Every tail from
the declared end through physical EOF is zero. Both `sum(bytes[0x10:declaredEnd]) + declaredEnd + 0x150`
and `sum(bytes[0x10:EOF]) + declaredEnd + 0x150` match every stored checksum, modulo 2^32.
The declared ends span 411397–417135 in 616448-byte files. These samples cannot distinguish the
coverage endpoints or exclude another integrity dependency.

Every byte of every currently writable primary count and paired ammo copy lies inside the declared
interval; individually flipping each such byte trips the production checksum gate. This establishes
coverage by the **implemented candidate**, not by the game's integrity checks. In-memory mutations
at the first tail byte and physical last byte distinguish the two formulas: the plugin accepts the
declared-end candidate and preserves the tail, but treats the whole-file candidate as inspection-only.
Export reparsing uses that same candidate and cannot independently prove which formula the game uses.
These synthetic tails are not console evidence or naturally observed nonzero tails.

The Tsushima `inventory/restore-imported-ammo` workflow from `a422395` supplies the intended immutable
opening-baseline/preview/atomic-history shape, but its established checksum coverage cannot be transferred
to Yōtei's different header. Revisit after an independently verified Yōtei checksum implementation or
accepted nonzero-tail capture/control distinguishes the hypotheses, and any additional integrity is
understood and repaired. [#31 capture 7](YOTEI_FORMAT.md#exact-owner-captures-for-31) describes the
required owner experiment. No new rows, quantities, capacities or unlocks were granted by this review.

## Research log and sources

Earlier format research, 2026-10-28: reviewed the converter, both SaveWizard code lists, Apollo's patch and its actual
code interpreter before implementation. Compared all 25 Tsushima files / 12 distinct payloads, including
the older memory snapshot; rejected obsolete binary anchors, parsed named inventory, correlated
subtitles, and tested fixed-size edits. Searches for Yōtei save editors, checksums and `memory.dat`
research did not produce a usable primary-source parser. Downloaded references were read as text and
never executed. Only algorithms and patch semantics were independently implemented; no substantial
third-party code was copied or ported.

Write-round recheck, 2026-10-28: reread [KOVRlN technical notes at 519bb466](https://github.com/KOVRlN/ghostoftsushima-save-converter/blob/519bb46620ef20a4424f1ef899d11455f4381044/TECHNICAL.md)
and [Apollo's patch at 273f032b](https://github.com/bucanero/apollo-patches/blob/273f032b4c31c017fed5c1588f209b8faf87ba6c/PS4/CUSA11456.savepatch).
The checksum and paired ammo-write facts remain the basis of the existing writer. The timer is an
independent inference from the supplied PS5 correlations, not a field documented by either tool.
A fresh GitHub-focused Yōtei `memory.dat`/checksum/editor search yielded no usable primary-source
quantity parser; no third-party code or new reference dataset was imported in the write round.

Ammo-restoration recheck, **2026-10-29**: reread the pinned
[Apollo paired-ammo codes at 273f032b](https://github.com/bucanero/apollo-patches/blob/273f032b4c31c017fed5c1588f209b8faf87ba6c/PS4/CUSA11456.savepatch)
and [KOVRlN checksum notes at 519bb466](https://github.com/KOVRlN/ghostoftsushima-save-converter/blob/519bb46620ef20a4424f1ef899d11455f4381044/TECHNICAL.md).
They support the existing count-word layout/checksum, not a game capacity or console acceptance.
The new action composes that writer with immutable workspace-opening values; it adds no gameplay
mapping or reference data. Tests reduce eligible ammo in every one of the 25 PS5 sample files,
restore each row's own count, independently check the checksum and exact bytes, and re-open exports.
Synthetic cases exercise flags 0/1/2/3, unequal copies, imported 0/32767/32768, higher counts,
selection exclusions, unsupported versions/layouts, forged/stale plans, checksum carries and re-import baselines.

* [KOVRlN technical notes](https://github.com/KOVRlN/ghostoftsushima-save-converter/blob/main/TECHNICAL.md):
  Tsushima header and additive checksum. Independently verified against every supplied Tsushima file.
* [KOVRlN converter](https://github.com/KOVRlN/ghostoftsushima-save-converter): cross-platform format lead;
  upstream version/platform assumptions are not treated as proof for PS5.
* [SaveWizard Tsushima codes](https://github.com/sterben-Dev10/SaveWizard-Resources/blob/main/SaveWizard-Codes/Ghost%20of%20Tsushima%20Codes%20%5BSaveWizard%5D.txt):
  named ammo anchors, relative count writes and obsolete unlock/rank patterns.
* [Alternative Google Docs mirror](https://github.com/sterben-Dev10/SaveWizard-Resources/blob/main/SaveWizard-Codes/Ghost%20of%20Tsushima%20Codes%20Alt%20(Google%20Docs)%20%5BSaveWizard%5D.txt):
  cross-check of older binary-anchor unlock codes; no direct addresses were reused.
* [Apollo CUSA11456 patch](https://github.com/bucanero/apollo-patches/blob/main/PS4/CUSA11456.savepatch):
  independent archive of named ammo refill codes; also labels itself untested.
* [Apollo patch interpreter](https://github.com/bucanero/apollo-lib/blob/main/source/patches.c): authoritative
  interpretation of search, pointer advance, little-endian pointer-relative writes and multi-writes.
* [Apollo code-format reference](https://bucanero.github.io/apollo-lib/savewizard.html): syntax overview;
  actual interpreter source was used where descriptions were ambiguous.

Yōtei recheck, 2026-10-29: searched GitHub and the web for `Ghost of Yotei save editor`,
`PPSA26344 memory.dat checksum`, datamines, inventory IDs and binary templates. No usable public
primary-source Yōtei parser, authoritative gameplay-ID registry or checksum implementation was found.
[Digital2buy's modified-save listing](https://digital2buy.com/es/producto/ghost-of-yotei-espanol-guardado-modificado-goyotei-mod-goy-ps5-mods-yotei-ghost-of-yotei)
claims modified resources but supplies no reproducible offsets/algorithm; it is research context only,
not format evidence. The [Young Bamboo wiki page](https://ghostfranchise.fandom.com/wiki/Young_Bamboo)
was a naming lead, but direct retrieval failed and no wood-ID join was inferred. These are evidence
limits, not licence refusals. The checksum lead remains the attributed Tsushima converter; Yōtei's
different header/trailers were independently established from the new corpus and differential tests.

[IGN's 2025-10-02 skill guide](https://www.youtube.com/watch?v=_H2a_RaapdM) supplies ten named
reference techniques; [PowerPyx's 2025-10-01 hot spring guide](https://www.powerpyx.com/ghost-of-yotei-all-hot-spring-locations/)
supplies sixteen names/regions and one in-game screenshot. The
[collectibles guide](https://www.powerpyx.com/ghost-of-yotei-all-collectibles-locations-guide/) was also
cross-checked. No save-ID ordering is inferred from those lists. Short descriptions were written
in original words; no verified source established official effect text tied to the raw saved gear/skill
IDs, so that join remains owner-blocked. No third-party code was downloaded and executed.

## Sources and attribution

Caption-adoption research, **2026-10-30**: read the actual published SDK/Core sources at main
`201dd690a48a93bf496f1825246bc6954cc5ec52` and PR60
`5109db8af09ce2effe19ff6c0897de15fd9a0d5d`, their tests and contract documentation before
implementation. Rechecked KOVRlN’s technical notes and RPCS3’s PSF loader as primary format
comparisons. Those upstream sources do not establish a PS5 metadata/gameplay dependency.
No downloaded code was copied, executed or ported, no dataset was added, and no private sample
was read for this adoption.

| Caption source / revision | Author / rights holder | Licence / use |
| --- | --- | --- |
| [SDK-10 substrate at 201dd690](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/201dd690a48a93bf496f1825246bc6954cc5ec52/docs/SDK10-SFO-SUBSTRATE.md), [ownership contract](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/201dd690a48a93bf496f1825246bc6954cc5ec52/docs/SYSTEM_OUTPUT_OWNERSHIP.md), and tests linked above | PS5 Save Game Toolkit contributors | Repository GPL-3.0-or-later; compose public SDK helpers unchanged. |
| [PR60 logical text capacity at 5109db8a](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/5109db8af09ce2effe19ff6c0897de15fd9a0d5d/docs/TEXT_CAPACITY.md) | PS5 Save Game Toolkit contributors | Repository GPL-3.0-or-later; use logical byte hint; storage NUL remains SDK writer policy. |
| [KOVRlN technical notes](https://github.com/KOVRlN/ghostoftsushima-save-converter/blob/main/TECHNICAL.md), retrieved 2026-10-30; existing pinned revision 519bb466 below | KOVRlN | Existing attributed format research; no code or new data imported. |
| [RPCS3 PSF loader](https://github.com/RPCS3/rpcs3/blob/master/rpcs3/Loader/PSF.cpp), retrieved 2026-10-30 | RPCS3 contributors | GPL-2.0-or-later; comparison only, no copied implementation. Existing-entry writer authority is the SDK, not RPCS3 game semantics. |

| Dataset | Entries | Pinned source | License |
| --- | ---: | --- | --- |
| `suckerpunch_items` | 48 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Resources?oldid=29221) | CC-BY-SA-3.0 |
| `suckerpunch_equipment` | 122 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Sword_Kits?oldid=32356) | CC-BY-SA-3.0 |
| `suckerpunch_techniques` | 6 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Techniques?oldid=29271) | CC-BY-SA-3.0 |
| `suckerpunch_resource_guide` | 12 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Resources?oldid=29221) | CC-BY-SA-3.0 |
| `suckerpunch_charm_guide` | 79 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Charms?oldid=32085) | CC-BY-SA-3.0 |
| `suckerpunch_armor_guide` | 15 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Armor?oldid=29366) | CC-BY-SA-3.0 |
| `suckerpunch_sword_guide` | 41 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Sword_Kits?oldid=32356) | CC-BY-SA-3.0 |
| `suckerpunch_technique_guide` | 18 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Techniques?oldid=29271) | CC-BY-SA-3.0 |
| `suckerpunch_world_guide` | 17 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Shinto_Shrines?oldid=29432) | CC-BY-SA-3.0 |
| `suckerpunch_techniques_art` | 18 icons / 34,818 bytes | [Pinned Techniques page](https://ghostfranchise.fandom.com/wiki/Techniques?oldid=29271), individual files in [asset sidecar](SaveToolkit.Plugins.SuckerPunch/ReferencePacks/suckerpunch.tsushima/assets.source.md) | SIE rights reserved; owner decision 2026-10-29 |
| `suckerpunch_charms_art` | 32 images / 64,167 bytes | [Pinned Charms page](https://ghostfranchise.fandom.com/wiki/Charms?oldid=32085), individual files in asset sidecar | SIE rights reserved; owner decision 2026-10-29 |
| `suckerpunch_gear_art` | 56 thumbnails / 61,612 bytes | [Armor](https://ghostfranchise.fandom.com/wiki/Armor?oldid=29366) and [Sword Kits](https://ghostfranchise.fandom.com/wiki/Sword_Kits?oldid=32356), individual files in asset sidecar | SIE rights reserved; owner decision 2026-10-29 |
| `suckerpunch_map_art` | 1 background / 78,140 bytes | [Map of Tsushima](https://ghostfranchise.fandom.com/wiki/File:Map_of_Tsushima.png), retrieved 2026-10-29 | SIE rights reserved; owner decision 2026-10-29 |
| Official armor descriptions (part of `suckerpunch_armor_guide`) | 14 descriptions | [Pinned per-article revisions in asset sidecar](SaveToolkit.Plugins.SuckerPunch/ReferencePacks/suckerpunch.tsushima/assets.source.md#official-descriptions) | CC BY-SA 3.0 wiki adaptations; game text credited to SIE / Sucker Punch |
| External collectible map link (no imported data) | 17 linked markers | [PowerPyx Tsushima guide](https://www.powerpyx.com/ghost-of-tsushima-wiki-strategy-guide/) | Link only; licence not asserted |
| `suckerpunch_yotei_items` | 48 raw IDs / evidence descriptions | [Original corpus research, #22](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/22); [sidecar](SaveToolkit.Plugins.SuckerPunch/ReferencePacks/suckerpunch.yotei/items.source.md) | Game identifiers: SIE / Sucker Punch; rights reserved, owner decision 2026-10-29 |
| `suckerpunch_yotei_techniques` | 10 names/categories / original summaries | [IGN, 2025-10-02](https://www.youtube.com/watch?v=_H2a_RaapdM); [sidecar](SaveToolkit.Plugins.SuckerPunch/ReferencePacks/suckerpunch.yotei/techniques.source.md) | IGN / SIE rights reserved; owner decision 2026-10-29 |
| `suckerpunch_yotei_world` | 16 names/regions / original summaries | [PowerPyx, 2025-10-01](https://www.powerpyx.com/ghost-of-yotei-all-hot-spring-locations/); [sidecar](SaveToolkit.Plugins.SuckerPunch/ReferencePacks/suckerpunch.yotei/world.source.md) | PowerPyx / SIE rights reserved; owner decision 2026-10-29 |
| `suckerpunch_yotei_art` | 3 symbols + 1 excerpt / **89,702 bytes** | [PowerPyx screenshot](https://www.powerpyx.com/wp-content/uploads/ghost-of-yotei-hot-spring-1.jpg); [hashes/crops/dimensions](SaveToolkit.Plugins.SuckerPunch/ReferencePacks/suckerpunch.yotei/assets.source.md) | PowerPyx / Sony Interactive Entertainment / Sucker Punch Productions; rights reserved, owner decision 2026-10-29 |

The nine text catalogues and four art datasets belong to embedded pack `suckerpunch.tsushima`
v3.0.0. Original wiki retrieval was 2026-10-28; artwork and game-description retrieval was 2026-10-29. Wiki-derived data and the adapted mappings retain
[CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/) share-alike. Each dataset has a
`.source.md` sidecar, manifest attribution, per-record pinned source, and UI attribution; upstream
URLs are also in `Info.References`. The inventory dataset additionally uses
[Arrows, oldid 31796](https://ghostfranchise.fandom.com/wiki/Arrows?oldid=31796). Source changes and
contributor attribution are detailed in the pack's `SOURCES.md`. Names, gameplay facts and acquisition
information were initially extracted/shortened. The asset round adds 157 catalogue records with game
effect/flavor descriptions, omits button instructions and retains the existing factual acquisition
summaries. All 106 icons/thumbnails are at most 64x64 and are deduplicated; the whole-island image is
1152x2048 WebP without crop, rotation or projection. **Tsushima embedded binary assets: 238,737 bytes.**
There are 158 icon bindings: 18 technique guides, 4 Inferred saved stance mappings, 79 charm guides,
15 armor guides, 41 sword-kit guides and the existing Inferred Storm of Clan Sakai mapping.
Minor charms reuse game category symbols; nine major charms without individual art use their major
category symbol. Appearance thumbnails do not identify a saved armor dye/tier or establish ownership.
Ambiguous numbered charms/armor slots stay raw/Unknown with no guessed icon.

Art is credited to **Sony Interactive Entertainment / Sucker Punch Productions** and hosted by
**Ghost Franchise Wiki contributors**. The F2 licence field is
`Rights reserved by SIE; used with attribution per owner decision 2026-10-29`; SIE abbreviates Sony
Interactive Entertainment to fit the framework's 100-character licence-label cap. This records the owner's
reuse policy, not a new publisher licence. Full holder names, individual source/download URLs, downloaded
content hashes, retrieval date, processing and bindings are in `assets.source.md`, pack attribution,
`Info.References`, field descriptions, table attribution cells and the Sources panel. Wiki text
adaptations retain CC BY-SA 3.0; quoted game text is credited separately in dataset modification notices.
No game-state coordinates are invented: image bounds are pixel bounds only, alignment remains
unverified, all 17 shrine markers are unpositioned, and missing art/provenance retains the list.
Yōtei now has a separate title-isolated pack with three symbols (Mon, Flowers, hot spring) and one
1920×1080 local screenshot excerpt. **Yōtei assets: 89,702 bytes; plugin total: 328,439 bytes.**
There are 18 icon bindings and 74 original evidence/guide descriptions (48 items, 10 techniques, 16
locations). None is claimed as an official effect quotation. Every Yōtei dataset has a sidecar,
manifest attribution, Info.References link and visible provenance; no new licence-based refusal exists.

### Mapping policy and evidence

There are **19 Inferred friendly-name mappings** across the 176 raw-key entries:

* Nine resources: `IngredCoreBamboo`, `IngredUpgradeGold`, `IngredUpgradeSteel`, `IngredCoreLinen`,
  `IngredUpgradeLeather`, `IngredUpgradeSilk`, `IngredUpgradeYew`, `Supplies`, `Flowers`. The names
  match explicit resource words; Yew → Yew Wood is a lexical inference. The resource list supplies
  category/vendor facts, not the raw-ID join. No ore → iron, Hide → Predator Hides, or wax-tree →
  Wax Wood assumption is shipped.
* Five arrows: `ArrowNormal`, `ArrowFire`, `ArrowHeavy`, `ArrowExplosive`, `ArrowPiercing`, joining
  the named Apollo refill anchors/sample records to wiki arrow labels. Piercing Arrow is explicitly
  Legends-only in its description; string presence does not establish campaign availability.
* Four stances: `StoneStance`, `WaterStance`, `WindStance`, `MoonStance`, matching the wiki names
  after inserting a space. These strings do not prove stance unlocks, points or active selection.
* `SwordKatanaSakaiStorm` → The Storm of Clan Sakai: the katana/Sakai/Storm words match the wiki's
  default kit. This does not prove a live owned/equipped cosmetic record.

Each mapping includes a record-level `mappingEvidence` and `mappingConfidence`. All remaining
157 raw mappings are **Unknown**. Quantity confidence and edit safety are independent of name
confidence. In particular, a wiki list's order is never used to assign `AccMinorCharmNN` or
`ArmorAccN` names. Rechecking all 12 distinct Tsushima payloads found the same numbered charm and
stance identifiers across versions 47/48/49, consistent with definitions/registries being mixed into
the save. No controlled observation proves their role as owned, equipped or unlocked state.

The six additional guide datasets have **182 named reference entries** (12 resources, 79 charms,
15 armor sets, 41 sword kits, 18 selected techniques, 17 shrines). Their `wiki:` keys are a separate,
explicitly unjoined reference namespace. Their names are bound read-only `ReferenceSpec` fields with
browse-only pickers; they are not saved item IDs or writable replacement choices. Armor/charm
ownership, technique unlocks, and world discovery/completion/rewards remain
Unknown. The historical Archery Master's Attire is marked removed in patch 2.04. The shrine reference
separates Izuhara (7), Toyotama (6), Kamiagata (3), and Iki expansion (1), without claiming a
save-specific denominator or completed count. No Yōtei entries are imported into Tsushima's pack.

## Licence exclusions

| Source / revision | Licence found | Disposition | Alternative | Unblock |
| --- | --- | --- | --- | --- |
| [PowerPyx Tsushima guide](https://www.powerpyx.com/ghost-of-tsushima-wiki-strategy-guide/), historical check 2026-10-28 | No separate media licence recorded | Resolved: publisher-asset refusal superseded by owner decision 2026-10-29. | Attributed wiki island art and game icons imported instead; PowerPyx remains an optional source link. | No remaining licence blocker. Marker alignment and native saved-state joins still need evidence; these are not licence refusals. |

### Remaining work and owner captures

Phase 1's attributed naming/readability and target-quantity preview are implemented. Phase 2 still
lacks live equipment/character joins. Phase 3's equipment/technique/character edits and Phase 4's
saved world-state reads require actual field/relationship definitions; the World reference is not a completion tracker.
The existing corpus contains no annotated single-action captures. No new gameplay state semantics
were inferred merely from different slot balances. The timer and expanded quantity action above are
enabled independently of owner console evidence. No console-tested feature or Stable maturity is claimed.

For each capture, use one disposable slot and retain **all participating files**; record title, PS5
patch/build, region, mode (campaign/Legends), NG/NG+, DLC and locale. First capture an unchanged
load/save control. Then capture separate before/after pairs for:

1. **Resource labels/caps:** +1 Supplies, pick one flower, sell one Predator Hide, sell one iron,
   one yew wood and one wax wood; fire/pick up one arrow; upgrade one pouch/quiver. Photograph the
   relevant menu balance and upgrade tier. This resolves raw Hide/Ore/WaxTree joins and capacities.
2. **Equipment:** equip one already-owned armor, sword kit and charm separately; switch one enabled
   per-armor loadout; acquire a second copy of one minor charm; buy one dye; upgrade one existing
   armor/weapon tier. Record exact item names, slot/ownership counts and both loadouts before/after.
3. **Character/techniques:** earn one technique point, spend one point on a named technique, buy one
   prerequisite chain, unlock one stance, finish one mythic tale, gain one legend rank, take damage
   then heal. Record displayed points/rank/health/Resolve and technique state at every step.
4. **World:** for one named shrine, fox den, hot spring, haiku, bamboo strike, pillar and quest,
   capture discovery separately from completion and reward. Record region/location, journal state,
   awarded item/health/Resolve/charm-slot change and coordinates if available.
5. **Console validation:** restore an unchanged export first. For a small supported quantity edit,
   load, observe the exact balance, consume/buy one item, resave, reload and export. Record any
   clamping/normalization and compare every changed span; repeat separately for NG+/Iki/Legends
   before claiming support in those modes.
6. **Yōtei:** follow the [exact seven-part capture checklist](YOTEI_FORMAT.md#exact-owner-captures-for-31):
   validate enabled +1/F6 edits; individual resource/ammo pickups and capacities; separate gear
   equip/acquire/dye/upgrade; point earn/spend/prerequisite; separate discovery/completion/reward;
   idle 60 seconds, damage/heal and difficulty; manual/auto/memory roles and checksum coverage.
   Preserve all participating files and the unchanged load/save control for each pair.
7. **Play time:** on a disposable v48/v49 slot, change the timer by one minute within its displayed
   bound. Load, check displayed time, resave, reload and export. Compare the timer, per-difficulty
   counters, all participating files and `param.sfo SUBTITLE`; record normalization or rejection.

## Validation and maintenance

SDK-41 target-selection adoption, **2026-10-29**: authored regression tests in
`QuantitySelectionBindingTests`, `QuantitySelectionSessionTests` and
`YoteiQuantitySelectionEligibilityTests` are **NOT YET EXECUTED**. This implementation round ran
static checks only; dedicated integration agents own builds, test hosts and browser validation.
The new tests cover title/version declarations, unchanged restore placement, staged advisory
selection ownership/pure repeated plans, empty/document/foreign/query scopes, all 14 native Yōtei
types and bounds, eligibility/checksum restrictions, exact changed ranges, export/reopen and
sample-backed one-step undo/redo/no-op history and stale rejection. Advisory fixture success means
only that staged case passed; NotEvaluated and Failed are not passes. Runtime/browser scope wiring
has not been established by these authored assertions.

After configuring scratch and samples below, run the focused cases and full plugin suite:

```powershell
dotnet test --project plugins/SuckerPunch/SaveToolkit.Plugins.SuckerPunch.Tests -c Release --filter-class '*QuantitySelection*'
dotnet test --project plugins/SuckerPunch/SaveToolkit.Plugins.SuckerPunch.Tests -c Release
```

The binding/advisory/eligibility cases use invented in-memory saves. The session cases require the
external `PPSA02225` archive (`suckerpunchUser1`) and `PPSA26344` archive (`suckerpunchUser4`);
missing archives must report skips. No private samples or extracted save data belong in the checkout.

### Browser scope validation recipe (pending)

Use the integration agent's running app and isolated workspace; do not change the shared frontend.
Inspect live request scopes without retaining private save payloads or exported sample files in logs.

1. Import each title and open Inventory. Confirm the target control's accessible hint names
   **Existing inventory** (Tsushima) or **Existing Yōtei inventory** and describes document fallback.
   Check both Tsushima target controls and Yōtei's target control retain the console/integrity warnings.
2. Stage distinct eligible quantities: Supplies/Mon = 0, ArrowNormal = 0, Flowers = 0. Select the
   resource and ArrowNormal, then filter to hide ArrowNormal. Preview target 1 on the resource/ammo
   control. Inspect `POST .../actions/plan`: the original action ID and `scope.rowKeys` must contain
   exactly both raw IDs, with no `queryScope`. Preview changes only these eligible primaries, the ammo
   copy and checksum. Cancel must leave staged bytes/history unchanged. Repeat/apply, verify Flowers
   and every unselected count/copy are unchanged, export/reopen, then undo/redo in one step.
3. For Tsushima goods, keep the same selection. Preview must change Supplies only and skip ArrowNormal
   with an exclusion reason. For either title, select TestItem/locked/inconsistent rows as applicable;
   verify skip reasons and existing eligibility. Yōtei target 5 must skip ArrowFire when its bound is 3;
   retain the checksum-tail warning. No equipment/progression/world write should appear.
4. Select all filtered inventory rows, deselect one, and hide a selected row with a new filter.
   The target request must contain exactly the remaining selected raw IDs, including hidden selections.
   These eager tables have no all-query selection. Query/excluded-query requests for either inventory
   ID or the read-only equipment IDs must reject; a fully excluded query must never become document scope.
5. Clear selection while a filter is active. Preview must send document scope (no rowKeys/queryScope),
   include eligible rows outside that filter and show **Whole document**. Separately send an explicit
   empty `rowKeys: []`: preview/apply must change nothing and add no undo step. Tsushima's table-cell
   ammo restore with no selection must remain empty; its whole-document restore remains independent.
6. Switch away from Inventory and return: table selection resets, and a fresh target preview uses
   document scope. After apply/undo/view rebuild, verify the binding still resolves. A bound control
   retained in an open dialog must disable if its table unmounts; no unavailable-table request should
   be sent. A stale preview after an intervening edit/undo must require a fresh preview.

These checks establish software scope only; maturity stays **Experimental**, and console load/effect/
resave/reload acceptance and Yōtei checksum-tail coverage remain unverified.

### Existing suite commands and historical evidence

```powershell
$scratch = 'D:/Temp/saveeditor/test-scratch/feat-coverage-suckerpunch'
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP = $scratch
$env:TEMP = $scratch
$env:TMP = $scratch

dotnet test --project plugins/SuckerPunch/SaveToolkit.Plugins.SuckerPunch.Tests -c Release --results-directory "$scratch/results" --report-xunit-trx --report-xunit-trx-filename results.trx
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj -c Release --warnaserror
python docs/tests/test_documentation.py
dotnet run --project tools/SaveToolkit.SaveDiff -c Release -- list-decoders
dotnet run --project tools/SaveToolkit.SaveDiff -c Release -- verify plugins/SuckerPunch/fixtures.json
```

Accuracy audit on **2026-10-29**: the sample-backed Release suite passed **351 tests, 0 failed,
0 skipped**; the API Release build passed with **0 warnings and 0 errors**. All 14 documentation
checks and the normal all-plugin SaveDiff fixture verification also passed. Sample-dependent tests skip when archives are absent; a skipped
run does not validate the corpus. `SampleSaves` uses the shared `TestScratch` helper, honors
`SAVETOOLKIT_TEST_TEMP`, and attempts to remove its per-process sample imports on exit. TEMP/TMP also stay on D:
for other tooling. Use a separate scratch folder for each concurrent run.

Ammo-restoration validation on **2026-10-29**: the sample-backed Release suite passed
**372 tests, 0 failed, 0 skipped**, including all 25 Tsushima files and the existing Yōtei,
conformance and mutation-robustness checks. The API Release build with `--warnaserror` passed
with **0 warnings and 0 errors**. Restoration-specific tests cover pure preview/cancel,
per-row baselines, limits/skips, one-step multi-ammo undo/redo, stale/forged rejection,
independent export checksums, re-open and exact byte/no-op preservation. Owner console
validation is still required under #31; no console result is claimed.
The central documentation check against `main` passed 26 of 27 checks: its existing
`test_built_app_smoke_docs_cover_shipped_flows_and_skipped_regressions` still expects
`test.fixme(` in `web/e2e/import.e2e.ts`, although that regression is now an active test.
Those unchanged central files are outside this plugin-only task.

Yōtei restoration feasibility validation on **2026-10-29**: **387 tests passed, 0 failed,
0 skipped** with both private sample archives. The 15 new integrity cases pin the corpus, eleven
header vectors, every writable count/copy byte and first/last-tail counterexamples. Existing tests
retain exact scalar/target exports, history and byte-identical no-op coverage. The API Release build
with `--warnaserror` passed with **0 warnings and 0 errors**. All test/temp paths were under
`D:/Temp/saveeditor/test-scratch/feat-coverage-suckerpunch-yotei`; samples remained external/read-only.
The restore-imported Yōtei action was deferred because exact game checksum/tail coverage is unproven.

Tsushima restoration selection fix on **2026-10-29**: first reproduced the rendered-scope regression
with ArrowNormal selected and both ammo types reduced; the unselected ArrowFire incorrectly restored
from 0 to 2. The fix places a planned restore control inside each inventory row's table context,
using the same F6/#27 pattern as other plugins. The separate document-wide control is explicitly
labelled and retains its previous behavior. Four new regression cases cover selected-only changes,
explicit empty selection, document scope and sample-backed export/checksum/reopen/undo/redo.
The full sample-backed Release suite passed **391 tests, 0 failed, 0 skipped**; the API Release build
with `--warnaserror` passed with **0 warnings and 0 errors**. Yōtei restoration remains deferred;
there is no new Yōtei restore action or selection claim.

Coverage includes all 25 Tsushima and 13 Yōtei files (including both zero placeholders), byte-identical
no-op exports, exact count/copy/timer/checksum changes, export reparsing, atomic rejection and rollback,
Core scalar and grouped undo/redo, console-risk warnings, stale/forged plans, corruption and byte
boundaries, isolated returned buffers, pinned pack validity/hashes/attribution, F4 browse-only fields,
complete F8 traversal and bounded pages, F9 Unknown-state maps/graphs and missing-art fallbacks, and
native decoder depth/file/value limits. Synthetic characterizations retain plan ordering, validation
precedence, query matching/sorting and early decoder-stop behavior. Production code depends only on
the SDK and performs no file/network I/O.

Historical Debug allocation/timing comparisons from the asset and Yōtei expansion rounds are not
current performance guarantees. The later [performance review](PERFORMANCE.md) identifies its exact
baseline/final commits and records reproducible Release measurements for both corpora, allocation
regressions and the first-query sorting tradeoff. The earlier Yōtei implementation measurements remain
in [YOTEI_FORMAT.md](YOTEI_FORMAT.md#measurements), explicitly dated and qualified. Pure plans avoid
whole-save copies; paged collections retain one document-local result index with copied filter criteria.

## Open questions

* Console-load the edited quantities; only a human can promote maturity to Stable.
* Confirm individual resource meanings and game capacity limits, plus the flag bits and other raw words.
* Identify chapter/quest state and rank/technique records without relying on unmatched PS4 offsets.
* Locate the v47 play timer and determine the accounting of per-difficulty float counters and subtitle normalization.
* Validate enabled Yōtei quantities on console; establish checksum coverage and remaining gameplay joins via #31.

## SDK requests

No current blocking SDK request. `TextSpec.MaxUtf8Bytes`, system-output ownership and the manual
Tsushima caption consumer are merged. `CaptionPlanningTests`, `CaptionSessionTests`,
`CaptionOwnershipTests` and `CaptionMementoTests` cover capacity, ownership and whole-byte history.
The merged `web/e2e/suckerpunch-caption-*.e2e.ts` suites exercise the production consumer with authored
fixtures; execution is recorded separately in issue #81. Native console acceptance remains unverified.

Resolved conformance follow-up: `PluginConformanceTests.Serialize_WithoutEdits_IsByteIdentical`
uses `FindAnyFile`, including declared SFO outputs. The inherited sample-dependent fact still skips
without samples; synthetic tests do not establish sample-backed or console acceptance.

Presentation limitation: the published `ActionNode` has no disabled/reason property. Unavailable
captions therefore show a bounded Overview warning and publish no actionable control. A future
framework-owned disabled-action property would improve discoverability; no invented wire metadata
or plugin-local UI substitutes are used. The host snapshot budget is configurable but not exposed
to plugins: each caption token retains at most 555,008 + 1,048,576 payload bytes plus small overhead,
well below the default 256 MiB history budget. A host configured below that bound applies its ordinary
history eviction/warning policy; the plugin cannot promise undo under an arbitrarily lower host budget.

- **Resolved — shared SaveDiff discovery:** the former `ffxvi` hook/view collision no longer occurs.
  The FinalFantasy16 hook is `ffxvi-evidence`; the full CLI lists `suckerpunch` (view) and
  `suckerpunch.native-v1` (native hook) alongside the other plugins. The historical isolated-assembly
  workaround is no longer needed. Use the normal commands above.
- **Resolved — test scratch:** `SampleSaves` now uses `TestScratch.ProcessFolder`, which honors
  `SAVETOOLKIT_TEST_TEMP` and performs best-effort process-exit/stale-folder cleanup. Setting TEMP/TMP
  remains useful for other tools, rather than working around a missing SampleSaves capability.
- **Resolved — float duration:** F13 supplies `FloatFormat.Duration` and `UnitsPerSecond` for the
  known Tsushima timer. No Yōtei timer location/units are established.

F2/F4/F6/F8/F9/F12/F13 need no plugin-local substitute. F8 query actions are N/A because both large
collections are read-only and the writable inventories are bounded eager tables. F5/F7 need field and
relationship evidence, not a new SDK contract. The landed SDK-10 substrate and system-output ownership support the independent manual caption.
Automatic timer/difficulty synchronization still requires controlled native evidence; no SDK API can establish that relationship. The optional shared helper adoption gaps are
listed under [Framework feature adoption](#framework-feature-adoption).

## Synthetic mutation fuzz testing (#40)

Two independently authored `SyntheticSave` seeds, `dis.dat` for PPSA02225 and PPSA26344, cover
Tsushima and Yōtei respectively. The existing builders create inventory tables and repair the real
checksums; Yōtei also has statistics, gear and secondary tables. Both baselines open and render.
Mutations retain the real checksum gate, so covered-byte damage usually rejects before table
decoding. No private bytes or authentication material are embedded.

`MutationFuzzSyntheticSeedsOpenAndRender` checks every unmutated seed before the inherited
`MutationFuzz` fact supplies deterministic mutations. No samples are required for these checks.
