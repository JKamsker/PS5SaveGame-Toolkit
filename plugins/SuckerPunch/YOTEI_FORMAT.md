# Yōtei format evidence and validation (PPSA26344)

Updated 2026-10-29 against the owner's expanded `PPSA26344_saves_dec.zip`. Plugin 0.9.0 stays
Experimental. No field is console-validated. All statements below are byte observations or explicitly
labelled inferences; unrelated opaque data and all sibling files are preserved.

## Corpus and differential method

The archive contains 13 payload entries of 616,448 bytes: 11 distinct nonempty saves and two identical
all-zero placeholders (`Auto2`, `AutoE0`). Native F1/F12 decoding compares every nonempty pair (55)
in tests. A scratch-only CLI run compares ten adjacent pairs covering all eleven nonempty slots.
Files were wrapped at a common title/directory/path to compare slot content rather than file identity;
payload bytes were unchanged. The actual CLI used `--decoder suckerpunch.native-v1`: all comparisons
completed without input/output truncation or semantic errors. The existing Tsushima fixture also passed
(1 passed, 0 failed, 0 skipped). See the README SDK request for the unrelated all-plugin CLI conflict
and SuckerPunch-only scratch deployment workaround. Reports and normalized ZIPs stay on D:, never in git.

## Header and integrity

Four little-endian uint32 words, 16 bytes total:

| Offset | Observation | Confidence |
| --- | --- | --- |
| 0x00 | Magic `0x150` | Verified bytes |
| 0x04 | Varies across slots; meaning unknown, not assumed to be a format version | Unknown |
| 0x08 | Declared content end; equals last nonzero byte end in all 11 samples | Inferred |
| 0x0C | Additive checksum candidate below | Inferred algorithm/coverage |

```text
word_at_0x0C = (sum(bytes[0x10:word_at_0x08]) + word_at_0x08 + 0x150) modulo 2^32
```

All eleven nonempty payloads match. Header word 1 is not included in this formula. Bounds are checked
before slicing. All bytes after the declared end are zero, so content-only and whole-file coverage
remain indistinguishable. A matching candidate enables experimental fixed-width edits; a mismatch
is inspection-only and no-op export does not normalize it. Known candidate integrity is recomputed
after each atomic edit, and Serialize reparses the container and every named table before export.

**The game may reject the edited save.** This warning appears on fields/actions, previews, outcomes
and the overview because coverage and console behavior remain unproven. A synthetic nonzero tail is
preserved exactly by tests; that is a preservation test, not proof of game coverage.

| Slot | Header word 1 | Declared end | Checksum candidate | Mon | Flowers | Normal arrows |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| memory | 11 | 414213 | 30214928 | 106024 | 19 | 9 |
| Auto0 | 10 | 414182 | 30193082 | 106009 | 13 | 9 |
| Auto1 | 11 | 414332 | 30234516 | 106054 | 22 | 9 |
| Auto3 | 13 | 412284 | 29988190 | 0 | 0 | 0 |
| Auto4 | 14 | 413377 | 30080227 | 112099 | 0 | 6 |
| AutoE1 | 16 | 411397 | 30157398 | 0 | 0 | 0 |
| User0 | 0 | 412308 | 29994398 | 120 | 0 | 0 |
| User1 | 1 | 413162 | 30064194 | 120 | 0 | 0 |
| User2 | 2 | 414023 | 30176692 | 106009 | 13 | 9 |
| User3 | 3 | 414156 | 30199339 | 106024 | 16 | 9 |
| User4 | 4 | 417135 | 30420001 | 106076 | 22 | 9 |

Values are raw readings, not observed in-game balances. Some unusually high values may reflect
prior modification; observed maxima are conservative editor bounds, not intended carrying capacities.

## Named sections and engine similarities

Section locations move across saves. Anchors and bounded traversal replace absolute offsets. Every
name is a unique 1–64-byte ASCII identifier. The parser validates complete counts, expected final
names, ordering and nonoverlap within the declared content.

| Section | Unique anchor / end | Layout and evidence | View / write policy |
| --- | --- | --- | --- |
| Statistics | uint32 `33`, length-prefixed `FishSpeared`; ends `CrTriggerMarkedPerkEffects` | 33 names, each followed by one uint32; values vary across slots | Read-only Inferred statistics; not quest, technique, completion or reward ownership |
| Gear registry | uint32 `1037`, length-prefixed `HalfShog101`; ends `SwordSwingFxOnryo` | 1,037 names, each followed by two opaque bytes; exact entire registry identical across all 11 saves | F8 `yotei/gear-registry`, pages 1–200, stable raw IDs, search/filter/sort; no ownership/equipped inference |
| Primary inventory | First uint32 `48`, length-prefixed `ArrowNormal`; ends `SurvivalCurrency` | 48 names, 21-byte trailers; quantities and ammo copies change plausibly across slots | Eager bounded table, read-only F4 reference IDs, 14 eligible quantity types writable |
| Secondary inventory registry | Second identical 48-name anchor/end | 16 bytes after each name, all zero across every supplied slot; name sequence matches primary | Read-only native decoder evidence; all bytes preserved |

The primary trailer is `uint32 unknown/eligibility word`, `byte unknown/eligibility flag`,
`uint32 quantity`, `uint32 ammo count copy`, `uint32 unknown`, `uint32 unknown`. Quantity is unaligned
at trailer +5, and the copy is +9. Tsushima's five aligned uint32 record writer is **not** reused.
No `LegendsInvestment` anchor occurs. Tsushima's earlier named ammo/checksum research was a lead;
the new Yōtei offsets, extra byte and eligibility were independently checked across all generations.

Gear byte pairs: `00 01` for 1,009 names, `01 00` for 20, `01 01` for 8. Since they never change,
editing them as ownership/equipped flags would be an unsupported structural guess. Name-family
categories are lexical conveniences, not game categories or acquisition data.

## Enabled quantities and bounds

Eligibility is fixed when opening: known record name below, word0 == 1, byte4 == 1, matching ammo
copies and matching candidate checksum. Locked/unmapped/mismatched rows stay inspection-only.
Bounds are 0 through the observed maximum; if a matching original record is larger, its bound expands
to preserve undo. Flags, unknown words, registry data, metadata and sibling saves never change.

| Record | Observed maximum | Record | Observed maximum |
| --- | ---: | --- | ---: |
| Mon | 112099 | Flowers | 22 |
| IngredComWood | 95099 | IngredComCloth | 52 |
| IngredComMetal | 13 | IngredRarMetal | 10 |
| IngredUncPowder | 50 | IngredComFood | 1 |
| IngredComPlant | 3 | ArrowNormal | 9 |
| ArrowFire | 3 | ArrowHeavy | 8 |
| ArrowDisarm | 4 | AmmoSake | 1 |

All quantities are Inferred. Resource names remain raw except the lexical Mon/Flowers mapping; no
Young Bamboo/metal rarity/food effect is guessed. `yotei/inventory/set-quantity` uses F6 pure preview,
per-record skips/bounds, derived ammo copies/checksum, stale/forged-plan rejection and one Core
undo/redo group. Empty selection is empty. Query scope is rejected because the only F8 collection
is the read-only gear registry and inventory is a fixed 48-row table. Cross-title actions are rejected.

Tests edit every eligible type across every slot, serialize/reopen, compare the entire expected byte
array, restrict F1 changes to selected count/copy/checksum ranges, reject invalid types/values and
verify scalar plus grouped Core undo/redo. Byte-identical no-op includes both empty placeholders.

## Guides, assets and remaining unknowns

The embedded pack has 48 raw inventory references, 10 technique guides and 16 hot spring guides.
All reference IDs are browse-only. Techniques form an F9 graph with Unknown states and no invented
prerequisite edges. World has an F9 map and a complete text table; the 1920×1080 Horizon View screenshot
is a local excerpt, not a whole-island map. Only its visible Horizon View pin uses original image pixel
(960,550); the other 15 locations stay unpositioned. No saved-world coordinate transform is claimed.

Three real screenshot symbols and one WebP excerpt total **89,702 new asset bytes**, with full
source hashes/processing in the adjacent `*.source.md` sidecars and manifest. The owner-approved
rights-reserved attribution appears in Info.References, rows/pickers, graph/map descriptions and the
Sources panel. Descriptions are original short evidence/guide summaries; no official effect text or
individual gear/technique icon is invented from an unverified saved-ID join. Missing packs/art retain
raw data and text fallbacks.

Play time, health, spirit, active difficulty, skill-point balances, technique hashes/prerequisites,
gear ownership/equipped joins, collectibles, quest progress and discovery/completion/reward state
remain unknown. Medium/Ship strings alone do not establish active difficulty; all sample subtitles
are blank, so Tsushima's timer/subtitle correlation cannot be transplanted.

## Exact owner captures for #31

Use a disposable slot; record PS5 title/patch/build, region, mode, NG/NG+, DLC and locale, all menu
values and all participating gameplay/memory/SFO files. Preserve an unchanged load/save control first.

1. Restore an unchanged export; then separately edit Mon, Flowers, one named ingredient and one arrow
   by +1 within the displayed bound. Load, photograph balance, consume/buy one, resave, reload and
   re-export. Repeat the F6 multi-row edit once; record rejection/clamping/normalization and sibling changes.
2. Separate before/after pickups or purchases of exactly one of each ingredient/raw ammo type; fire
   one arrow then pick one up; upgrade one carry pouch. Photograph exact names, counts and capacity.
   This resolves raw-ID labels, flag roles, mirrored counts, intended limits and unusually high balances.
3. Equip one already-owned armor, weapon, mask and charm in separate pairs; acquire one new item,
   buy one dye and upgrade one tier in separate pairs. Record loadout and ownership menus at each step.
4. Earn one technique point, spend one on a named technique, then buy one prerequisite-dependent
   technique in separate pairs. Record points, branches, prerequisite and unlocked state each time.
5. Discover Horizon View separately from using the hot spring and receiving its health reward. Repeat
   one shrine/collectible and one quest stage separately; record map/journal/reward and coordinates if exposed.
6. Stand idle for exactly 60 seconds, then save; separately take one damage event/heal and change one
   difficulty setting. Record displayed time/health/spirit/difficulty and any subtitle before/after.
7. Save identical gameplay into manual, auto and memory roles, with an unchanged load/save control.
   Supply any naturally occurring nonzero trailing data or a disposable console checksum/load/resave
   experiment to distinguish declared-end versus full-file coverage. Do not use primary saves for this.

## Measurements

Largest Yōtei declared payload: User4, 616,448 file bytes / 417,135 content end. Standalone Debug
stopwatch after import, current-thread allocated bytes; cold F2 store + Open + BuildView and mean of
20 cached opens. Baseline was before the Yōtei implementation/pack, so this measures both new parsing
and presentation, not isolated art decoding. Baseline scratch was on C: before the owner's disk rule;
final scratch is on D:. Single cold timings are approximate and cannot establish a speed improvement.

| Operation | Before Yōtei changes | After |
| --- | ---: | ---: |
| Cold store + Open + BuildView | 127.247 ms / 13,426,032 bytes | 94.147 ms / 15,548,768 bytes |
| Cached Open + BuildView, mean of 20 | 0.300 ms / 704,839 bytes | 2.252 ms / 1,553,952 bytes |

The shared F2 store loads/caches once. GetAsset reuses cached content; plugin code performs no I/O
or image decode. Gear rows are bounded pages rather than 1,037 eager field nodes. No timing assertion
is added. The prior C: scratch created by this task was moved to D:; no unrelated TEMP content was touched.
