# Elden Ring (`eldenring`)

**Experimental**, v0.4.0, for character/profile formats **150/90**. Enables mirrored names,
bounded ordinary-stack quantities, both rune-history mirrors, and shared F6 refill/linked-rune
actions alongside the existing held-rune edit. New writes are **Inferred**, with explicit warnings:
there is no console validation and the game may reject edited saves. Owner validation is tracked
separately in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).

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
- Only all-zero rows are omitted from browsing. Every nonzero row, including zero quantities,
  sentinel/unknown handles and count mismatches, remains represented. Count mismatches warn without repair.
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
  with no boss, quest, grace, completion or NG+ semantics.

Sample assertions reproduce PlayerGameData `A2FF`, held inventory `A6A3` (63 common / 2 key rows),
empty storage `13A5E`, two Scimitars and the shield, Rowa Fruit ×12, Smithing Stone [1] ×3,
Cracked Pot ×1 and the west Limgrave map. Names come from the attributed upstream name catalog, not
in-game screenshots. Sample HP is **504/504/476**, FP **88/88/88**, stamina **97/97/97**; no base-stat
reset is inferred from those observations. See [Evidence/README.md](Evidence/README.md).

## Reference data and rich browsing (Phase 2, partial)

F2 embeds `eldenring.names.en` v2.0.0, dataset/catalog **`eldenring-items`**: the **complete 5,770
literal ID/name pairs** from ClayAmore/ER-Save-Editor revision
`014107f0ca1cff867b5f7565cb79d6684f9b35df`, using MIT under the owner policy on issue #14.
This replaces the six-item sample pack. Completeness refers to these pinned tables, not all game
builds/DLC. Names remain Inferred on PS5; no copied descriptions, icons or legal quantity caps ship.

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
The combined F2 dataset has MIT attribution, LF-normalized content hashes and an adjacent full MIT
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
use the SDK asset service and show their separate asset attribution; none are bundled.

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
has been invented. Progress/quest writers likewise have no mapped entities or implementation.

## Validation

Write-enable round, verified on 2026-10-28: **163 plugin tests passed / 0 failed / 0 skipped** with private
samples; **252 Core tests passed / 0 failed / 0 skipped**. API build succeeded with zero warnings/errors.
The full-catalog sample initial-view test passes the plan's **under two seconds** target, including
pack loading and document open. A separate empty-artifacts run passed **138 / 0 failed / 25 skipped**,
confirming sample-dependent cases skip cleanly; this run does not count as sample validation.

Run with `SAVETOOLKIT_ARTIFACTS` pointing to the private artifacts folder:

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
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
Sample-dependent cases skip when samples are absent. Current results are recorded in the issue #14
final implementation comment; no binary sample or extracted regulation is included.

## Sources and attribution

Retrieved/inspected **2026-10-28**. Downloaded code was read as untrusted text, never executed.

| Dataset / code port | Author / source | Licence / modifications / coverage |
| --- | --- | --- |
| PS prefix/profile, bounded GaItem/inventory/equipment reader | [ClayAmore/ER-Save-Editor, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/tree/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save) | Dual MIT/Apache-2.0; MIT option retained in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Adapted only read algorithms, bounded ranges and ID-space facts; sample v150 only for deeper reads. |
| AES/DCX/BND/PARAM directory format | [ClayAmore util, same revision](https://github.com/ClayAmore/ER-Save-Editor/tree/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/util) | MIT option, same notice. Narrow managed read-only port, sample-verified flag corrections, bounded decompression, no unsafe field/schema port. Public format key is not a user credential. |
| Weapon names/IDs: 2,974 rows | [ClayAmore / ER-Save-Editor, weapon_name.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/weapon_name.rs) | MIT; all literal pairs, category-qualified IDs, names verbatim, raw labels for empty names; no upgrade legality inferred. [Sidecar](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.names.en/weapon-names.source.md). |
| Goods names/IDs: 1,808 rows | [ClayAmore / ER-Save-Editor, item_name.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/item_name.rs) | MIT; all literal pairs, goods prefix, names verbatim, out-of-category sentinel retained separately; no caps/descriptions. [Sidecar](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.names.en/item-names.source.md). |
| Armor names/IDs: 653 rows | [ClayAmore / ER-Save-Editor, armor_name.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/armor_name.rs) | MIT; all literal pairs, armor prefix and unchanged names. [Sidecar](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.names.en/armor-names.source.md). |
| Talisman names/IDs: 117 rows | [ClayAmore / ER-Save-Editor, accessory_name.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/accessory_name.rs) | MIT; all literal pairs, talisman prefix and unchanged names; no slot eligibility implied. [Sidecar](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.names.en/accessory-names.source.md). |
| Ash of War names/IDs: 218 rows | [ClayAmore / ER-Save-Editor, aow_name.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/aow_name.rs) | MIT; all literal pairs, ash prefix and unchanged names, including tests; no compatibility inferred. [Sidecar](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.names.en/aow-names.source.md). |
| Read-only equipment relationship checks | [ClayAmore / ER-Save-Editor, vm/equipment.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/vm/equipment.rs) and same-revision `save/common/save_slot.rs` | MIT; adapted position/handle/ID relationships with explicit uniqueness/category checks; no writer, unsafe indexing or repair ported. Same full MIT notice. |
| Fixed-size write layout/evidence | [ClayAmore / ER-Save-Editor, save_slot.rs, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/common/save_slot.rs), [PlayStation user_data_10.rs](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/playstation/user_data_10.rs) and local [baseline measurements](Evidence/README.md) | MIT layout facts; original bounded in-place name/history/quantity writers and F6 plans, strict Unicode, preserved padding, staged rollback and sample-observed ceilings. No new game data or external implementation is embedded. Full notice retained. |
| PS checksum / legacy-count research only | [Hapfel1/er-save-manager, 5974934](https://github.com/Hapfel1/er-save-manager/tree/5974934537feeace982721f82f0fbe69c20c208d) | Restricted/source-available current licence: no code translated, copied, executed or distributed. Facts independently checked against permissive code and sample bytes. |
| Schema / data research only | [ERDB, e2028a6](https://github.com/EldenRingDatabase/erdb/tree/e2028a6e044b920a471388fb4e1c468b31b64350), [Paramdex](https://github.com/soulsmods/Paramdex), [TGA](https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA) | ERDB generator MIT does not license game assets. Paramdex/TGA reuse rights unresolved: no code, schema files, bulk lists, descriptions or artwork imported. |
| Title identity research | [JP](https://prosperopatches.com/PPSA03169), [Asia](https://prosperopatches.com/PPSA04608), [EU](https://prosperopatches.com/PPSA04609), [US](https://prosperopatches.com/PPSA04610) | Title/region facts only. No assets copied. |

The [plan](../../docs/plans/EldenRing.md) records broader research and provenance exclusions.
The owner's issue #14 policy approves these MIT name lists and attributed CC BY-SA text. No CC BY-SA
text is needed by this release. Restricted er-save-manager and unresolved Paramdex/TGA remain
research-only; none of their datasets, descriptions or artwork is embedded.

## Remaining gates and owner captures

Phase 1 reader/evidence infrastructure is implemented; its temporal/console validation gate remains
open. Phase 2 now includes the complete pinned five-table MIT name catalog, reference plumbing,
rich browsing and regulation **directory** validation. PARAM field schemas, validated caps, additional
localizations/descriptions/icons/classes/graces and actual DLC/version coverage remain unfinished.
Phase 3 now enables the fixed-size name/ordinary-quantity/history operations and F6 refill/linked-rune
previews above. Phase 4 retains read-only equipment joins; its missing writer dependencies and the
unmodeled Phase 5/6 structures are described above. No full-phase completion is claimed.

The exact before/after capture and console-validation checklist is in
[Evidence/OWNER_CAPTURES.md](Evidence/OWNER_CAPTURES.md), tracked in
[#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31). Independent control pairs, nonempty storage,
owned-instance equip/upgrade pairs, stat-modifier pairs and build/region/DLC observations remain validation
requests; console evidence is not a prerequisite for the enabled Experimental writes.

## SDK requests

No shared-code change is needed for the shipped reader, F2 packs, F4 inspection or F8 tables.
For later work: keep ELDENRING-R22 open for explicitly nonmutating actions on read-only documents;
currently Core blocks ordinary actions there. Shared F6 is available and used by the rune action;
no local preview substitute or new SDK request is needed. Unsupported build/loadout presets need the
missing writer/dependency model described above.
Catalogue metadata currently exposes search/paging only; an optional native filter/sort query would
avoid a plugin-side metadata index for very large F8 catalogues. No plugin substitute for F6 was added.
