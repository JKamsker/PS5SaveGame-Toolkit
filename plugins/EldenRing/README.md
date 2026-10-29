# Elden Ring (`eldenring`)

**Experimental**, v0.7.0, for character/profile formats **150/90**. Enables mirrored names,
bounded ordinary-stack quantities, both rune-history mirrors, and shared F6 refill/linked-rune
actions alongside the existing held-rune edit. New writes are **Inferred**, with explicit warnings:
there is no console validation and the game may reject edited saves. Owner validation is tracked
separately in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).
Completion adds read-only F9 progress maps and an attribute dependency graph, attributed class/progress
references, and F8 regulation browsing. All six existing write operations retain their warnings and limits.

## Titles and file roles

| PS5 retail title | Region | Sample coverage |
| --- | --- | --- |
| PPSA03169 | JP | One snapshot: primary plus identical console backup |
| PPSA04608 | Asia | Registered title; layout compatibility inferred, no sample |
| PPSA04609 | EU | Registered title; layout compatibility inferred, no sample |
| PPSA04610 | US | Registered title; layout compatibility inferred, no sample |

IDs were checked in PROSPEROPatches. Network Test titles are excluded. The plugin opens
`sce_sdmemory/memory.dat` (imported directory `sdimg_sce_sdmemory`, file `memory.dat`), or the
nested path `sce_sdmemory/memory.dat`. Backups open independently; export backup mirroring is
framework-owned. System files, account identifiers and multiplayer passwords are not decoded
by the semantic reader or evidence decoder. No sample bytes are shipped.

## Layout and preservation

Little-endian unless stated otherwise. Every unknown byte, inactive character, padding region,
encrypted regulation allocation and appended tail is preserved in the original allocation.
Serialization patches only explicitly supported fixed-size fields, reparses character/profile and
semantic-container invariants, and returns a copy. Failed batches restore the complete pre-edit bytes.

| Offset / size (hex) | Role |
| --- | --- |
| `0 / 70` | PS header: magic `2C9C01CB`, sentinel `7F7F7F7F` at `08`; remaining words opaque |
| `70 + index * 280000 / 280000` | Ten character slots, no PC per-slot MD5 prefix |
| `1900070 / 60000` | User data 10: version, opaque settings, menu, active flags, summaries |
| `1960070 / 240010` | Preserved regulation allocation (opaque 16 bytes, IV, AES payload) |
| `1BA0080..EOF` | Preserved uninterpreted trailing data |

Character prefix: version `+0`, opaque map `+4`, elapsed milliseconds `u64 +8`, GaItem map
`+20`. Walk 5,120 variable entries (5,118 for <=81): eight-byte handle/ID header, plus 13 bytes
for nonzero weapon IDs (top nibble zero), eight for armour (`0x1…`), none otherwise. This locates
PlayerGameData without name scanning. Legacy layouts have synthetic prefix coverage only.

PlayerGameData: attributes `u32 +34` (Vigor/Mind/Endurance/Strength/Dexterity/Intelligence/Faith/Arcane),
level `+60`, held runes `+64`, lifetime rune total `+68`, UTF-16LE name `+94` (16 code units plus
terminator). Active profile summaries cross-check name, level, elapsed whole seconds and history.
Level must equal `sum(attributes)-79`, attributes must be 1–99, and held runes must not exceed
999,999,999 or lifetime history. These are supported parser invariants, not universal repair rules.

The menu header at `19001BC` has a bounded payload-length word. Ten active bytes follow its
eight-byte header and payload; summaries are `24C` bytes each. Sample flags are at `19019C4`,
summaries at `19019CE`; summary level/time/history offsets are `22/26/2A`. Inactive residue is not
interpreted. Other structurally accepted character versions retain prefix views but no deeper reading.

## Bounded semantic reader (Phase 1)

Deeper reads are gated to character format **150**. `ItemMapReader`, `SlotCursor`,
`InventoryReader` and `SemanticReader` compose bounded ranges; no full save reserializer exists.

- PlayerGameData is `1B0` bytes. A `D0` gap precedes EquipData (`58`), ChrAsm (`74`) and ChrAsm2 (`58`).
- Held inventory has fixed common/key capacities 2,688/384; storage has 1,920/128. A row is
  `(u32 handle, u32 quantity, u32 inventoryIndex)`. These are technical array capacities, **not legal
  item quantity caps**. Counts larger than capacity and variable counts extending beyond the slot fail.
- Every physical row is retained. Confirmed all-zero vacancies are hidden by default through F14,
  with the hidden count and a Show toggle; shown vacancies read "Empty slot" and keep their raw cells.
  Every nonzero row, including zero quantities, sentinel/unknown handles and count mismatches, remains
  visible. Count mismatches warn without repair.
- Goods `0xB…` and talismans `0xA…` normalize to category-qualified IDs `0x4…` and `0x2…`.
  Weapon/armour/AoW handles resolve through a unique GaItem entry; missing, duplicated or
  category-inconsistent map references stay raw. Upgrade/affinity IDs are never silently rounded down.
- Document row keys contain character/container/array/physical-record/handle/inventory-index identity.
  Duplicate item names or duplicate handles do not collapse rows. F1 uses handle/index/occurrence
  identities to align moved physical rows; it does not claim persistence across container transfers.
- HP/FP/stamina current/effective-max/base-max are read separately (`+08/+14/+24` for their triples).
  The archetype byte is `+B7`, independently tested against the nearby gift byte `+BB`.
- Equipment representations have a read-only relationship table and independent raw references with
  Inferred labels; no equipment write is enabled. Acquisition/event ranges are bounded candidates,
  with no verified boss/grace/completion or any quest/NG+ semantics. Source event candidates are
  exposed separately in the read-only progress view below.
- Acquisition history is an eight-byte header plus **7,000 sixteen-byte rows**, not eight-byte rows.
  Its distinct count is bounded to capacity. Correcting the old stride moves the sampled event range
  forward 56,000 bytes to `0x36600`; no save bytes are changed by this reader fix.

Sample assertions reproduce PlayerGameData `A2FF`, held inventory `A6A3` (63 common / 2 key rows),
empty storage `13A5E`, two Scimitars and the shield, Rowa Fruit ×12, Smithing Stone [1] ×3,
Cracked Pot ×1 and the west Limgrave map. Names come from the attributed upstream name catalog, not
in-game screenshots. Sample HP is **504/504/476**, FP **88/88/88**, stamina **97/97/97**; no base-stat
reset is inferred from those observations. See [Evidence/README.md](Evidence/README.md).

## Reference data and rich browsing (Phase 2, partial)

F2 embeds `eldenring.names.en` v3.0.0, dataset/catalog **`eldenring-items`**: the **complete 5,770
literal ID/name pairs** from ClayAmore/ER-Save-Editor revision
`014107f0ca1cff867b5f7565cb79d6684f9b35df`, using MIT under the owner policy on issue #14.
This replaces the six-item sample pack. Completeness refers to these pinned tables, not all game
builds/DLC. Names remain Inferred on PS5. Selected exact-ID/name matches now include official game
descriptions and icons under the owner decision of 2026-10-29; no legal quantity caps are asserted.

| Constituent table | Records | Empty upstream names | Canonical ID prefix |
| --- | ---: | ---: | --- |
| `weapon_name.rs` | 2,974 | 98 | `0x00000000` |
| `item_name.rs` | 1,808 | 143 | `0x40000000` |
| `armor_name.rs` | 653 | 30 | `0x10000000` |
| `accessory_name.rs` | 117 | 1 | `0x20000000` |
| `aow_name.rs` | 218 | 1 | `0x80000000` |

Names (including accents), unused/test entries and all 273 empty names are retained. Empty names
display the raw canonical key and preserve `upstreamName=""`. Out-of-category sentinels such as
goods ID 999999999 use `goods:upstream:999999999`, never a fabricated canonical save ID. No item
eligibility, ownership, upgrade ceiling or release/DLC coverage is inferred from presence in this list.
Unknown IDs and empty reference contexts retain exact raw fallback; upgrades are not rounded silently.

Each constituent has a source sidecar/hash; every record names its exact upstream file and revision.
The identity labels retain MIT attribution, LF-normalized content hashes and an adjacent full MIT
notice. The importer [Evidence/import-name-catalog.py](Evidence/import-name-catalog.py) only parses
reviewed scratch text with pinned SHA-256 checks; run it using `python -I <script> <scratch-directory>`.
It does not download or execute upstream code. Offline tests independently pin sorted ID/name digests
for all five tables and traverse every F8 page, including affinity variants and category collisions.

`Open(file, IReferenceContext)` pins the catalog using the **64 lowercase hex SHA-256 of the complete
preserved regulation allocation** as its compatibility key. This is a content fingerprint, not a guessed
app patch or regulation version. An audited exact pack uses that key in `gameBuilds`; an unrelated
build-specific pack is skipped. A changed IV/padding also changes this conservative key. Build-agnostic
packs may supply identity labels only; the bundled catalog applies to all four registered titles without
claiming that their layouts or individual records are sample-verified. The Sources panel records
resolved data; field descriptions, row details and the picker carry provenance. Optional imported icons
use the SDK asset service and show their separate asset attribution. Selected bundled content is described below.

F8 publishes server-backed held/storage tables per active sampled character and a reference-catalog
table. Search, exact column filters and deterministic sorts run over the complete collection before
paging (maximum 200 rows, stable keys, numeric quantity/index/handle sorts). The catalog lazily indexes
bounded record metadata, at most the framework's 250,000-record ceiling; it never eagerly creates view
nodes for the entire pack. Whole-filtered selection remains owned by F8; refill actions in table cells
use F6's resolved row keys, exclusions and revision checks, including rows not yet loaded by the UI.

F4 `ReferenceSpec` fields show exact canonical IDs and attributed names. Inventory references are
read-only. **Inspect an item reference** uses the shared searchable picker and returns provenance
without changes, dirty state or clearing rune undo history. On read-only document versions, the F8
catalog remains available; Core currently blocks all actions when `CanEdit=false`.

## Read-only regulation directory

Lazy inspection decodes the sampled public game-format AES-256-CBC/IV framing, validates the exact
DCX/DFLT header and declared sizes, and caps zlib output at **64 MiB**. The sample decompresses
1,859,344 bytes to **37,745,424** bytes. Other codecs/headers are refused with a warning; raw inspection
and save export remain available. Encrypted bytes are never rewritten.

The reader validates the sampled BND4 format's byte order, directory/name/payload bounds and overlaps,
then PARAM long-offset row directories, type-tag termination and schema flags. It finds **186 tables**;
BND version `11001000` is **not** an application patch. The actual entry encoding is bit-reversed `0x2E`
(stored `0x74`), with **32-bit data offset plus ID**, established from bytes rather than assuming all
upstream flag code transfers. Duplicate PARAM row IDs occur in the sample and are retained. The `0x80`
type-pointer flag makes its long type offset authoritative; three Cutscene tables have stale legacy
string words beyond file length. Synthetic tests cover these cases.

This validates directory structure, **not PARAM field layouts, parameter-derived legal caps, FMG text,
DLC schema compatibility or writer safety**. Those parts of Phase 2 remain open. There is no native code,
SoulsFormats/Paramdex dependency, unsafe cast, network access or plugin filesystem I/O.
The 186-table directory now uses F8 search/filter/sort/paging (`regulation/tables`), including numeric
row-count/size sorts. Its collection is published only after lazy inspection has decoded metadata;
initial views never decompress regulation. Metadata is cached once per document, without retaining
the 37 MiB decoded allocation. Unsupported regulation still produces a warning and an empty query.

## Read-only progress maps and character graph (F9)

The separate `eldenring.progress.en` v1.0.0 F2 pack embeds **499** pinned MIT source candidates:
**314 graces**, **157 bosses** and **28 map controls**, plus **10 starting-class references**.
Datasets are `eldenring-progress` and `eldenring-classes`. The names, explicit source region/group,
event ID, sparse relative byte offset and LSB bit are preserved; offsets are **not** `eventId / 8`.
All source files/hashes, joins, modifications and the full MIT notice accompany the pack in
[progress.source.md](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.progress.en/progress.source.md).
The reproducible [importer](Evidence/import-progress-catalog.py) reads reviewed, hash-pinned scratch
text using `python -I`; it never executes downloaded code. Exact `upstreamName` metadata preserves
one boss label's trailing space, although F2 trims the displayed name.

**World / progress** is optional spoiler content. Each sampled-format character has an F8 global
candidate table (`slots/<slot>/progress`) with search, category/region/raw-bit filters and numeric
event-ID sorting. Lazy regional sections expose F9 maps and linked raw-bit tables, at most **100**
entries per section. Each marker has a stable character/source identity, Inferred confidence,
uncertainty/provenance, a text alternative and a field target with its full tab/section/row path.
The linked eager regional row resolves immediately; it does not rely on finding an unloaded F8 row.
Malformed/duplicate/oversized imported mappings are rejected with diagnostics; a missing pack retains
an honest empty collection and raw fallback. Unsampled character layouts do not expose these reads.

Every marker status is **Unknown**, even when its raw source bit is set. Bits are read-only candidate
observations, not a PS5 discovery/defeat/completion claim. The sample reproduces source bits 76100/76101
set and 76102 unset at the corrected range; no independent before/after or menu observation exists.
Map controls include viewport/layer controls and are not all collectible fragments. Inventory ownership,
historical collection, consumed/sold rewards and boss/quest completion are never equated. Summoning pools,
quests, blessings, NG+ resets and mutually exclusive world states remain unmodeled. Additional
community DLC/NG+ source candidates are available below; their semantics remain Inferred.

Maps link to [Map Genie](https://mapgenie.io/elden-ring/maps/the-lands-between) with host/attribution
through the shared renderer. Selected exact event-ID joins now place candidates on local, attributed
surface/underground/DLC map art. Alignment stays Inferred; absent, ambiguous and other-layer candidates
retain the complete accessible list and raw-bit table. Missing packs retain the external/list fallback.
No plugin network access is added. The asset round below records the transform and limitations.

The character's F9 **Attribute-to-level dependencies** graph has eight existing attribute-field nodes
feeding level (`sum − 79`), with field navigation and a complete text alternative. Unknown graph state
means calculation dependencies rather than unlock progress. No quest prerequisites, upgrade paths or
modifier/vital derivations are invented. All affected fields are read-only, so no map/graph state can
become stale after an enabled write or undo.

F4 now also covers class candidates, progress identities and resolved equipment identities, alongside
inventory and inspection. Raw numeric class/equipment representations remain diagnostic values; semantic
references retain their exact catalog keys and raw fallback. Starting class values are attributed source
references, never asserted legal respec minima or used to enable a stat writer.

## Framework feature adoption

| Feature | Status | Where / concrete reason |
| --- | --- | --- |
| F1 SaveDiff | Added this round | SDK-only `eldenring-evidence` hook, `Evidence/fixtures.json`, endpoint privacy masks and intended-only byte diffs. Existing paths and privacy allowlist retained; fixtures now use the shared hook adapter. |
| F2 reference packs | Used | Complete 5,770 identities, 1,245 official descriptions and illustrated records, 1,096 deduplicated icons, 597 inferred placements and three backgrounds; separate text/image/coordinate attribution. Progress/classes/community packs retained. |
| F3 rich cells | Used | Catalogue and inventory details display bundled attributed icons and official text. Inferred/Unknown badges, refill actions and raw identity fallback retained. |
| F4 reference pickers | Added this round | Class/progress/equipment semantic identities now join inventory and inspection through `ReferenceSpec`; F13 provides browse-only lookup on read-only fields/saves. Immutable raw diagnostics retain their numeric encoding. All current semantic references preserve unknown IDs. |
| F5 structural undo | N/A | No safe structural writer exists: free-handle allocation, variable GaItem growth, counts/acquisition and dependent equipment cleanup are unproven. Fixed-size scalar edits already have exact undo; arbitrary insertion/removal would corrupt unknown structure. |
| F6 action planning | Used | Held-only runes, linked rune/history and supported-stack refill; pure previews, warnings/skips, semantic review, stale/tampered-plan rejection, atomic group undo and #27 whole-query selections/exclusions. Respec/loadout presets lack complete dependency writers. |
| F7 document groups | N/A | Every enabled operation changes one `memory.dat`; there is no independently coupled character/profile partner file. Originally identical backups are mirrored atomically by Core export, not a new plugin-owned group. |
| F8 queryable tables | Added this round | Existing inventory/storage/5,770-item catalog, new 499-entry progress table and formerly eager 186-table regulation directory. Remaining eager regional/equipment/raw lists are bounded to at most 100 rows. |
| F9 maps/graphs | Used | Local surface/underground/DLC art with inferred event-ID positions, bounded regional candidates, Unknown states and raw-field targets. Missing/unpositioned candidates retain accessible lists; attribute-to-level DAG retained. Spatial validation, protocol, Core navigation and preservation tested. |
| F10 exact/nullable/union values | Used | UInt32 fields and UInt64 millisecond durations use exact SDK integer transport. Decimal lexemes/nullable/union specs are N/A: modeled scalars are binary integers or fixed text without tagged/null states; inactive slots omit fields. |
| F11 shared codecs | N/A | This format uses public AES/DCX/DFLT plus BND/PARAM directories; it has neither Kraken nor SQLite. BCL AES/ZLib remain bounded and preserve original encrypted bytes; no redundant shared-codec route or dependency added. |
| F12 decoder hooks / F8 traversal | Added this round | Adopted merged #32: `EldenRingPlugin` publishes `eldenring-evidence` through `ISaveDecoderProvider`; shared `HookSaveDecoder` replaces the test-only decoder. Shared view traversal is tested across 203 inventory rows with 17-row pages and explicit truncation. CLI discovery and fixture selection verified. |
| F13 read-only inspection / browsing | Added this round | Adopted merged #33: `references/inspect` has `Mutates=false` and is published on read-only saves. Shared F4 category filtering/Key–Name–Category sorting and immutable browse-only fields are tested through Core; inspection preserves bytes, revision and undo/redo. Float duration is N/A because saved play time is exact UInt64 milliseconds with existing integer duration formatting. |
| F14 empty slots | Used | Held/storage retain every physical slot in their existing single F8 tables; all-zero vacancies are flagged and hidden by default, with matching hidden counts and raw details. Inactive profiles are flagged in their eager table. F6 query scopes exclude hidden vacancies; SaveDiff view traversal explicitly includes them. |

## F14 inventory and table audit

Held and storage already used one searchable/sortable F8 table per character/container, without
"Slots N–M" sections. Previously the reader omitted twelve-byte all-zero records; it now retains
all **3,072 held** and **2,048 storage** slots with unchanged physical record identities and offsets.
Both collection and view metadata publish `EmptyRows`; direct and Core queries default to hiding
vacancies, filter the complete model before paging, and return matching `HiddenEmptyCount` values.
Shown vacancies read "Empty slot" while preserving raw handle, quantity, index and bindings.
The supplied PS5 snapshot has **65 visible / 3,007 hidden held** rows and **0 visible / 2,048 hidden
storage** rows. Positive quantity edits cannot create or fill a vacancy, so visibility stays stable.

The rule is deliberately limited to `(handle, quantity, inventoryIndex) == (0, 0, 0)`, matching
the pinned [ClayAmore serialized defaults](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/common/save_slot.rs)
and actual sample bytes. Rechecked 2026-10-29 against the same revision's
[inventory reader](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/vm/inventory/mod.rs)
(MIT); no new upstream implementation was copied. Its broader unknown-category fallback is not
proof of vacancy: nonzero unknown handles, `0xFFFFFFFF`, zero quantities and residue remain visible.

The eager profile summary also flags inactive character slots, retaining all ten keys. Equipment
relationships and raw representation tables remain visible because empty/unarmed encodings are
explicitly unverified. Catalogues, regulation directory, semantic ranges and progress/reference
lists contain no fixed inventory vacancies; regional progress tables also serve F9 map targets.
No slot-range inventory groups were present or introduced. Document-scope refill skips vacant
slots; explicit shown-slot selections can still report them as protected. The F1 evidence hook
keeps its occupied-identity alignment, while general SaveDiff view traversal includes every slot.

## Writes and integrity

The PS wrapper has no PC per-slot/profile MD5: ClayAmore's PS writer concatenates preserved header,
common slots and user-data blocks directly, while its PC wrapper adds MD5. This agrees with sampled
boundaries; independent restricted-source research corroborates the format but contributed no code.

The owner's 2026-10-28 decision, **“Enable writes. Keep warnings.”**, replaces the prior evidence gate.
Unknown character/profile versions remain read-only because their writable layouts are not modeled.
No additional integrity algorithm is known for this PS wrapper. Every writable field/action and F6
preview warns that unknown integrity/dependency rules may cause game rejection. No checksum is invented,
and the regulation bytes remain unchanged. Only the owner may promote maturity to Stable.

## Enabled writes and their evidence

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| Held runes (existing field/F6 action) | Inferred write semantics | Pinned ClayAmore layout; sample 1147 held / 4427 history | One u32; final held <= history and 999999999; parser/semantic reparse; unknown rules may cause rejection | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): load, balance, resave/reopen/reload |
| Live/menu name | Inferred | Two source-guided UTF-16LE buffers, sample mirror agreement; Unicode/16-unit/padding regressions | Both buffers and terminators updated; strict surrogate/control validation; original trailing bytes retained; exact undo; unknown rules may cause rejection | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): Unicode, shortening and 16-unit names in both menus |
| Existing ordinary-stack quantity, held/storage | Inferred | Direct goods rows, Rowa Fruit `0x400050F0` observed 12; Smithing Stone [1] `0x40002774` observed 3; storage layout source-guided/synthetic only | One u32, minimum 1, observed ceilings 12/3; handles/counts/acquisition preserved; legal caps/integrity unverified and game may reject | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): consume/refill each ID; nonempty storage; legal caps |
| Manual lifetime-history total | Inferred | Player `+68`, menu `+2A`, sample agreement at 4427 | Both u32 mirrors updated; must cover held runes; ceiling max(imported history, 4427); no per-save accumulation; unknown rules may cause rejection | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): earn/spend/death/recovery/loss and menu persistence |
| F6 linked rune/history increase | Inferred | Same fixed offsets/invariants; sample-derived lower-history transition, no controlled gameplay pair | Preview lists held balance and derived history; history only raised to cover requested balance; both copies; atomic/group undo; unknown rules may cause rejection | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): balance above prior history, load/resave/reload |
| F6 refill supported stacks | Inferred | Same finite two-ID allowlist and observed ceilings | Preview lists old/new values, skipped rows and warnings; scopes/exclusions checked; one atomic undo unit; unknown rules may cause rejection | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): two-row refill, filtered selection and undo/redo export |

Names accept 1–16 UTF-16 code units, including valid surrogate pairs; whitespace-only names, embedded
NUL/control characters and unpaired surrogates are rejected. Each mirror retains its own original
trailing bytes after the new terminator. Reconstructing from those original buffers makes even
longer → shorter → undo sequences byte-identical without erasing unexplained padding.

Quantity eligibility requires an existing positive common-array direct-goods row, unique handle in
that container, matching declared counts, one of the two IDs, and an imported quantity within its
observed ceiling. Technical array capacity is never used as a stack cap. Zero, larger imported stacks,
key/story/container-managed items, unknown IDs and ambiguous/residual rows remain protected because
their write would require unmodeled removal/dependency handling. These ceilings are conservative
sample observations, **not legal carry/storage limits**. Storage coverage is synthetic; no nonempty
private storage snapshot exists. No transfer, insertion, removal or automatic equipment change occurs.

Manual history and linked writes are bounded by the larger of imported history and the sample's
4427, with held balances additionally capped at 999999999. A direct balance above current history
rejects atomically; use the linked preview to raise both copies. Existing held-only F6 behavior keeps
the current-history cap and never changes history. Serialization itself never increases totals.

### Phase 3: shared F6 review for held runes

Each active character exposes **Review held-rune change**, using `IActionPlanningDocument` and
`ActionNode.SupportsPlanning`. Preview lists the old/new balance and unchanged-history warning,
without touching bytes or undo state. Apply recalculates the proposal, validates the same
`min(existing lifetime total, 999999999)` cap, rejects altered/stale effects and uses the ordinary
scalar binding. Core provides revision tokens, semantic review and one-step undo/redo. Direct action
invocation is rejected; row/query scopes cannot accidentally target another character. No-op previews
create no undo step. The sample regression proves exactly the existing four-byte patch, preserves
HP **504/504/476**, both history mirrors and all regulation/unknown bytes, and restores exact bytes on undo.

F6 also exposes **Review linked rune/history increase** and **Refill supported ordinary stacks**.
Both reject direct invocation, revalidate complete changes/warnings/skips on apply, bind unopened
fields for undo and use shared semantic review. Refill supports document scope, explicit row selection
and F8 whole-query selection; an empty selection changes nothing. Skipped rows explain protected
entries and stacks already at the observed ceiling. No plugin-local preview/history substitute exists.

### Phase 4: existing equipment relationship checks

The character view joins **34** candidate gear/quick/pouch slots against the held common array,
with names, raw handles, positions, IDs, status and attribution. It compares:

- `EquipData`: physical common-array record **+0x180**, distinct from the acquisition `inventoryIndex`.
- `ChrAsm2`: instance/direct handle; a unique held row and resolved category are required.
- `ChrAsm`: category-stripped ID after seven selector words, for gear.
- `EquippedItems`: category-qualified ID, including armor and talismans.
- Quick/pouch records: `(handle, position)` pairs plus their `EquippedItems` copies.

The sample has **17 agreeing nonempty slots** and 17 unassigned candidates. Its two Scimitars retain
separate handles/record keys; the first has position `0x182`, physical record 2 and acquisition index
`0x24D`. The helmet joins to Blue Cloth Cowl; armor/talisman ID prefixes are checked independently.
Missing/ambiguous/zero-quantity/wrong-category references and mirror disagreements remain visible,
unaltered and protected. Independent raw representations and unknown words remain inspectable.

Equipment/loadout writes remain unavailable because the reader does not yet model the additional
profile-summary equipment copies, empty/unarmed encoding, slot capacity and modifier updates. Changing
only the displayed joins would leave inconsistent mirrors. Upgrade/affinity/AoW changes lack compatible
PARAM rules and maximum-ever upgrade-history modeling. Attributes/respec have no correct modifier-aware
vital transaction: resetting the sample's **504/504/476** HP to one base value would destroy saved state.
These are missing writer/dependency implementations, not a wait for owner evidence alone.

Structural add/remove/transfer remains unavailable: free-handle allocation, variable map growth,
counts/acquisition indices and dependent equipment cleanup are unmodeled, so an arbitrary patch could
corrupt unknown structure. There is no prepared F5 or F7 writer to enable. Current writes affect one
`memory.dat`; independent backups retain framework-owned export mirroring. No cross-file transaction
has been invented. Progress/quest writers lack verified entity semantics and dependency transactions;
the new source candidate lookups are read-only.

## Validation

Completion round, verified on 2026-10-28: **214 plugin tests passed / 0 failed / 0 skipped** with private
samples; **282 Core tests passed / 0 failed / 0 skipped**. API build succeeded with zero warnings/errors.
The full-catalog sample initial-view test passes the plan's **under two seconds** target, including
both embedded packs and document open. Missing-sample behavior is checked separately and never
substitutes for sample-backed validation.

Run with `SAVETOOLKIT_ARTIFACTS` pointing to the private artifacts folder:

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP = 'D:/Temp/saveeditor/test-scratch'
$env:TEMP = 'D:/Temp/saveeditor/test-scratch/EldenRing'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
dotnet test --project plugins/EldenRing/SaveToolkit.Plugins.EldenRing.Tests
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

The suite covers conformance, byte-identical sample/backup export, four-byte rune scope and undo,
semantic offsets/identities, malformed counts, duplicate/unknown handles, privacy-filtered F1 fixtures,
F2 pack validity and exact compatibility selection, F4 session routing/undo preservation, F8 complete-set
paging/search/filter/sort, bounded DCX/BND/PARAM rejection and real regulation directories.
Round 2 adds complete-source catalog digests/provenance, all 5,770 paged identities, equipment
position/ID/handle joins and ambiguity rejection, F6 purity/bounds/staleness/review and exact undo.
The write-enable round adds sample-backed mirrored-name/history and two-ID quantity persistence,
F1 intended-only byte ranges, Unicode/padding/terminator rejection, protected-row rules, live quantity
filtering, linked derived changes, grouped refill undo, query exclusions and stale/tampered-plan rejection.
Completion adds independent progress-name/event digests, all map targets through Core, F9 JSON
round trips, exact sparse-bit/stride vectors, bounded malformed pack/range rejection, class/raw fallback,
F4 equipment identity protection, optional F2 image attribution and lazy F8 regulation paging/undo.
F12/F13 tests cover production decoder registration/budgets/privacy, multi-page shared view traversal,
read-only inspection and native picker filtering/sorting without changes to bytes, revisions or history.
F14 adds seven cases covering collection/view metadata, inactive profiles, matching hidden counts,
raw empty cells, unknown/zero-quantity residue, every slot across pages, sparse visible edits and exact
undo, and F6 select-all-matching visibility/exclusions. The sample asserts all 5,120 physical inventory
records and its 3,007/2,048 vacant counts. Validation on 2026-10-29: **254 passed, 0 failed, 0 skipped**;
API build: **0 warnings, 0 errors**.
Repeated handle/selection scans now use lookups/sets; empty edit batches avoid a full-save copy/reparse.
Sample-dependent cases skip when samples are absent. Current results are recorded in the issue #14
final implementation comment; no binary sample or extracted regulation is included.

## Sources and attribution

Initial sources inspected **2026-10-28**; community-data round **2026-10-29**.
Downloaded code was read as untrusted text, never executed.

| Dataset / code port | Author / source | Licence / modifications / coverage |
| --- | --- | --- |
| PS prefix/profile, bounded GaItem/inventory/equipment reader | [ClayAmore/ER-Save-Editor, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/tree/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save) | Dual MIT/Apache-2.0; MIT option retained in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Adapted only read algorithms, bounded ranges and ID-space facts; sample v150 only for deeper reads. |
| F14 vacancy/default research | [ClayAmore serialized inventory/defaults, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/common/save_slot.rs), [inventory reader, same revision](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/vm/inventory/mod.rs) | MIT, rechecked 2026-10-29. Existing twelve-zero-byte vacancy rule checked against sample bytes; original F14 presentation/query integration, no new code port. Unknown-category fallback is not used as proof of vacancy. |
| AES/DCX/BND/PARAM directory format | [ClayAmore util, same revision](https://github.com/ClayAmore/ER-Save-Editor/tree/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/util) | MIT option, same notice. Narrow managed read-only port, sample-verified flag corrections, bounded decompression, no unsafe field/schema port. Public format key is not a user credential. |
| Weapon names/IDs: 2,974 rows | [ClayAmore / ER-Save-Editor, weapon_name.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/weapon_name.rs) | MIT; all literal pairs, category-qualified IDs, names verbatim, raw labels for empty names; no upgrade legality inferred. [Sidecar](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.names.en/weapon-names.source.md). |
| Goods names/IDs: 1,808 rows | [ClayAmore / ER-Save-Editor, item_name.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/item_name.rs) | MIT; all literal pairs, goods prefix, names verbatim, out-of-category sentinel retained separately; no caps/descriptions. [Sidecar](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.names.en/item-names.source.md). |
| Armor names/IDs: 653 rows | [ClayAmore / ER-Save-Editor, armor_name.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/armor_name.rs) | MIT; all literal pairs, armor prefix and unchanged names. [Sidecar](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.names.en/armor-names.source.md). |
| Talisman names/IDs: 117 rows | [ClayAmore / ER-Save-Editor, accessory_name.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/accessory_name.rs) | MIT; all literal pairs, talisman prefix and unchanged names; no slot eligibility implied. [Sidecar](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.names.en/accessory-names.source.md). |
| Ash of War names/IDs: 218 rows | [ClayAmore / ER-Save-Editor, aow_name.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/aow_name.rs) | MIT; all literal pairs, ash prefix and unchanged names, including tests; no compatibility inferred. [Sidecar](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.names.en/aow-names.source.md). |
| Read-only equipment relationship checks | [ClayAmore / ER-Save-Editor, vm/equipment.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/vm/equipment.rs) and same-revision `save/common/save_slot.rs` | MIT; adapted position/handle/ID relationships with explicit uniqueness/category checks; no writer, unsafe indexing or repair ported. Same full MIT notice. |
| Fixed-size write layout/evidence | [ClayAmore / ER-Save-Editor, save_slot.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/common/save_slot.rs), [PlayStation user_data_10.rs](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/playstation/user_data_10.rs) and local [baseline measurements](Evidence/README.md) | MIT layout facts; original bounded in-place name/history/quantity writers and F6 plans, strict Unicode, preserved padding, staged rollback and sample-observed ceilings. No new game data or external implementation is embedded. Full notice retained. |
| PS checksum / legacy-count research only | [Hapfel1/er-save-manager, 5974934](https://github.com/Hapfel1/er-save-manager/tree/5974934537feeace982721f82f0fbe69c20c208d) | Restricted/source-available current licence: no code translated, copied, executed or distributed. Facts independently checked against permissive code and sample bytes. |
| Game-data generator research | [ERDB, e2028a6](https://github.com/EldenRingDatabase/erdb/tree/e2028a6e044b920a471388fb4e1c468b31b64350) | Generator MIT does not license publisher assets. Research lead; the selected game content below comes from a separately pinned Compass extraction under the 2026-10-29 owner policy. |
| Official game text/icons, map art and placement facts | [EthanShoeDev / elden-ring-compass, be49e96b](https://github.com/EthanShoeDev/elden-ring-compass/tree/be49e96b0513d8e08d22806b5b3fee9654e3e9de), underlying game content © FromSoftware / Bandai Namco | Rights reserved by FromSoftware / Bandai Namco; used with attribution per owner decision 2026-10-29. Exact source/selection, original input hashes, separate asset/coordinate provenance and transformations in [visuals.source.md](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.names.en/visuals.source.md). No upstream implementation copied/executed. |
| Community item identities/groups: 3,221 rows | [TGA, 393c439e](https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/tree/393c439e72ad48128efdc5e30fc3feb4bfbd210f); inherited [ClayAmore items.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/items.rs) | No licence published; reused with attribution per owner decision 2026-10-28. Names/category facts and literal memberships only: 19 inherited groups, 545 source occurrences / 544 distinct memberships. No spawn quantities/code/descriptions/art. |
| Base/DLC/NG+ flags: 1,748 candidates | [TGA flag manager, same revision](https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Event%20Flags/Event%20Flag%20Manager/.cea), Dasaav/Umgak; [ClayAmore ER-Save-Lib block map, 664ff9e8](https://github.com/ClayAmore/ER-Save-Lib/blob/664ff9e83b7022a6fdc5a8332e8b7b22dce34e0f/src/res/eventflag_bst.txt) | Same no-licence owner decision. Literal event/group labels plus sparse addressing facts; original bounded reader, no predicates, runtime pointers or writers copied. All statuses Unknown. |
| PARAM layouts: 1,072 records | [soulsmods / Paramdex, ff7245e5](https://github.com/soulsmods/Paramdex/tree/ff7245e524329bc3eab00036723d2bd53384cedf/ER) | Same no-licence owner decision. Ten technical field layouts. Unused standalone row-name dump removed in v1.1.0. XML descriptions/display strings/defaults/editor bounds excluded. No field values decoded or caps inferred. |
| Title identity research | [JP](https://prosperopatches.com/PPSA03169), [Asia](https://prosperopatches.com/PPSA04608), [EU](https://prosperopatches.com/PPSA04609), [US](https://prosperopatches.com/PPSA04610) | Title/region facts only. No assets copied. |
| Progress names/regions/events: 314 graces, 157 bosses, 28 map controls | [ClayAmore db, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/tree/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db): `graces.rs`, `map_name.rs`, `bosses.rs`, `maps.rs`, `event_flags.rs`; same-revision `vm/events.rs` / `util/bit.rs` | MIT; explicit sparse joins and original labels/source boss groups; `upstreamName` preserves exact whitespace. Added schema, offsets/bits, provenance and uncertainty; no art/coordinates, pools, quest prerequisites or PS5 completion claims. [Sidecar and hashes](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.progress.en/progress.source.md), separate F2 dataset and full MIT notice. |
| Ten starting-class names/values | [ClayAmore classes.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/classes.rs) | MIT; explicit enum IDs and starting-value tuples, separate `eldenring-classes` dataset. No class/respec writes or asserted legal minima. Same sidecar, source hash and full notice. |
| External map reference | [Map Genie, Elden Ring](https://mapgenie.io/elden-ring/maps/the-lands-between) | Link only; reuse licence unverified. Host and attribution shown through F9; no art, coordinates, labels, descriptions or external resources copied from this site. |

The [plan](../../docs/plans/EldenRing.md) records broader research and provenance exclusions.
The owner's 2026-10-28 decision also unblocks unlicensed community data. Paramdex/TGA and the
ER-Save-Lib block map are now embedded with their own attribution, never relabelled as MIT.
Restricted er-save-manager remains excluded. Selected publisher text/art is now imported with attribution
under the owner decision of 2026-10-29; this does not relicense it as MIT.

## Visual assets (2026-10-29)

`eldenring.names.en` v3.0.0 retains all 5,770 original identities and adds **1,245 official descriptions
and illustrated records**: 437 base weapons/shields, 593 common goods/key/crafting/upgrade items,
115 talismans, nine starter armour pieces and 91 Ashes of War. Icons appear on the actual F2 records,
in browse-only F4 pickers and in F3 catalogue/inventory details. Joins require exact parameter ID and unchanged name,
apart from the reviewed punctuation alias on goods 8600 (`Map: Limgrave- West` / `Map: Limgrave, West`).
Affinity/upgrade variants do not inherit guessed content. Existing limits and write warnings remain.

**1,096 deduplicated 64px WebP icons and three 1312×1312 backgrounds total exactly 2,640,798 new asset bytes**.
The backgrounds cover the Lands Between surface/underground and Realm of Shadow surface. A separate
597-record F2 placement dataset joins exact event IDs to source grace/boss coordinates. Native master pixels
use `X=worldX-7168`, `Y=16640-worldZ` over 10496-square bounds; tile art is reduced to 1/8 native size.
Legacy dungeons use source nearest-basepoint translations, so all placement remains **Inferred**.
Each regional node selects its dominant layer; other-layer/missing/ambiguous candidates remain unpositioned.
M11 and map-fragment pickup placement are unsupported. All statuses remain **Unknown**.

Publisher rights are retained under the owner's attributed-use decision of 2026-10-29. Separate text,
image and coordinate provenance appears in F2 Sources, UI details/maps, `Info.References`, notices and the
[source sidecar](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.names.en/visuals.source.md).
Original-byte source pins and an offline reproducible importer live under `Evidence/`; image processing
happens in scratch, and only validated final WebP bytes enter the plugin. Framework pack loading happens
once; map-catalog resolution is lazy, and document asset reads return the pinned cached content.
Missing images retain a schematic/list; missing coordinates retain the original external/list presentation.

See [asset-round validation and first-load measurements](Evidence/ASSET_ROUND.md).

## Unlicensed community data round

`eldenring.community.en` v1.1.0 contains three separate F2 datasets listed above. The exact source URLs,
revisions, original-byte hashes and modifications are in [community.source.md](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.community.en/community.source.md).
[import-community-catalog.py](Evidence/import-community-catalog.py) and its local helper reproduce
the pack from hash-pinned scratch downloads using `python -I`; upstream code is never executed.
All sources appear in `Info.References`, Sources, fields and browsable evidence.

Inventory retains existing names and gains TGA name fallback (including Scadutree Fragment),
an attributed searchable community-group column and browse-only F4 references. The new catalogs
page/filter/sort all records through F8. The ten PARAM layouts cover equipment, goods, gems,
arts, graces, classes, reinforcement and NG+ scaling. Technical schemas contribute declarations,
order, type/version and field counts only. Regulation browsing compares reference/save versions
without asserting compatibility; no decoded PARAM values or new editable limits are introduced.

The v1.1.0 pack minifies JSON and hoists repeated source/revision/hash/schema/uncertainty metadata
into dataset attribution, per-table layout summaries and the source sidecar. Differing item sources
remain per-record. It removes all 8,651 PARAM row labels and the standalone dump browser: no save
decoder or inventory/equipment/progress/regulation view used those labels. This avoids selecting
data from a single sample or discarding unseen TGA items. All 3,221 identities, 1,748 candidates,
1,072 layouts and their used facts remain pinned by semantic digests against v1.0.0. Full attribution
still appears in F2 Sources, pickers and views. See [compaction measurements](Evidence/PACK_COMPACTION.md)
for byte counts and first-open timing/allocation methodology.

World / progress gains a separate lazy community section: **1,340 base-game**, **392 DLC1** and
**16 DLC3** flag candidates, including **26 NG+ persistence** flag observations. DLC1/DLC3 are
upstream content-group labels, not PS5 installation/ownership claims. Each multi-flag recipe becomes
separate raw observations; predicates and writer actions are discarded. One exact repeated flag
is retained once with `sourceOccurrences=2`. Sparse addressing is `block * 125 + (id % 1000) / 8`,
LSB bit `7 - id % 8`; it is never `id / 8`. All 499 existing mappings independently agree with the
new map; 488 unique existing event IDs overlap the new candidates. Sample source bits 76100/76101
are set and 76102 unset. Base/DLC/NG+ meanings remain Inferred and every F9 status stays Unknown.
Missing/malformed/out-of-range mappings are skipped, retained in the reference browser and counted
in diagnostics. No current NG+ cycle, DLC entitlement, quest completion or reset semantics is asserted.

## Licence exclusions

| Source (pinned revision) | Licence/terms found | Why excluded | Alternative | What would unblock it |
| --- | --- | --- | --- | --- |
| [Hapfel1/er-save-manager, 81b55f78](https://github.com/Hapfel1/er-save-manager/blob/81b55f78d19b571f480b0ae06d680151a9b61e1e/LICENSE) | Source-Available: “may not be sublicensed, sold or included in any other package or bundle intended for distribution” | Explicit redistribution restriction, not missing licence | Research only; independent source/sample facts; no code/data imported | Specific written redistribution permission |
| Publisher game text/icons/map art; previous ERDB / ER-Save-Lib / Paramdex / TGA leads | Publisher rights retained; owner decision 2026-10-29 permits attributed use | Resolved: selected content imported from pinned Compass extraction | 1,245 official descriptions/illustrated records, 1,096 icons and three map backgrounds; other material unneeded | No publisher-asset policy blocker remains; exact joins/alignment still require evidence |
| [Map Genie](https://mapgenie.io/elden-ring/maps/the-lands-between) | Reuse terms unverified; no explicit NC/no-redistribution terms established | Publisher/art policy blocker resolved; site content not needed | Attributed external link remains; local art/coordinates use pinned Compass source instead | No permission request; optional additional sources require provenance and matching coordinates |

Resolved: Paramdex, TGA (including inherited ClayAmore groups), and ER-Save-Lib factual maps were
previously held for missing licences; their useful facts are imported under the owner's decision.
Other template/library/editor leads remain optional cross-checks rather than permission blockers;
existing bounded code covers their applicable prefix/quantity facts. No selected imported file declares NC
or no-redistribution terms. Explicit NC/no-redistribution content remains excluded.

## Remaining gates and owner captures

Phase 1 reader/evidence infrastructure is implemented; its temporal/console validation gate remains
open. Phase 2 includes the complete pinned five-table MIT name catalog, ten starting classes,
reference plumbing, rich browsing and regulation **directory** validation. Technical PARAM reference
layouts and DLC/NG+ candidate labels are now available; validated field decoding/caps, additional
localizations and actual DLC/version coverage remain unfinished. Selected game descriptions/artwork now ship
under the separate attribution policy; they do not establish save-build or gameplay compatibility.
Phase 3 now enables the fixed-size name/ordinary-quantity/history operations and F6 refill/linked-rune
previews above. Phase 4 retains read-only equipment joins. Phase 5 now has source-only progress
candidates and F9 map navigation, plus the character calculation DAG; verified statuses, quest edges
and world/upgrade/equipment writer dependencies remain owner-blocked. Phase 6 allocation/cleanup
semantics remain unproven. No full Phase 2–6 completion or console acceptance is claimed.

The exact before/after capture and console-validation checklist is in
[Evidence/OWNER_CAPTURES.md](Evidence/OWNER_CAPTURES.md), tracked in
[#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31). Independent control pairs, nonempty storage,
owned-instance equip/upgrade pairs, stat-modifier pairs and build/region/DLC observations remain validation
requests; console evidence is not a prerequisite for the enabled Experimental writes.

## SDK requests

No blocking SDK request. F9/F8 meet the shipped map/graph/regulation requirements.
Asset-round performance follow-up: Core `PackFileReader.ReadBounded` allocates a fresh 81,920-byte
temporary buffer per embedded file. The 1,099 new images add about 90 MB of transient read-buffer
allocations at the one-time pack load. Reusing/pooling that shared buffer would remove most of the
measured increase without changing asset contracts. This plugin does not modify Core; exact before/after
time/allocation measurements are in [ASSET_ROUND.md](Evidence/ASSET_ROUND.md).
ELDENRING-R22 is resolved through merged [F13 #33](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/33):
`Mutates=false` reference inspection works on otherwise valid v150/profile-91 read-only saves,
while save edits remain rejected. Browse-only F4 controls use native category filtering and declared
sorting. The F8 catalog metadata index remains for its richer column/detail filters, rather than
implementing a second picker service.

Merged [F12 #32](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/32) supplies bounded F8
view traversal and decoder discovery. `savediff list-decoders` lists `eldenring-evidence`; use
`savediff compare before.zip after.zip --decoder eldenring-evidence`. The evidence fixture keeps
the same decoder ID and allowlisted paths. Raw byte reports still need the endpoint privacy masks
described in [Evidence/README.md](Evidence/README.md). No plugin-local inspect/traversal framework
was added. Unsupported build/loadout presets require the format dependency evidence described
above, not another SDK abstraction.
