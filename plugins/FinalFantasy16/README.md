# Final Fantasy XVI (`ffxvi`)

Supports **PPSA10665** (retail PS5) and **PPSA13803** (PS5 demo), using the outer
folder title ID. Unrelated values in supplied retail `param.sfo` files are not
schema evidence. PPSA01288 is not an FF16 title.

**Maturity: Experimental.** Existing main-bank quantities can be patched in place,
including gil, AP and renown. No modified save has been validated on a console.
Equipment, ability arrays, location, growth context, item identity and the
secondary bank are read-only. System/empty/unsupported saves preserve every byte.

## Delivered through round 2 (#15)

- Phase 1 foundation: named categorized inventory, F2 attribution, F4 catalog
  lookup, explicit unknown IDs, separate secondary-bank diagnostic, deterministic
  catalog conversion, F1 generation fixture and machine-readable schema/evidence.
- The **234-entry** item/equipment/resource pack names **131/172 distinct
  observed retail main IDs** and **2/2 demo IDs**. Round 2 resolves **26 of the
  original 67 gaps** with attributed Inferred mappings; **41 remain Unknown**.
  The [complete decision ledger](research/round2-naming-audit.json) records every
  original gap and its evidence or rejection reason. This is partial coverage.
- F4 identity fields preserve the exact raw ID and are read-only. **Look up an
  item** opens a searchable catalog picker and returns attributed information
  without changing save bytes or undo history. No item replacement is enabled.
- Phase 2 read-only foundation: three separate equipment banks, all 68 rank bytes,
  independent 96-bit new/protect arrays, map/party identifiers, stored summon and
  command IDs, item shortcuts, HP/recovery/limit-break context. The 272-byte common
  player prefix is independent of the opaque extended palette suffix.
- MIT Nex packs add 107 map annotations and 10 summon-mode annotations. In the
  first autosaves, candidate map IDs resolve to Hideaway 2 (retail) and Phoenix
  Gate (demo). Their semantic joins remain **Inferred**. Map subrow/sky variation
  is preserved as reference context rather than collapsed to a unique asset.
- Round 2 adds three named Nex skill IDs in a separate research table, seven
  vanilla feat command labels/eight summon-mode relationships from MIT restoration
  code, separate stored adult/child level/XP pairs, and read-only potion-upgrade
  diagnostics. **Nex skill IDs are not joined to rank-byte indices.** A reference
  feat is not proof of an equipped command, learned ability or unlocked Eikon.
- Phase 3 evidence review: explicit per-field legacy manual policy and resource,
  consumable, quest/curio and equipment dependency warnings. The four upgrade IDs
  are named by the MIT UseItem schema, but current numerical capacities, active
  banks and gameplay activation are unproven. **Contextual caps and F6 bulk
  actions remain disabled**; naming an upgrade does not authorize a write.

Phase 1's full-name ambition remains open for the 41 missing observed labels.
Phase 2's controlled field identification, skill-index mapping, matched growth
curves and human-observed acceptance remain open. Phase 3's writes/actions are
evidence-blocked; Phases 4–6 are not implemented. No new write path was introduced.

## File roles and platform boundary

| File / directory | Role |
| --- | --- |
| `FAITHDATAAUTO/game-save-data00` through `09` | Autosave ring |
| `FAITHDATAnnnn/game-save-data` | Manual/special gameplay state |
| `FAITHDATASYSTEM/system-save-data` | System/settings |

Only these basenames under the supported title IDs are claimed, with matching
container versions. The PS5 inner files are plaintext binary. PC PNG `faTh`, XOR
and dictionary-zlib wrappers are separate formats and are not emitted here.
No XML/runtime hook, sibling rewrite, slot transplant or demo conversion is used.

## Wire layout and integrity

All integers are little-endian. Header: u32 size `0x10` at `0`, CRC32 at `4`, u64
container version at `8` (gameplay `0x785930BD`, system `0x0C044C6D`). Blocks start
at `0x10`: u32 type, u32 declared size, u64 schema version, then payload.
Payload length is declared size minus 24; physical advance is declared size minus
8. A four-byte zero type terminates the walk. CRC-32/ISO-HDLC covers `0x10..EOF`
(reflected polynomial `0xEDB88320`, initial `0xFFFFFFFF`, final complement).

The parser validates CRC, alignment, progress, bounds, terminator and recognized
allocator bounds. Limits: 64 MiB, 4,096 blocks. Duplicate types, opaque blocks,
padding and trailing bytes remain untouched. A semantic reader requires exactly
one occurrence of its type and a supported version/length. A mixed known/unknown
duplicate also disables that reader. Unrecognized schemas stay in Structure/Hex.
`Serialize()` recomputes CRC and reparses the container/inventory.

## Curated schemas and confidence

Offsets are relative to payloads. See [supported-schema.json](research/supported-schema.json)
for supported fields, title/role gates, provenance and evidence status.

| Type | Version / payload length | Interpretation |
| --- | --- | --- |
| 1 PlayerData | `202211161` / 272; retail also 1536 | Level/XP, child level/XP Verified read-only; common context Inferred; suffix opaque |
| 2 PartyData | `2021112401` / 168 | Scene +0, map +4, party +16, member count +20; Inferred read-only |
| 6 ItemData | `2021110401` / 36448 | Main allocator +0, secondary +33624; dense storage layout Verified |
| 7 UserData | `2021061001` / 104 | Play time +0, seconds; Verified read-only |
| 13 EquipmentData | `20220104` / 72 | Three six-word arrays +0/+24/+48; Inferred read-only |
| 27 SkillData | `20210422` / 96 | 68 rank bytes +0; newFlags +68; protectFlags +80; index meaning Unknown |

Demo never inherits the extended retail player schema. Each block is gated
independently because retail archives also contain older states. Skill indices
are **not** claimed to be skill IDs. Active equipment bank, slot ordering, Eikon
flag meaning, AP costs/refunds, XP semantics and growth thresholds are unproven.
Equipment value **900000** and ID zero remain unknown; no sentinel is invented.

Main/secondary capacities are 1200/100, with 16-byte records. Only the substantiated
dense identity-key arrangement enables main-stack writes. IDs, new flags, indices,
keys, padding, the secondary bank and dependent equipment state are never patched.
Allocated zero stacks remain visible; allocation does not prove current ownership.
The secondary bank has an **unconfirmed role** and its quantities are read-only.

The existing manual quantity policy remains experimental: nonnegative signed i32
storage range only, with a warning. `int.MaxValue` is not a proven gameplay cap.
Reference-pack limits do not authorize new bounds or max/refill presets. Unknown
caps, unique/key-item effects and progression dependencies prevent bulk controls.

## Reference data and presentation

`Open(file, IReferenceContext)` resolves `ffxvi_items`, `ffxvi_maps`,
`ffxvi_summons`, `ffxvi_skills`, `ffxvi_commands` and `ffxvi_feats`. All have
embedded attributed fallbacks. `ffxvi_feats` uses summon-mode keys;
`ffxvi_commands` uses command keys. Optional external `ffxvi_growth` supplies
label/context only; no matched growth curves are embedded. A level number does
not identify a proven ParamGrow row. No XP/HP calculation follows from a label.
Unknown IDs remain exact strings. Standalone opening reads immutable assembly
resources, with no filesystem/network I/O.

Labels and categories are sourced, not inferred from numeric ranges. Names carry
record/dataset provenance in field descriptions and the Sources panel. The
catalog picker displays the pinned dataset's provenance. Every item record now
also carries Inferred confidence, factual identifier provenance and an evidence
note. Aliases, plural normalization and upgrade suffixes are explicit, including
Hanged Man Signboard and the source's singular Flame of Rebirth spelling. Two
Martelle Apples IDs remain separate. Known `<i>` and
`<color=...>` game presentation tags are removed from imported names; unknown
markup remains literal text. No text is interpreted as HTML. There are no icons.

Tables are bounded (main at most 1200, secondary at most 100, gear 18, skill arrays
92 bytes, named skill research at most 68 references, growth 2 branches, capacity
4 upgrade IDs). Structure expands lazily, at most 64 children and 128 words per leaf;
raw words have Unknown meaning. Hex requests are bounded to 64 KiB.

## Reproducible evidence and validation

[corpus-summary.json](research/corpus-summary.json) records archive SHA-256,
title/role counts and observed block triples, without save bytes. There are 60
retail records (51 distinct contents), 12 demo records (12 distinct), 70 gameplay
records and 60 populated main inventories. All 72 CRC/framing checks and 60
fixed-width in-memory gil-patch preservation checks reproduce independently.
Duplicate contents/paths are not independent experiments.

[F1 fixture](research/generations.fixture.json) pins the retail archive and
compares autosave00 generations 0/1. These are **uncontrolled archive occurrences**,
not chronological play progression: populated level-42 state versus initialized
empty main inventory at level 10. No bytes are masked as presumed churn. Strict
mode is false because many unrelated changes are expected. No new write semantic
is derived from this pair. Do not mistake missing other generation-1 directories
in a whole-archive diff for files removed by the game.

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/FinalFantasy16/SaveToolkit.Plugins.FinalFantasy16.Tests
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
dotnet run --project tools/SaveToolkit.SaveDiff -- verify plugins/FinalFantasy16/research/generations.fixture.json --artifacts $env:SAVETOOLKIT_ARTIFACTS
# Metadata goes to scratch, never to an artifacts archive:
python -I plugins/FinalFantasy16/research/summarize_corpus.py --summary > "$env:TEMP/ff16-corpus.json"
python -I plugins/FinalFantasy16/research/build_catalog.py
python -I plugins/FinalFantasy16/research/build_layout_catalog.py '<scratch>/ffxvi-nex-layouts'
python -I plugins/FinalFantasy16/research/build_feat_catalog.py
python -I plugins/FinalFantasy16/research/verify_reference_audit.py
```

Tests cover conformance, every archive occurrence, CRC vectors, corruption,
allocator gates, byte-identical no-op/undo, edits confined to stack/CRC, context
values, schema length/version/duplicate rejection, independent skill flag arrays,
zero IDs/stacks, unknown 900000, locale fallback, F2 hash/ID validation, attribution,
F4 read-only identities/nonmutating lookup, literal markup and the shared F1 fixture.
Sample tests explicitly skip when archives are absent; validation requires 0 skipped.
No sample bytes, extracted saves or downloaded third-party code are committed.

Round-2 validation (2026-10-28): **75 passed, 0 failed, 0 skipped**, with the
artifacts variable exported in the test command; API build **0 warnings/errors**;
F1 verification **1 passed, 0 failed, 0 skipped** (all four expectations).
All three catalog converters reproduce their committed JSON byte-for-byte;
the naming audit and independent corpus preservation checks also pass.

## Sources and attribution

Research/retrieval date: 2026-10-28. Downloads stayed in scratch and were read as
text; no upstream code/templates were executed. Notices are retained in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

| Dataset / code research | Source / revision | Licence / modifications |
| --- | --- | --- |
| English names/categories and resource labels | [Final Fantasy Wiki translations, 4007473](https://finalfantasy.fandom.com/wiki/Final_Fantasy_XVI_translations?oldid=4007473); per-record Gil/Renown/AP revision links | CC BY-SA 3.0; original 205 labels plus sourced base labels for upgraded variants; original factual ID crosswalk and evidence, no prose/art/caps. [Share-alike sidecar](SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.wiki.en/data.source.md) |
| Additional curio, quest and material labels | [Fafnir, 3987229](https://finalfantasy.fandom.com/wiki/Fafnir?oldid=3987229), [Wall of Memories, 3968454](https://finalfantasy.fandom.com/wiki/Wall_of_Memories?oldid=3968454), [Journal, 3776380](https://finalfantasy.fandom.com/wiki/Journal?oldid=3776380), [Patron's Whisper, 3965500](https://finalfantasy.fandom.com/wiki/Patron%27s_Whisper?oldid=3965500), [Joshua Rosfield, 4047110](https://finalfantasy.fandom.com/wiki/Joshua_Rosfield?oldid=4047110) | CC BY-SA 3.0; selected labels and documented aliases/normalization; factual ID joins Inferred; same share-alike pack/sidecar |
| Four potion-upgrade IDs/labels | [Nenkai UseItem.layout, d3ad062](https://github.com/Nenkai/ffxvi-nex-layouts/blob/d3ad0627a29af168916ad574dadbca6759e2ac48/UseItem.layout) | MIT (2025 Nenkai); explicit 506–509 annotations incorporated into the CC BY-SA item adaptation with MIT notice retained; no numeric capacities imported |
| Map/summon annotations | [Nenkai Nex layouts, d3ad062](https://github.com/Nenkai/ffxvi-nex-layouts/tree/d3ad0627a29af168916ad574dadbca6759e2ac48) | MIT (2025 Nenkai); extracted 107/10 research annotations, grouped subrows; [sidecar](SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.nex.en/data.source.md) |
| Three named skill IDs | [Nenkai Skill.layout, d3ad062](https://github.com/Nenkai/ffxvi-nex-layouts/blob/d3ad0627a29af168916ad574dadbca6759e2ac48/Skill.layout) | MIT (2025 Nenkai); explicit annotations for IDs 39/45/49 only; no rank/flag association; same Nex sidecar |
| Vanilla Eikonic feat/command crosswalk | [Logos-Unleashed Mod.cs, 6424cb4](https://github.com/mThund3R/Logos-Unleashed/blob/6424cb4e60bd31005ef7f0cfb42d4e5360660fc6/LogosUnleashed/Mod.cs) | MIT (2025 mThund3R); selected vanilla restoration relationships, omitted custom IDs/ambiguous paths; [sidecar](SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.feats.en/data.source.md), full original notice retained |
| Save block/allocator/context layout | [Nenkai 010 template, 4a3685f](https://github.com/Nenkai/010GameTemplates/blob/4a3685f9b63f72ca49e24ac2f18420aaad2762b8/Square%20Enix/Final%20Fantasy%2016/FaithSaveFile_Sub.bt) | MIT (2024 Nenkai); composed gated PS5 readers and fixed-width writer; measured against actual bytes |
| CRC/platform boundary research | [FF16Tools, b87c2f2](https://github.com/Nenkai/FF16Tools/blob/b87c2f22a8ea630cb03f7b2198af84c7709a1b0c/FF16Tools.Files/Save/FaithSaveFile.cs) | MIT (2025 Nenkai); independent CRC checked on 72 records; PC wrapper excluded |
| Numeric identifier research only | [Published IDs, 4b5e7eb](https://github.com/Nenkai/ffxvi-modding/tree/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids) | No dataset licence found; CSV files are not vendored/re-licensed. Wiki-derived labels are a separately curated extract; ID associations remain Inferred |
| Dependency/growth research only | Nex Item, EquipItem, UseItem, Skill, ParamGrow layouts at d3ad062 | MIT; schemas reviewed, no game data, costs/caps or unproved setters imported |

Additional research: [Nenkai save-format notes](https://nenkai.github.io/ffxvi-modding/resources/other/save_files/)
and [bbfox PC cheat-table thread](https://opencheattables.com/viewtopic.php?t=1129).
No unlicensed scripts were copied/executed and runtime offsets were not used as
PS5 save offsets. No additional signature/MAC is substantiated.

## Owner capture and console-validation checklist

For **each pair**, use a spare slot and provide original decrypted before/after
archives, title ID, PS5 patch, region, mode (normal/FF NG+), DLC, stage (adult/child),
filename/slot and exact UI values. First capture a save/reload/save no-action pair.

1. Buy one potion, use one, sell one material, and reacquire an allocated zero-stack
   item in separate pairs; record quantities and gil. Then capture each pouch
   capacity/potency upgrade separately.
2. Equip exactly one weapon, belt, vambrace and accessory separately; record all six
   UI slots before/after. Include boyhood and stage replay to identify active banks.
3. Learn, upgrade, master and reset exactly one skill in separate pairs; record
   skill name/rank, assigned Eikon/slot and exact AP paid/refunded. Repeat one Eikon
   unlock separately, without combining a story transition with a skill edit.
4. Earn known XP without leveling, then cross exactly one level; record XP display,
   HP and stats before/after. Repeat adult/child and normal/FF mode as available.
5. Supply matched binary/XML PC states from a legally owned copy if available,
   with build/locale/DLC and matching observations; this complements PS5 evidence.
6. For an existing-stack edit, back up a spare slot, edit only a small quantity,
   export/load, verify UI, spend/use/sell, save, reload and re-export. Report every
   effect and compare unrelated fields. CRC success alone is insufficient.
7. For the [41 remaining IDs](research/round2-naming-audit.json), provide a
   licence-compatible ID/localized-name table with pinned revision and its licence,
   or separately observed item-label/acquisition pairs. A similarly named song,
   recurring item in another title, or unlicensed guide is insufficient.
8. For potency/capacity IDs **506, 507, 508, 509**, capture one upgrade at a time:
   record both banks, all displayed potion/tonic capacities and potency values,
   quest state, mode, DLC and stage. Include an allocated zero-stack control and
   a stage replay. This must distinguish allocation/quantity from activation.
9. To associate named skill IDs **39, 45, 49** with save indices, capture each
   ability's learn/upgrade/master/reset independently with exact AP deltas,
   displayed rank and Eikon assignment. Supply all rank bytes and both flag
   arrays through the original save; do not assume equal numeric IDs/indices.

Still needed: cleared names for 41 observed IDs; matched skill/command/growth data;
normal/FF NG+/DLC/legacy/extended coverage; controlled bank/flag semantics and
on-console acceptance. Only the owner may promote maturity to Stable.

## SDK requests

None for this round. F1/F2/F4 and existing table/field/asset contracts suffice.
F6 planning and F8 bounded query scopes are already available; they are not the
blocker. Future bulk actions must use F6, preview every included/skipped row and
derived CRC, apply atomically, and restore exact bytes with one undo. Their first
prerequisite is a proven current capacity/dependency policy. No local substitute
or non-applicable preview action was built.
