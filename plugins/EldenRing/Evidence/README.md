# Elden Ring evidence corpus

Inspection date: **2026-10-28**. No save bytes, extracted game files, account identifiers or
passwords belong here. Samples remain in the private artifacts folder. This directory stores
reproducible assertions and limitations, not game acceptance claims.

## Available baseline

`PPSA03169_saves_dec.zip`: two `memory.dat` records, one primary and one backup, each
28,967,040 bytes; both have SHA-256 prefix `5ae1d00212aadbc1`. F1 lists **one generation**.
The two files are identical twins, **one independent snapshot**. Application/regulation version,
DLC ownership, in-game item menu observations and console acceptance are unknown.

Character 0/profile formats: 150/90. The existing parser's level/rune/history/name/time/attribute
assertions are retained. New source-guided measurements:

| Fact | Observation | Evidence level |
| --- | --- | --- |
| PlayerGameData | `0xA2FF`, length `0x1B0` | Source plus snapshot structure; no controlled pair |
| Held common/key | `0xA6A3`, counts 63/2, matching nonzero rows | Structural cross-check; not 65 unique user possessions |
| Storage common/key | `0x13A5E`, counts 0/0 | Empty-container structure only |
| Variable projectile list | 23 records, eight bytes each | Explains storage position; not ownership/equip proof |
| Scimitar instances | Canonical `0x006CF2A0`, two separate handles | Inferred ID/name join; no menu screenshot |
| Shield | `0x01CB2310` | Same limitation |
| Rowa Fruit | `0x400050F0`, quantity 12 | Same limitation; no proven cap |
| Smithing Stone [1] | `0x40002774`, quantity 3 | Same limitation |
| Cracked Pot | `0x4000251C`, quantity 1 | Container semantics unproven |
| Map: Limgrave, West | `0x40002198`, key array | No map/progress event interpretation |
| HP / FP / stamina triples | `504/504/476`, `88/88/88`, `97/97/97` | Current/effective/base source labels inferred |
| Archetype byte | `PlayerGameData+0xB7`, value 1 | Class mapping not promoted; nearby `+0xBB` is gift |

Source revision: ClayAmore/ER-Save-Editor `014107f0ca1cff867b5f7565cb79d6684f9b35df`.
See the plugin README attribution table and dataset sidecar for identity provenance.

## Round-2 catalog and equipment evidence

All five pinned MIT name maps are embedded: 2,974 weapon, 1,808 goods, 653 armor, 117 talisman
and 218 Ash of War pairs (**5,770 total**), retaining 273 empty names and unused/test/sentinel
rows. The exact source bytes are pinned by SHA-256 in `import-name-catalog.py` and each adjacent
dataset sidecar. Tests independently verify normalized sorted ID/name digests from all source pairs;
the goods digest uses explicit UTF-8 so `Kalé's Bell Bearing` keeps its accent. This is completeness
at one revision, not DLC/current-build coverage, an eligibility list or legal-cap evidence.

`EquipmentReader` compares source-guided gear/quick/pouch representations without writing them.
The sample provides **34 candidate slots: 17 agreeing nonempty joins and 17 unassigned candidates**.
Gear has four related representations, while quick/pouch uses three:

| Representation | Observed role | Sample cross-check |
| --- | --- | --- |
| EquipData | Common-array position + `0x180` | First Scimitar `0x182` = record 2 + `0x180`, not its acquisition `inventoryIndex=0x24D` |
| ChrAsm2 | GaItem/direct handle | Two Scimitars have distinct handles and held row keys despite sharing item ID |
| ChrAsm | Category-stripped ID, after seven selector words | Armor `0x000A3930`, talisman `0x000003E8` |
| EquippedItems | Category-qualified ID | Armor `0x100A3930`, talisman `0x200003E8`; armor name Blue Cloth Cowl |
| QuickItems | Ten `(handle, position)` pairs, active selector, six pouch pairs | First quick ID `0x400003E9`; first pouch ID `0x40000073`; matching held rows/positions |

The variable projectile list still has 23 entries, putting EquippedItems at `0x13887`; Magic and
QuickItems now have explicit bounded ranges. No spell is equipped in this snapshot. Missing/ambiguous
held handles, category conflicts, zero quantity and mirror disagreement stay visible without repair.
Synthetic tests change each gear mirror independently and retain all bytes. Raw equipment fields
stay read-only; the 17 unassigned candidates are not proof of general empty/unarmed encodings.

Round 2 added F6 preview/apply for the **existing** held-rune operation. Sample-backed session tests prove pure
preview, the existing four-byte delta, unchanged `504/504/476` HP and history, one-step exact undo/redo,
semantic review and stale-plan rejection. It does not establish new history semantics or any other
write family.

The **write-enable round** follows the owner's separate authorization to enable fixed-size writes
with warnings. `NameWriteTests`, `QuantityWriteTests`, `HistoryWriteTests` and `RefillPlanningTests`
verify live/menu names, both history copies, positive ordinary quantities, linked F6 history and F6
refill. F1 `ByteDiffer` asserts only intended ranges change; complete-byte equality proves unknown
regions and tails remain intact. Real-sample edits are serialized and reopened, with session undo/redo,
grouped bulk history, filtered-query exclusions and stale/tampered-plan rejection. A lower-history
copy of the sample exercises linked increase; this is explicitly **sample-derived**, not an independently
captured earn/spend event. Nonempty storage remains synthetic-only. Observed quantities 12/3 and
history 4427 are conservative edit ceilings, never claimed legal game caps. New name/history semantics
remain Inferred and warnings state that unknown integrity/dependency rules may cause game rejection.
Console validation is tracked in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).
See [OWNER_CAPTURES.md](OWNER_CAPTURES.md) for current capability decisions and exact validation steps.

## Completion-round progress and layout correction

The bounded acquisition range previously skipped 7,000 **eight-byte** rows, but pinned MIT
`GaItem2::read` consumes four u32 values (**16 bytes**). The corrected sample range starts
at `0x1AC53`, has an eight-byte header/count **73** and 112,000 row bytes, followed by tutorial/tail
bytes. The event range starts at **`0x36600`**, 56,000 bytes later than the old candidate; length
remains `0x1BF99F`. `SemanticSampleTests.SampleAcquisitionRowsAreSixteenBytesAndEventRangeMatchesIndependentProbe`
pins these independent scratch observations. Oversized/negative-as-u32 counts are rejected, and
no exported bytes change. The new reader reproduces source flags 76100/76101 set and 76102 unset;
this is raw-byte agreement, **not** in-game activation proof.

New `eldenring.progress.en` F2 datasets contain every pinned source name/event pair for 314 graces,
157 bosses and 28 map controls, plus ten starting-class tuples. Sparse offsets and LSB bits come
from explicit `event_flags.rs` joins; no flat `eventId/8` assumption. Source hashes, full MIT notice,
all modifications and coverage limits accompany the resources. Independent name/event digests
retain exact upstream whitespace in metadata where F2 trims display labels.

The original completion round left all F9 statuses Unknown and used unpositioned regional maps.
The asset round now adds optional local backgrounds and inferred event-ID placements from the
separate names pack; Map Genie links, accessible lists and raw-field navigation remain available.
Missing coordinates/art retain the original fallback. See [ASSET_ROUND.md](ASSET_ROUND.md).
The F9 graph
shows eight attributes feeding level, without fabricated quest/unlock/upgrade dependencies.
F8 covers the complete progress list and the formerly eager 186-table regulation directory;
regional text/graphic alternatives are bounded to 100 entries. Read-only versions and missing packs
retain honest fallbacks. Inspection and failed raw-flag edits preserve rune undo and exact bytes.
The F1 decoder/privacy masks remain unchanged: these new raw event reads are not added to reports.

## F1 reproduction

From repository root, set the private sample root:

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$sample = Join-Path $env:SAVETOOLKIT_ARTIFACTS 'PPSA03169_saves_dec.zip'
dotnet run --project tools/SaveToolkit.SaveDiff -- list $sample
dotnet run --project tools/SaveToolkit.SaveDiff -- compare $sample $sample --before-gen 0 --after-gen latest --file memory.dat --context 0 --preview 1 --semantic eldenring
dotnet test --project plugins/EldenRing/SaveToolkit.Plugins.EldenRing.Tests
```

The first two commands were run against the existing baseline: two unchanged files, zero changed
bytes, zero semantic changes, zero decode failures, one identical backup pair. Comparing `0` with
`latest` is a self-comparison here. It establishes **no** temporal dependency or gameplay write.
Do not use the raw compare command above on newly captured changing saves without privacy masks.

`fixtures.json` uses the shared `savetoolkit-fixtures/1` schema and records that limitation explicitly.
`EvidenceTests.BaselineCorpus_VerifiesWithoutPretendingToHaveIndependentGenerations` runs F1's
`FixtureVerifier`, supplying the production `EldenRingEvidenceDecoder` through F12's shared
`HookSaveDecoder` adapter, plus `EvidencePrivacy` from the test project. The runtime hook depends
only on SDK and is explicitly registered by `EldenRingPlugin.ISaveDecoderProvider` under the
unchanged ID `eldenring-evidence`. `savediff list-decoders` discovers it; the fixture CLI can resolve
its existing decoder ID without a test-only implementation. CLI `--semantic eldenring` now traverses
bounded F8 inventory pages through the shared view decoder; prefer `--decoder eldenring-evidence`
for the privacy-filtered semantic allowlist. Neither CLI mode applies raw-byte privacy masks by itself.

The hook emits only numeric profile version/held runes/counts and inventory identity/quantity paths.
It excludes names, profile settings, account/password data and raw blobs. Handle + inventory-index
paths align relocated physical rows; duplicate identities get explicit occurrence suffixes. A
synthetic 12→13 quantity change with physical relocation produces one F1 semantic change, no
spurious removal/addition. Decode-budget truncation is reported, not interpreted as removal.

For future changing captures, use `EvidencePrivacy.Masks` for **both** endpoints with zero byte
context. It allowlists only active v150 inventory-container ranges, player numeric words `+08..+6B`
and archetype `+B7`; all other bytes are excluded before reporting. Combining endpoint masks keeps
only their safe intersection if ranges relocate. Exclusions are counted as masked differences and
explicitly labelled privacy exclusions, **not proven save churn**. Synthetic tests verify excluded
changes never enter raw hex previews. No uncontrolled timestamp/clock ignore rule has been invented.

## Regulation observations

Read-only AES/IV decode yields the exact sampled DCX/DFLT variant. Declared zlib payload is
1,859,344 bytes; decoded BND4 is 37,745,424 bytes, bounded to 64 MiB. All 186 entry/name/payload
ranges and sampled PARAM long-offset row directories validate. BND version string is `11001000`,
not a game patch. EquipParamGoods has 1,808 rows/data version 3; EquipParamWeapon has 2,973/data
version 6. These counts are structural evidence only.

The sample's encoded BND format byte `0x74` corresponds to bit-reversed `0x2E`, using a **32-bit
payload offset followed by an ID**. Reading an eight-byte offset happens to work on entry 0 (ID 0)
and fails on entry 1; a known-sample regression prevents repeating that assumption.

PARAM numeric row IDs are not universally unique in the actual snapshot. The type-pointer flag
`0x80` supplies an authoritative long type offset; the legacy string word is stale/out of bounds
in three Cutscene tables. The narrow reader handles both facts without repairing or rewriting them.
It does not deserialize row field bodies, derive carry/storage limits, copy Paramdex schemas,
read FMG descriptions or enable writes. Unsupported regulation variants produce a warning and
preserve the allocation. Synthetic malformed-input tests cover headers, codec, sizes, decompression
budget, zlib corruption, directory counts/offsets/names and PARAM schema/body/name bounds.

## Implementation and validation status

- Phase 1 infrastructure: implemented. Temporal/console proof remains owner-dependent.
- Phase 2: complete pinned MIT item/weapon/armor/talisman/AoW names, ten class references,
  packs, F4 identities/inspection, F8 browsing and directory-only regulation decode implemented.
  Community item/flag identities and technical layout references supplement this (below); the unused
  standalone PARAM row-name dump was removed in v1.1.0. Validated field decoding,
  legal item caps, upgrade compatibility and verified DLC/build coverage remain evidence dependent.
  Selected publisher descriptions/icons and local map art/placements now ship under the separate
  2026-10-29 owner policy, with attribution in [visuals.source.md](../SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.names.en/visuals.source.md).
- Phase 3: fixed-size names, positive ordinary quantities, manual history mirrors, F6 held-only/linked
  runes and refill are enabled with warnings. Modifier-aware attribute/respec writers remain unimplemented.
- Phase 4: read-only equipment joins; complete profile/equipment/upgrade/history transactions remain unimplemented.
- Phase 5: 499 original and 1,748 community source candidates, F9 regional maps with optional local
  art/inferred placements, F8 browsing and character
  dependency graph. Statuses remain Unknown; verified progress/quest/world semantics require controlled captures.
- Phase 6: no allocation/cleanup writer. No prepared F5/F7 write exists.
  See [OWNER_CAPTURES.md](OWNER_CAPTURES.md); evidence no longer gates the enabled Experimental operations.

## Community-data unblock validation (2026-10-29)

The owner's 2026-10-28 decision resolves missing-licence exclusions for community facts. The
separate `eldenring.community.en` pack contains 3,221 TGA item identities/groups, 1,748 TGA flag
candidates and 1,072 technical layout records. The unused 8,651-row PARAM dump was removed in the
v1.1.0 compaction follow-up; no native save-data view/decoder used it. The compact pack retains revisions,
source-byte hashes, per-dataset attribution and source sidecars; the text-only reproducible importer
never executes upstream code. This community pack excludes descriptions/artwork, editor bounds
and spawning recipes; the names pack's separately attributed publisher assets remain available.

- ER-Save-Lib's sparse block map agrees with all **499** existing grace/boss/map-control mappings.
  The new flag catalog overlaps **488 unique event IDs** from the old catalog (the old list repeats
  some IDs). All **1,748** new addresses fit the existing bounded event allocation.
- The sample independently reproduces source events **76100/76101 set, 76102 unset**. Synthetic
  address vectors cover DLC source event **76800 → 0xD16, LSB bit 7** and NG+ persistence event
  **60730 → 0x53D, LSB bit 5**. These vectors are addressing tests, not console semantics.
- All 1,748 community F9 markers publish through Core with bounded regions, Unknown status, source
  attribution and full navigation paths. Optional coordinates/art come from the separate names pack,
  where an exact event-ID join exists. Candidate/reference fields reject writes. No-op,
  inspection, rejected edits and rune undo retain exact complete save bytes.
- DLC Scadutree Fragment has an attributed fallback name and read-only F4 catalog on synthetic
  inventory rows, including unsupported profile-91 write mode. No DLC save sample or ownership
  proof is claimed. Sample Rowa Fruit gains factual Crafting: Plant/category-source attribution.
- The sampled `EquipParamGoods` directory has data version **3**; the technical reference also
  reports **3 / 117 fields**. This comparison establishes neither field compatibility nor caps.
  No PARAM row values are decoded or exported.
- Original v1.0.0 validation: **230 passed / 0 failed / 0 skipped** using the required artifacts path;
  API build **0 warnings / 0 errors**. All ordinary C# files remain below 300 lines.

The compaction follow-up preserves all used facts with independent semantic digests from `c63ba1b`.
It rejects compact flags without the explicit dataset schema contract and keeps the existing bounds.
Measured sizes, fresh-process timing/allocation results and follow-up validation are recorded in
[PACK_COMPACTION.md](PACK_COMPACTION.md). The benchmark program stays outside the repository.

Controlled observations needed for verified DLC/NG+/quest meanings remain in the owner checklist.
No additional writer, console compatibility claim, SDK request or maturity promotion was introduced.
