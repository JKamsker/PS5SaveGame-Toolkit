# Final Fantasy XVI (`ffxvi`)

Supports **PPSA10665** (retail PS5) and **PPSA13803** (PS5 demo), using the outer
folder title ID. Unrelated values in supplied retail `param.sfo` files are not
schema evidence. PPSA01288 is not an FF16 title.

**Maturity: Experimental.** Supported existing fields can be patched in place,
including inventory identities/stacks, equipment, raw ability arrays, player and
party context. No modified save has been validated on a console. Every new write
retains an Inferred/Unknown label and a console-validation warning. System and
unsupported schemas remain read-only; empty gameplay saves may expose independent
supported context fields. No-op output preserves every byte.

## Completion round (#15)

Main inventories above 100 rows now use F8 server-backed pages. Search, exact
column filters and numeric sorting cover every allocated row, with stable slot
keys and deterministic ties. Smaller banks keep eager tables. F3 expandable
details retain sources/storage warnings and F4 identities retain their controls.
F6 previews accept Core-resolved **all matching rows** scopes (#27), including
unloaded pages and exclusions. Empty selection remains empty. Primary storage
bindings are independent of paging, including original values needed by undo.

The **Ability reference** tab has a collapsed spoiler section with 48 attributed
abilities in ten Eikon groups, including Ifrit, Leviathan and Ultima. F9 chains
show published initial/upgrade/master progression and individual AP annotations
where sourced. All saved learned/unlocked/mastered states remain **Unknown**.
Every graph node opens a read-only F4 reference field with sources. These keys
are reference slugs, not Nex IDs or saved rank indices. The 68 raw rank controls
and three named Nex IDs remain separate. Missing AP costs remain Unknown.

The F9 location map links the candidate stored map ID to its control and an
external wiki guide. No artwork, coordinates, visited/completion claims or
teleportation are supplied. Missing/duplicate/unsupported PartyData produces no
map. Standalone opening now displays the same map-variant attributes as host packs.

The 234-entry item pack and **131/172 retail, 2/2 demo** observed-ID coverage are
unchanged. Rechecking pinned wiki memory/item pages and published identifier leads
did not establish cleared FF16-specific evidence for the remaining 41 IDs.
Their numeric fallbacks remain. The ability pack adds reference coverage rather
than confirmed save mappings. See the [requirement/acceptance gap audit](COMPLETION.md).

## Framework feature adoption

| Feature | Adoption | Where / concrete reason |
| --- | --- | --- |
| F1 SaveDiff | Used | Hash-pinned generation fixture, native `ffxvi` F12 hook and exact allowed-byte-range write tests. No local diff engine. |
| F2 reference packs | Used; added this round | Four packs, immutable standalone fallbacks, Sources panel and provenance; new 48-entry ability pack. |
| F3 rich cells | Used; added this round | Field confidence, selected-row planned actions and expandable source/storage details in eager/queried rows. No reviewed licensed icons are available. |
| F4 pickers | Used; added this round | Inventory/gear/shortcut IDs, summon/command/map context, Nex skill and read-only ability references. Read-only IDs publish browse-only ReferenceSpec controls; catalogue searches use category and declared sort keys. Other numeric context is an index/bitset or has no catalogue. |
| F5 structural undo | N/A | No safe allocator writer: free-list/generation/slot reuse and dependencies are unidentified. Fixed-width edits use value history. |
| F6 planning / #27 | Used; added this round | Explicit main-stack targets for document, selected rows and resolved whole-query scopes, with exclusions, CRC, skips, warnings and one undo group. The only planned parameter is a numeric quantity; selected item IDs already use ReferenceSpec fields, so no planned-action ID fallback is needed. |
| F7 groups | N/A | No established gameplay/system coupling or prepared multi-file writer; speculative sibling edits have no substantiated contract. |
| F8 collections | Added this round | Main inventory reaches 1,200 rows; materialize only requested rich rows. Secondary <=100, gear 18, raw ability bytes 92. |
| F9 graph/map | Added this round | Lazy spoiler-gated reference progression DAG and link-only candidate location map; text alternatives, provenance and field navigation. |
| F10 numbers | Used | Exact integer-string wire and integer-duration seconds. ExactNumberSpec/nullable/union are N/A to present fixed-width binary integers/bytes with no lexeme or null/type discriminator. |
| F11 codecs | N/A | Supported PS5 files are plaintext Faith blocks with CRC; no Kraken/SQLite or PC PNG/XOR/zlib wrapper. |
| F12 diff traversal | Used | The `ffxvi` SDK decoder hook emits bounded Faith root, inventory and context evidence independently of UI paging; the existing F1 fixture now has a native hook option. |
| F13 inspect/picker/metadata | Used | The catalogue lookup is an explicit `Mutates = false` inspect action. Read-only IDs use browse-only ReferenceSpec pickers, and catalogue views use category filtering plus declared key/category sorting. Field-metadata publishing is N/A because the plugin has no separate metadata source beyond its bound views. |

## Enabled writes and their evidence

The owner authorized **"Enable writes. Keep warnings."** on 2026-10-28. The write round
enabled **15 write families** below, in addition to the inherited manual main-stack
writer. Fixed-width layout is checked against the attributed MIT Nenkai template
and actual PS5 samples; gameplay meaning remains unproven. New numeric writes use
the native type intersected with conservative observed maxima, documented in
[write-ranges.json](research/write-ranges.json) and reproduced by
[measure_write_ranges.py](research/measure_write_ranges.py). Nonnegative fields
have a zero lower bound; observed `-1` HP/recovery/LB values are retained without
inventing a sentinel meaning. Original outliers can be restored exactly. A field
whose observed interval is constant (demo LB) has no alternative value enabled.
These are experimental bounds, **not legal gameplay caps**.

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| Main existing item ID replacement | Inferred / Unknown original | Dense identity-key layout; named F2 IDs; complete candidate duplicate-ID check | CRC repaired; container/allocators reparsed | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): replace one unequipped item, inspect quantity/flags/dependencies, use/sell, save/reload |
| Secondary existing stack | Unknown | Same fixed i32 record layout; observed 0..152; bank role unconfirmed | CRC repaired; reparsed | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): edit one stack, identify active adult/child/stage behavior |
| Secondary existing item ID | Inferred / Unknown original | Dense records; ID-zero records excluded; duplicate-ID check | CRC repaired; reparsed | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): replace one nonzero ID; compare both banks after save/reload |
| All three equipment ID arrays | Inferred / Unknown original | Type 13 version/length gate; 18 fixed i32 values; equipment-category picker | CRC repaired; reparsed | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one slot/bank at a time; verify adult/boyhood/virtual behavior and ownership |
| 68 stored rank bytes | Unknown | Type 27 gate; retail 0..3, demo 0..1 observed | CRC repaired; reparsed; padding untouched | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): identify each index; learn/upgrade/master/reset with exact AP deltas |
| 12 newFlags bytes | Unknown | Separate 96-bit stored array; byte range 0..255 | CRC repaired; reparsed | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): change one bit and observe skill notifications/state |
| 12 protectFlags bytes | Unknown | Independent 96-bit array, including bits beyond rank count | CRC repaired; reparsed | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): change one bit and observe protection/reset behavior |
| Adult/child stored level and XP pairs | Inferred | Supported common PlayerData prefix; independent stored pairs | CRC repaired; reparsed; no stat calculation | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one value/branch at a time; verify XP basis, level/HP/stats and save/reload |
| Stored HP, recovery and LB | Inferred | Common prefix offsets; observed ranges/sentinels | CRC repaired; reparsed | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): small change to one field; verify battle/recovery behavior |
| Summon index, raw learned/revealed flags and 3 IDs | Inferred / Unknown unmapped IDs | Fixed slot count and observed bounds; named summon catalog | CRC repaired; reparsed; no unlock reconciliation | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one index/ID/bit at a time; verify Eikon availability and active loadout |
| 22 stored command IDs | Inferred / Unknown unmapped IDs | Common-prefix array; named reference choices within observed range | CRC repaired; reparsed | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one command at a time; verify slot, mastery and AP behavior |
| 4 item shortcuts | Inferred / Unknown original | Fixed i32 array; consumable-only named choices, observed IDs <=9 | CRC repaired; reparsed | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): replace one shortcut, use item, verify ownership and persistence |
| Party scene/map/party IDs and member count | Inferred / Unknown unmapped map | Type 2 gate; fixed fields and observed bounds | CRC repaired; reparsed; transforms/quests untouched | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one ID/count at a time; verify location/member consistency; this is not validated teleportation |
| Stored play-time seconds | Inferred | UserData gate; sample timestamp delta relationship | CRC repaired; reparsed; timestamps untouched | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): verify displayed duration and save/reload |
| F6 explicit main-stack quantity action | Inferred policy | Existing fixed-width writer, 0..127811 corpus ceiling; deterministic selected/document scope | Exact CRC preview, atomic apply, one undo/redo | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): small target on two selected rows; use/spend/sell, save/reload/re-export |

All these writes can produce gameplay-inconsistent state that the game rejects or
resets despite a valid CRC. No quest, ownership, AP/refund, stat/growth, capacity,
unlock or loadout dependencies are reconciled. Unknown replacement IDs are rejected;
original unknown IDs (including 900000) remain restorable. Main/secondary identity
replacement never allocates/removes a record. New duplicate IDs are rejected after
validating the complete batch, so an atomic identity swap remains possible.

The F6 action requires a preview, lists unchanged/negative rows as skipped, keeps
per-resource/category warnings and shows the derived CRC. Empty selection means
no rows; null selection means all supported main records. Stale/forged plans,
foreign/duplicate row keys and invalid targets are rejected without mutation.
Core resolves filtered query scopes to the complete matching key set, including
unloaded pages and explicit exclusions; the plugin validates the collection and keys.
It sets an **explicit stored target**, not a legal-max, refill or activation preset.

Still off for structural safety: add/remove/reorder/free-list edits (slot reuse,
generation/index rules and dependent references are unknown), secondary ID-zero
identities (possible sentinel; their fixed-width quantities remain editable), opaque extended palettes, and unmapped progression
structures. Feat/skill research labels and upgrade diagnostics have no separate
storage setter. No prepared multi-file writer exists and no cross-file dependency
has been established; F7 is therefore not used. Structural safety still requires
the owner-supplied format/dependency evidence described below; raw writes are enabled.

## Prior rounds (#15)

The following records the earlier read-only delivery. Its evidence gates were
superseded by the write-enable round above; unresolved semantic research remains.

- Phase 1 foundation: named categorized inventory, F2 attribution, F4 catalog
  lookup, explicit unknown IDs, separate secondary-bank diagnostic, deterministic
  catalog conversion, F1 generation fixture and machine-readable schema/evidence.
- The **234-entry** item/equipment/resource pack names **131/172 distinct
  observed retail main IDs** and **2/2 demo IDs**. Round 2 resolves **26 of the
  original 67 gaps** with attributed Inferred mappings; **41 remain Unknown**.
  The [complete decision ledger](research/round2-naming-audit.json) records every
  original gap and its evidence or rejection reason. This is partial coverage.
- F4 identity fields preserve the exact raw ID and are writable where mapped, with
  warnings and validation described above. **Look up an
  item** opens a searchable catalog picker and returns attributed information
  without changing save bytes or undo history.
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
  banks and gameplay activation are unproven. **Contextual legal caps and refill
  presets remain unimplemented**; the explicit-quantity F6 action is enabled with warnings.

Phase 1's full-name ambition remains open for the 41 missing observed labels.
Phase 2's controlled field identification, skill-index mapping, matched growth
curves and human-observed acceptance remain open. The write round delivers raw
fixed-width controls and an explicit F6 action, not complete contextual gameplay
policies or progression reconciliation from Phases 3–6.

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
| 1 PlayerData | `202211161` / 272; retail also 1536 | Stored level/XP and common context writable Inferred; suffix opaque |
| 2 PartyData | `2021112401` / 168 | Scene +0, map +4, party +16, member count +20; writable Inferred |
| 6 ItemData | `2021110401` / 36448 | Main allocator +0, secondary +33624; dense storage layout Verified |
| 7 UserData | `2021061001` / 104 | Play time +0, seconds; writable Inferred |
| 13 EquipmentData | `20220104` / 72 | Three six-word arrays +0/+24/+48; writable Inferred |
| 27 SkillData | `20210422` / 96 | 68 rank bytes +0; newFlags +68; protectFlags +80; index meaning Unknown |

Demo never inherits the extended retail player schema. Each block is gated
independently because retail archives also contain older states. Skill indices
are **not** claimed to be skill IDs. Active equipment bank, slot ordering, Eikon
flag meaning, AP costs/refunds, XP semantics and growth thresholds are unproven.
Equipment value **900000** and ID zero remain unknown; no sentinel is invented.

Main/secondary capacities are 1200/100, with 16-byte records. Only the substantiated
dense identity-key arrangement enables existing-record writes. New flags, indices,
keys and padding are never patched; dependent equipment state is not reconciled.
Allocated zero stacks remain visible; allocation does not prove current ownership.
The secondary bank has an **unconfirmed role**, warned on every new writable field.

The existing manual quantity policy remains experimental: nonnegative signed i32
storage range only, with a warning. `int.MaxValue` is not a proven gameplay cap.
Reference-pack limits do not authorize max/refill presets. The new F6 explicit
target uses a conservative observed ceiling and warns about all unknown caps,
unique/key-item effects and progression dependencies.

## Reference data and presentation

`Open(file, IReferenceContext)` resolves `ffxvi_items`, `ffxvi_maps`,
`ffxvi_summons`, `ffxvi_skills`, `ffxvi_commands`, `ffxvi_feats` and
`ffxvi_abilities`. All have
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

Tables are bounded (main pages at most 200 of 1200, eager only at <=100;
secondary at most 100, gear 18, skill arrays 92 bytes, named Nex skill research at
most 68 references, ability-reference details 48, growth 2 branches, capacity
4 upgrade IDs). The spoiler tree is lazy; graph states remain Unknown.
Structure expands lazily, at most 64 children and 128 words per leaf;
raw words have Unknown meaning. Hex requests are bounded to 64 KiB.

## Reproducible evidence and validation

Completion-round validation (2026-10-28): **106 passed, 0 failed, 0 skipped**,
including conformance and every archive occurrence. `SAVETOOLKIT_ARTIFACTS` was
exported in the test command; configuration Release. The final API Release build
passed with **0 warnings, 0 errors**. The shared F1 fixture passed **1/0/0**, all
four expectations. Independent corpus checks reproduced all **72 CRC/framing**
and **60 fixed-width preservation** checks; write-range metadata matches exactly.

New coverage includes capacity-1200 inventory paging, numeric sort/ties, long
localized labels without icons, query refresh after identity edits/undo, complete
filtered actions over unloaded pages, exclusions, zero matches/full exclusion,
row caps/stale/invalid scopes, exact CRC and one-step bulk undo/redo. F9 tests
validate graph structure, all-Unknown states, spoiler laziness, targets, read-only
F4 references, pack/fallback parity, independent rank bytes and unsupported/unknown
map schemas. The ability converter is deterministic: **48 records, 10 groups,
135 graph nodes, 87 edges, 96 individual AP annotations**. C# files remain under
300 lines; local Markdown links, pack hashes, attribution and staged scope pass.
These checks do not establish console acceptance.

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
python -I plugins/FinalFantasy16/research/build_ability_catalog.py
python -I plugins/FinalFantasy16/research/verify_reference_audit.py
python -I plugins/FinalFantasy16/research/measure_write_ranges.py
```

Tests cover conformance, every archive occurrence, CRC vectors, corruption,
allocator gates, byte-identical no-op/undo, edits confined to stack/CRC, context
values, schema length/version/duplicate rejection, independent skill flag arrays,
zero IDs/stacks, unknown 900000, locale fallback, F2 hash/ID validation, attribution,
F4 bounded identity replacements/nonmutating lookup, literal markup and the shared F1 fixture.
Sample tests explicitly skip when archives are absent; validation requires 0 skipped.
No sample bytes, extracted saves or downloaded third-party code are committed.

F12/F13 follow-up validation (2026-10-28): **108 passed, 0 failed, 0 skipped** with
the external artifacts directory exported; the native `ffxvi` hook reproduces the
F1 fixture and the session inspection test verifies unchanged revision/history.

Write-enable validation (2026-10-28): **92 passed, 0 failed, 0 skipped**, including
conformance, with `SAVETOOLKIT_ARTIFACTS` exported in the same command:

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/FinalFantasy16/SaveToolkit.Plugins.FinalFantasy16.Tests --configuration Release
```

Every newly writable, nonconstant field in the first retail/demo autosaves is
edited, serialized, reopened, checked with F1 for unrelated-byte preservation,
rejected on invalid input, and exercised through session undo/redo. Synthetic
boundary tests cover ID-zero quantity persistence, unknown/negative original IDs,
outliers, identity swaps/duplicates, category restrictions, live derived aliases,
trailing bytes and the system-role boundary. F6 tests cover exact CRC/diagnostic
previews, selection/document/empty scopes, skips, invalid/forged/stale plans and
one-step group undo/redo. Final API build: **0 warnings/errors**. F1 fixture:
**1 passed, 0 failed, 0 skipped**. Range metadata, all 72 independent CRC/framing
checks and 60 inherited fixed-width preservation checks reproduce. None of these
checks establish console acceptance.

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
| 48 Eikon ability references / progression / AP annotations | [Base-game navbox, 3770527](https://finalfantasy.fandom.com/wiki/Template:Navbox_abilities_FFXVI?oldid=3770527), [Leviathan, 4005603](https://finalfantasy.fandom.com/wiki/Leviathan_(Final_Fantasy_XVI)?oldid=4005603), [Ultima, 4045298](https://finalfantasy.fandom.com/wiki/Ultima_(Final_Fantasy_XVI)?oldid=4045298); every individual AP/name article pinned in TSV/records | CC BY-SA 3.0; original slug keys/warnings, selected names/groups/stages and individual AP facts, no art or saved-index joins. [Share-alike sidecar](SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.abilities.en/data.source.md). Missing AP stays Unknown. |
| External location guide | [Final Fantasy Wiki locations](https://finalfantasy.fandom.com/wiki/Final_Fantasy_XVI_locations) | Link only; no artwork or coordinates imported. Stored map labels retain the separate MIT Nex attribution. |
| English names/categories and resource labels | [Final Fantasy Wiki translations, 4007473](https://finalfantasy.fandom.com/wiki/Final_Fantasy_XVI_translations?oldid=4007473); per-record Gil/Renown/AP revision links | CC BY-SA 3.0; original 205 labels plus sourced base labels for upgraded variants; original factual ID crosswalk and evidence, no prose/art/caps. [Share-alike sidecar](SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.wiki.en/data.source.md) |
| Additional curio, quest and material labels | [Fafnir, 3987229](https://finalfantasy.fandom.com/wiki/Fafnir?oldid=3987229), [Wall of Memories, 3968454](https://finalfantasy.fandom.com/wiki/Wall_of_Memories?oldid=3968454), [Journal, 3776380](https://finalfantasy.fandom.com/wiki/Journal?oldid=3776380), [Patron's Whisper, 3965500](https://finalfantasy.fandom.com/wiki/Patron%27s_Whisper?oldid=3965500), [Joshua Rosfield, 4047110](https://finalfantasy.fandom.com/wiki/Joshua_Rosfield?oldid=4047110) | CC BY-SA 3.0; selected labels and documented aliases/normalization; factual ID joins Inferred; same share-alike pack/sidecar |
| Four potion-upgrade IDs/labels | [Nenkai UseItem.layout, d3ad062](https://github.com/Nenkai/ffxvi-nex-layouts/blob/d3ad0627a29af168916ad574dadbca6759e2ac48/UseItem.layout) | MIT (2025 Nenkai); explicit 506–509 annotations incorporated into the CC BY-SA item adaptation with MIT notice retained; no numeric capacities imported |
| Map/summon annotations | [Nenkai Nex layouts, d3ad062](https://github.com/Nenkai/ffxvi-nex-layouts/tree/d3ad0627a29af168916ad574dadbca6759e2ac48) | MIT (2025 Nenkai); extracted 107/10 research annotations, grouped subrows; [sidecar](SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.nex.en/data.source.md) |
| Three named skill IDs | [Nenkai Skill.layout, d3ad062](https://github.com/Nenkai/ffxvi-nex-layouts/blob/d3ad0627a29af168916ad574dadbca6759e2ac48/Skill.layout) | MIT (2025 Nenkai); explicit annotations for IDs 39/45/49 only; no rank/flag association; same Nex sidecar |
| Vanilla Eikonic feat/command crosswalk | [Logos-Unleashed Mod.cs, 6424cb4](https://github.com/mThund3R/Logos-Unleashed/blob/6424cb4e60bd31005ef7f0cfb42d4e5360660fc6/LogosUnleashed/Mod.cs) | MIT (2025 mThund3R); selected vanilla restoration relationships, omitted custom IDs/ambiguous paths; [sidecar](SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.feats.en/data.source.md), full original notice retained |
| Save block/allocator/context layout | [Nenkai 010 template, 4a3685f](https://github.com/Nenkai/010GameTemplates/blob/4a3685f9b63f72ca49e24ac2f18420aaad2762b8/Square%20Enix/Final%20Fantasy%2016/FaithSaveFile_Sub.bt) | MIT (2024 Nenkai); composed gated PS5 readers, shared context layout and fixed-width writers; measured against actual bytes |
| CRC/platform boundary research | [FF16Tools, b87c2f2](https://github.com/Nenkai/FF16Tools/blob/b87c2f22a8ea630cb03f7b2198af84c7709a1b0c/FF16Tools.Files/Save/FaithSaveFile.cs) | MIT (2025 Nenkai); independent CRC checked on 72 records; PC wrapper excluded |
| Numeric identifier research only | [Published IDs, 4b5e7eb](https://github.com/Nenkai/ffxvi-modding/tree/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids) | No dataset licence found; CSV files are not vendored/re-licensed. Wiki-derived labels are a separately curated extract; ID associations remain Inferred |
| Dependency/growth research only | Nex Item, EquipItem, UseItem, Skill, ParamGrow layouts at d3ad062 | MIT; schemas reviewed, no game data, costs/caps or unproved setters imported |

Additional research: [Nenkai save-format notes](https://nenkai.github.io/ffxvi-modding/resources/other/save_files/)
and [bbfox PC cheat-table thread](https://opencheattables.com/viewtopic.php?t=1129).
No unlicensed scripts were copied/executed and runtime offsets were not used as
PS5 save offsets. No additional signature/MAC is substantiated.

## Licence exclusions

| Source and revision | Licence found | Why it was refused | What was done instead | What would unblock it |
| --- | --- | --- | --- | --- |
| [Published FF16 identifier CSVs, `4b5e7eb`](https://github.com/Nenkai/ffxvi-modding/tree/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids) | none found | no licence | Consulted only as research; raw numeric IDs remain, and separately licensed per-record labels are used only where evidence permits. | An explicit redistribution licence covering the CSV data and derived crosswalks. |
| [Final Fantasy XVI locations guide](https://finalfantasy.fandom.com/wiki/Final_Fantasy_XVI_locations) (unversioned link) | none found | proprietary game assets | Kept an HTTPS link-only guide and published no art, screenshots, or coordinates; local map labels use the separately noticed MIT dataset. | A rights-cleared map/coordinate dataset with explicit permission to embed it. |
| FF16 game icons, screenshots, and other extracted media (no standalone revision) | none found | proprietary game assets | No image assets are shipped; views expose text, raw IDs, provenance, and external links only. | Written permission from the rights holder for redistribution in this plugin. |

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
10. Validate each newly enabled family from the table above on a **separate spare
    slot/copy**: one main identity replacement; one nonzero secondary identity;
    one small secondary stack change; one equipment ID in each of the three
    banks; one rank byte; one newFlags bit; one protectFlags bit; one level or XP
    value in each branch; one HP/recovery/LB value; one summon slot/index/flag;
    one command; one shortcut; one party scene/map/ID/count; and play-time seconds.
    For each experiment, load, inspect the exact displayed/behavioral effect,
    perform one relevant use/spend/reset/battle or stage transition, save, reload
    and re-export. Include failures or automatic resets; compare both banks and
    unrelated quest/AP/stat/loadout state with F1. Do not combine these changes
    into one trial. For demo LB's constant range, supply a controlled nonzero
    capture before requesting a wider write range.
11. Validate F6 on two explicitly selected ordinary main rows with target **1**:
    inspect before/after, skipped rows, dependency warnings and CRC in the preview;
    apply, undo once (original bytes), redo once, export, load, use/sell/spend,
    save/reload and re-export. Separately test zero selection and document scope.
    Report all results to [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).
12. For the reference tree, record the displayed name, individual learn/upgrade/
    master costs and exact AP before/after for one ability per Eikon (include
    Leviathan/Ultima and demo availability). Supply independent learn/upgrade/
    master/reset pairs to identify saved indices/flags; no graph state is inferred
    from a reference name. Capture a single obelisk activation/collectible pickup
    separately before requesting completion markers or coordinates.
13. Validate F8 selection on a populated retail inventory: filter a category,
    select all matching, exclude one unloaded-page row, preview target **1**, verify
    scope/skips/CRC, apply and undo once, redo and export/load/use/save/reload.
    Include a zero-match and fully excluded query; both must change nothing.
14. To establish structural safety, capture buying the first new item, selling its
    last stack, reacquiring it, a second distinct allocation and a slot-reuse case.
    Record allocation count, equipment/shortcuts and both banks before/after. Include
    the capacity boundary if possible; do not combine events in one pair.

Still needed: cleared names for 41 observed IDs; matched skill/command/growth data;
normal/FF NG+/DLC/legacy/extended coverage; controlled bank/flag semantics and
on-console acceptance. Only the owner may promote maturity to Stable.

## SDK requests

No new blocking SDK request. F8/F9/#27, the F12 decoder hook, and F13 inspection/
browse-only catalogue support are consumed directly. Structural and multi-file work
still needs a substantiated format/dependency model, not another API. Primary
quantity/identity bindings are registered at document construction so unloaded-row
actions and undo work today. Owner validation (#31) does not gate the enabled
experimental fixed-width writes.
