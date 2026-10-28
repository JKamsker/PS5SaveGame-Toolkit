# Elden Ring (`eldenring`)

**Experimental**, v0.2.0. The only gameplay write is the existing bounded held-rune edit
for character/profile formats **150/90**. There is no console reload evidence. New inventory,
storage, equipment, class, vitals and regulation views are read-only and **Inferred/Unknown**.

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
Serialization patches only held runes, reparses character/profile invariants and returns a copy.

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
- Equipment representations remain independent raw references with Inferred labels; the plugin does
  not claim which candidate is equipped or writable. Acquisition/event ranges are bounded candidates,
  with no boss, quest, grace, completion or NG+ semantics.

Sample assertions reproduce PlayerGameData `A2FF`, held inventory `A6A3` (63 common / 2 key rows),
empty storage `13A5E`, two Scimitars and the shield, Rowa Fruit ×12, Smithing Stone [1] ×3,
Cracked Pot ×1 and the west Limgrave map. Names come from the attributed factual crosswalk, not
in-game screenshots. Sample HP is **504/504/476**, FP **88/88/88**, stamina **97/97/97**; no base-stat
reset is inferred from those observations. See [Evidence/README.md](Evidence/README.md).

## Reference data and rich browsing (Phase 2, partial)

F2 embeds the versioned `eldenring.sample-identities.en` pack, dataset/catalog **`eldenring-items`**,
with six independently assembled short factual ID/name associations. Original crosswalk annotations
are CC0; this is not a licence grant for game content or inherited upstream databases. No third-party
prose, game art, scraped descriptions, TGA-derived bulk lists, quantity limits or DLC catalogue ships.
Every record has coverage/evidence/schema attributes, per-dataset attribution, a source sidecar and a
normalized content hash. Names remain Inferred. Empty contexts and unknown IDs always fall back to raw IDs.

`Open(file, IReferenceContext)` pins the catalog using the **64 lowercase hex SHA-256 of the complete
preserved regulation allocation** as its compatibility key. This is a content fingerprint, not a guessed
app patch or regulation version. An audited exact pack uses that key in `gameBuilds`; an unrelated
build-specific pack is skipped. A changed IV/padding also changes this conservative key. Build-agnostic
packs may supply identity labels only; the bundled six-item pack is JP-only. The Sources panel records
resolved data; field descriptions, row details and the picker carry provenance. Optional imported icons
use the SDK asset service and show their separate asset attribution; none are bundled.

F8 publishes server-backed held/storage tables per active sampled character and a reference-catalog
table. Search, exact column filters and deterministic sorts run over the complete collection before
paging (maximum 200 rows, stable keys, numeric quantity/index/handle sorts). The catalog lazily indexes
bounded record metadata, at most the framework's 250,000-record ceiling; it never eagerly creates view
nodes for the entire pack. Whole-filtered selection remains owned by F8; no plugin bulk action exists.

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

Held runes alone may change, capped at `min(existing lifetime total, 999999999)`, patching exactly four
bytes. Both history copies remain unchanged. Unknown versions are read-only. All other new fields are
read-only until controlled captures establish identity, mirrors, limits and side effects, followed by
owner console reload/resave validation. Only the owner may promote maturity to Stable.

## Validation

Verified on 2026-10-28: **105 plugin tests passed** with private samples; **93 passed / 12 skipped**
with an empty sample root; **171 Core tests passed**. API build succeeded with zero warnings/errors.

Run with `SAVETOOLKIT_ARTIFACTS` pointing to the private artifacts folder:

```powershell
dotnet test --project plugins/EldenRing/SaveToolkit.Plugins.EldenRing.Tests
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

The suite covers conformance, byte-identical sample/backup export, four-byte rune scope and undo,
semantic offsets/identities, malformed counts, duplicate/unknown handles, privacy-filtered F1 fixtures,
F2 pack validity and exact compatibility selection, F4 session routing/undo preservation, F8 complete-set
paging/search/filter/sort, bounded DCX/BND/PARAM rejection and real regulation directories.
Sample-dependent cases skip when samples are absent. Current results are recorded in the issue #14
final implementation comment; no binary sample or extracted regulation is included.

## Sources and attribution

Retrieved/inspected **2026-10-28**. Downloaded code was read as untrusted text, never executed.

| Dataset / code port | Author / source | Licence / modifications / coverage |
| --- | --- | --- |
| PS prefix/profile, bounded GaItem/inventory/equipment reader | [ClayAmore/ER-Save-Editor, 014107f0](https://github.com/ClayAmore/ER-Save-Editor/tree/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save) | Dual MIT/Apache-2.0; MIT option retained in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Adapted only read algorithms, bounded ranges and ID-space facts; sample v150 only for deeper reads. |
| AES/DCX/BND/PARAM directory format | [ClayAmore util, same revision](https://github.com/ClayAmore/ER-Save-Editor/tree/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/util) | MIT option, same notice. Narrow managed read-only port, sample-verified flag corrections, bounded decompression, no unsafe field/schema port. Public format key is not a user credential. |
| Six sampled factual identities, v1.0.0 | [Toolkit contributors / evidence issue #14](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/14); research cross-checks [weapon names](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/weapon_name.rs), [goods names](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/item_name.rs) | Independently assembled factual subset and original annotations, [CC0-1.0](https://creativecommons.org/publicdomain/zero/1.0/), not an imported third-party database. Category-qualified IDs; six JP observations only. No claimed relicensing of game names/art or upstream TGA lists. [Dataset sidecar](SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.sample-identities.en/items.source.md). |
| PS checksum / legacy-count research only | [Hapfel1/er-save-manager, 5974934](https://github.com/Hapfel1/er-save-manager/tree/5974934537feeace982721f82f0fbe69c20c208d) | Restricted/source-available current licence: no code translated, copied, executed or distributed. Facts independently checked against permissive code and sample bytes. |
| Schema / data research only | [ERDB, e2028a6](https://github.com/EldenRingDatabase/erdb/tree/e2028a6e044b920a471388fb4e1c468b31b64350), [Paramdex](https://github.com/soulsmods/Paramdex), [TGA](https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA) | ERDB generator MIT does not license game assets. Paramdex/TGA reuse rights unresolved: no code, schema files, bulk lists, descriptions or artwork imported. |
| Title identity research | [JP](https://prosperopatches.com/PPSA03169), [Asia](https://prosperopatches.com/PPSA04608), [EU](https://prosperopatches.com/PPSA04609), [US](https://prosperopatches.com/PPSA04610) | Title/region facts only. No assets copied. |

The [plan](../../docs/plans/EldenRing.md) records broader research and provenance exclusions.
No approval to redistribute a larger database or artwork is inferred from the six factual labels.

## Remaining gates and owner captures

Phase 1 reader/evidence infrastructure is implemented; its temporal/console validation gate remains
open. Phase 2 provides reference-pack plumbing, a small factual subset, rich browsing and regulation
**directory** validation. PARAM field schemas, validated caps, full localized catalogues/icons/classes/
graces/AoW and DLC/version coverage are unfinished. Phases 3–6 introduce no new writes this round.

The exact before/after capture and console-validation checklist is in
[Evidence/OWNER_CAPTURES.md](Evidence/OWNER_CAPTURES.md). Independent control pairs, nonempty storage,
owned-instance equip/upgrade pairs, stat-modifier pairs and build/region/DLC observations are required.

## SDK requests

No shared-code change is needed for the shipped reader, F2 packs, F4 inspection or F8 tables.
For later work: keep ELDENRING-R22 open for explicitly nonmutating actions on read-only documents;
currently Core blocks them. F6 review/preview integration is deferred to the shared implementation.
Catalogue metadata currently exposes search/paging only; an optional native filter/sort query would
avoid a plugin-side metadata index for very large F8 catalogues. No plugin substitute for F6 was added.
