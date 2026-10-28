# Witcher3 implementation evidence — 2026-10-28

Scope: F1/F2/F3/F4/F8 rich reads/catalogs, plus the write-enable round's existing durability,
non-core skill ranks, Gwent copies and F6 bulk repair. Phases 2–4 are partial; no whole phase is claimed complete.
All gameplay meanings remain Inferred or Unknown; plugin maturity remains Experimental.

## Compatibility matrix

Every row is **PS5 / PPSA04018**, outer SAV3 **64/23/163**, with the same tuple in the structurally
reached player archive. Patch version, geographic region, NG/NG+ and mod state are **Unknown**.
The observed early-game state does not establish those metadata fields. The `.req` list is exactly
`content0;content1;content2;` on all eight files; it does not prove the installed DLC/version profile.

| Primary suffix | Compressed bytes | Expanded bytes | Names / variables | Items | XP | Crowns / Bread | F1 ZIP generation |
| --- | ---: | ---: | --- | ---: | ---: | --- | ---: |
| `CheckPt.53db8.7e73c800.313c5b3.sav` | 201,094 | 653,545 | 1,980 / 16,082 | 24 | 0 | 250 / 5 | 0 |
| `CheckPt.8559a.7e73c800.317a3e7.sav` | 218,715 | 702,856 | 2,053 / 17,399 | 25 | 10 | 250 / 4 | 0 |
| `CheckPt.8559a.7e73c800.31c45eb.sav` | 219,563 | 703,834 | 2,079 / 17,375 | 30 | 260 | 250 / 4 | 0 |
| `Manual.8559a.7e73c800.31c94f5.sav` | 228,734 | 724,174 | 2,106 / 18,445 | 30 | 260 | 250 / 4 | 0 |

These are four distinct **slots/snapshots**, not four repeated ZIP occurrences or eight independent
playthroughs. F1 lists every file at generation 0. Each primary has a byte-identical backup twin.
SFO labels show Kaer Morhen then Lilac and Gooseberries (German labels in the later saves), with
timestamps on 2023-08-19; those timestamps are metadata, not a verified patch/capture protocol.

Primary SHA-256 fingerprints, in the same order:

```text
14f597ad69e1bf7b60ee9df4074e00cdb3067e432a41e7a109738afe1c948f06
904ac6a97d32362381d5f65023ed71131cad7fd996633cb046d814c95adfc0e7
5b1bb0c4048669b7eb9a8b2e9d1755e80dd1a86e48e298499afca67de0adcb00
54cd9fdce5e35a902a96509129a93e7151fd650798a315bb8658daaac274fc9f
```

All eight records reproduce 102 skills, 16 skill slots, 4 mutagen slots, 13 mutations, 6 base-stat and
14 resistance records with exact array consumption. Sixteen level-1 core skills are definitions,
not a purchase count. Some skill records have no native identity; their read-only slot labels make
that absence explicit. Gwent contains 23 ordinary indices plus 4 leaders, totaling 35 copies.
The ordinary/leader declared counts are checked independently. Empty groups are tested synthetically.

## F1 comparisons, aligned by entity and native item IDs

The test-side [WitcherSemanticDecoder](SaveToolkit.Plugins.Witcher3.Tests/WitcherSemanticDecoder.cs)
implements F1's `ISaveDecoder`. It emits `entity/<IdTag>/inventory/<instance>/...` paths, named ability
identities where saved, and `gwent/collection/<index>/copies`. It resolves CName/enum indices before
comparison and records typed progression/quantities/durability, inventory lengths, directory counts
and pointer changes. Seven opaque item-prefix bytes are represented by SHA-256; unknown nested
ability properties retain their byte lengths and hashes. Such opaque hash changes can include
dictionary-index churn and are not interpreted as gameplay effects.

F1 `SemanticDiffer.Compare` produces these unmasked results (no warnings or truncation):

| Pair | Before / after values | Added | Removed | Changed | Selected verified observations |
| --- | --- | ---: | ---: | ---: | --- |
| `313c5b3 → 317a3e7` | 1,802 / 1,812 | 20 | 10 | 23 | Bread instance 44: 5→4; XP 0→10; letter instance 48 appears; bolt instance 33 disappears and 49 appears; mounted slots and durability change. |
| `317a3e7 → 31c45eb` | 1,812 / 1,861 | 60 | 11 | 10 | XP 10→260; silver sword instance 38 gains `silversword` slot; five monster-material records 50–54 appear; bolt 49 disappears and 55 appears. |
| `31c45eb → 31c94f5` | 1,861 / 1,861 | 0 | 0 | 5 | Inventory IDs, quantities, slots and durability remain unchanged; container/directory and a saved stat value change. |

Bread remains instance 44 and Crowns remains instance 46 in all four snapshots. **Bodkin Bolt does
not retain instance identity across the first two transitions.** Its matching name and quantity do
not justify merging those records into a single logical item. Gwent collection pairs are unchanged
across every comparison. Multiple unrelated changes make these unsuitable for proving a single
consume, repair, equip, reward or card operation. The owner now enables bounded stored durability edits
with warnings; automatic stack rules remain unimplemented because legal per-item limits are unknown.

Reproduce the CLI inventory and the F1 hook tests from the repository root:

```powershell
dotnet run --project tools/SaveToolkit.SaveDiff -- list 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts/PPSA04018_saves_dec.zip'
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/Witcher3/SaveToolkit.Plugins.Witcher3.Tests
```

`WitcherDifferentialTests.F1AlignsAllFourIndependentSnapshotsByEntityAndItemIds` emits complete F1 JSON
through xUnit output, checks byte-identical twins and asserts identity-sensitive changes. To save that
output outside the repository after building, the xUnit runner also supports:

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; & 'plugins/Witcher3/SaveToolkit.Plugins.Witcher3.Tests/bin/Debug/net10.0/SaveToolkit.Plugins.Witcher3.Tests.exe' -class '*WitcherDifferentialTests' -result-xml "$env:TEMP/witcher3-f1.xml"
```

No save, extracted game file or raw sample fixture is committed. The original CLI list and complete
semantic reports were generated under `%TEMP%/ps5plans/scratch/Witcher3/`. The CLI's view decoder
does not enumerate F8 collections, so the native hook is exercised through the shared F1 engine.

## Remaining implementation and owner checklist

Round 2 implements the remaining observed Phase 1 recipe/journal/facts readers described below.
Phase 1's broader compatibility matrix still needs exact owner metadata and additional fixture classes.
Phase 2 needs proven gameplay durability/stack rules and actual stash/Roach identity joins. Raw rank
and collection-count edits are enabled with dependency warnings; coherent Phases 3–6 transactions
remain unimplemented. The catalogs cover the complete reviewed wiki/community snapshot,
with unpublished native IDs explicit; this is not an installed-game database.

For every capture, supply title ID, console patch/version, outer/nested tuple if available, DLC list,
NG/NG+, mods, the complete decrypted spare slot and companions, plus an exact action log. Capture
independent before/after pairs (and a save→reload→save control pair):

1. Buy one Bread, consume one Bread, drop/pick up one ordinary stack; record the inventory count and
   money before/after. Include a stack near an observed game cap.
2. Repair exactly one damaged steel sword at a blacksmith; record durability and Crowns on both sides.
3. Transfer one ordinary stack into/out of stash; separately equip/unequip one Roach item and one
   ordinary combat item. Record displaced items and avoid combining these actions in one pair.
4. Earn XP across one level; buy one non-core skill rank; equip/unequip that skill. Keep each transition
   separate and record free/spent points, slots and displayed stats.
5. Buy/win one Gwent card, change one deck, learn one recipe and discover one signpost as separate pairs.
6. Provide mid/endgame and current-patch samples, HoS/B&W, NG+, Ciri, a large stash and socketed/enchanted
   gear to test each feature's schema/ownership boundary.
7. For Experimental level/XP/ability/Crowns/ordinary-stack/durability/rank/Gwent-count edits: restore only a spare slot,
   verify intended values and unrelated inventory/equipment/quest state, transition areas and check
   combat, save natively, reload and provide the decrypted re-export. Record which values persist.
   Only the owner can approve Stable. See the additional write-round checklist below and
   [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31); these checks no longer gate enablement.

No story write is enabled. Any later story preset requires its own exact branch/objective pair plus
quest continuation and world revisit; a parseable file is insufficient evidence.

## Round 2 — catalogs and remaining observed rich reads

Version 0.4.0 replaces the starter catalog with 4,011 item references (3,670 native keys and 341
explicit wiki-only keys), 107 named abilities/mutations and 225 Gwent native indices. See
[CATALOG.md](CATALOG.md) for extraction, pinned revisions/commits, share-alike/MIT terms and exact
coverage limits. All 95 named skill definitions and all 27 saved Gwent indices join in all eight
fixtures. Bookkeeping/default/sentinel identities remain raw. Wiki-only keys never become native IDs.

F2/F3/F4/F8 expose complete catalogs, attributed descriptions and read-only lookup pickers. Gwent
shows saved copies separately from community copy targets, acquisition and missable advice; aliases
are separate indices. Missing records do not prove obtainability or achievement eligibility. Three
unmapped numeric stats entries (26/27/28) remain unnamed; no sampled card index is unresolved.

Independent scratch Python inspection and the new C# readers agree on these counts for the four
primary snapshots, with identical results on each backup twin:

| Primary suffix | Crafting / alchemy recipes | Journal entries | Facts | Nonzero opaque fact words |
| --- | --- | ---: | ---: | ---: |
| `313c5b3` | 54 / 14 | 170 | 429 | 0 |
| `317a3e7` | 54 / 14 | 181 | 611 | 183 |
| `31c45eb` | 54 / 14 | 192 | 614 | 183 |
| `31c94f5` | 54 / 14 | 198 | 617 | 183 |

- Recipes: player-owned Entity `craftingSchematics` and `alchemyRecipes`, explicit counted
  `array:2,0,CName`; exact size, unique valid dictionary indices. Absent arrays are unavailable,
  not an inferred empty list. No recipe initialization/grant dependencies are established.
- Journal: root `CJournalManager` → counted `JActiveEntries` → `SJournalEntryStatus`. Each path
  consumes GUID/resource/flags triples; native status enum and unread bool are validated. Empty
  resources are present on GUID-only descendants and retained in the full path. Other entry data,
  including advanced-info payloads, remains in the raw image. Path hashes are stable row identities;
  no GUID-to-localized-title or completion claim is made.
- Facts: root `facts` → unique SS → matching SXAP tuple → SBDF count → compact-length names,
  opaque uint16, entry count, ten-byte int16/uint32/uint32 entries → exact EBDF end. Time and expiry
  remain exact bits, including nonfinite synthetic vectors. Some bounded wide-name payloads contain
  controls/invalid surrogates; their meaning is Unknown. Their original bytes are retained and hashed,
  never normalized into invented names. A positive entry or sum is not interpreted as quest completion.

These are independent optional projections. Unsupported/corrupt readers warn separately and leave
raw data and the existing preserving writer available. Queries return at most 200 rows; fact-entry
and journal-path detail tables page 64 rows. No new writer, bulk preset or plugin-local transaction
mechanism was added in round 2. The following write round adds F6 repair and bounded stored-value edits.

Validation: **79 plugin tests passed, 0 failed, 0 skipped** with `SAVETOOLKIT_ARTIFACTS` exported in
the test command; full API build **0 warnings / 0 errors**. New coverage audits every catalog record,
licenses/pins/unknown keys, full bounded paging, friendly joins, exact sample counts, unchanged no-op
exports, malformed recipe indices/counts, facts markers/counts/encodings, journal strings/status/bools,
opaque name preservation and independent fallback. Existing edit conformance and byte-preservation
tests still pass. No console was accessed. The owner checklist tracks validation and promotion to Stable.
No blocking SDK request; the earlier F1 CLI collection visitor request remains.

## Write-enable round — version 0.5.0

Owner instruction: **Enable writes. Keep warnings.** The README's enabled-writes table defines four
new capabilities: individual durability, existing non-core skill rank, existing ordinary/leader Gwent
copies and F6 bulk repair. All remain Inferred/Experimental. No new source dataset or third-party
code port was added; pinned MIT converter/tracker sources were re-read, and existing attribution and
licenses remain. Bounds are the saved skill maxLevel, conservative durability 0..100 and observed
ordinary/leader card copies (3/1), widened only to preserve a larger original card count.

Tests exercise all eight actual records, exact float/Int32 patch bytes, F1 intended paths, invalid
values and types, native reparse/reopen, session undo/redo and repair group undo. An in-memory variant
removes a skill's zero property to verify 12-byte default insertion and all relocation metadata;
every unrelated byte is compared after alignment. Mixed rank-page/progression/durability/Gwent edits
also exercise the first save's missing `free` dictionary entry. No fixture bytes are stored in the repo.
Sentinel durability (-1), core skills and unnamed definitions remain unchanged. Repair previews are
pure, list skipped rows/warnings, support resolved F8 queries, and reject stale/forged previews.

Exact additional owner checks for [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31):

1. Capture a no-action save → load → save control, recording title/build/region/DLC/NG/mod state.
2. On a spare slot, change one damaged steel sword's stored durability, then separately bulk-repair
   two damaged items. Verify displayed durability, combat damage, unrelated inventory, Crowns and
   repair rewards/achievements; native save, reload and decrypted re-export each case. Also capture
   a genuine blacksmith repair pair with displayed durability/Crowns to establish the scale.
3. Rank one non-core skill up, then down, as separate cases. Verify rank, free/spent points, path
   spending, prerequisites, equipped slots, active modifiers and displayed stats; fight, transition
   area, native save/reload and re-export. Include a native purchase and equip pair for comparison.
4. Change an existing ordinary-card duplicate count; separately remove/restore an existing leader
   count. Verify collection, selected deck validity, card availability during a match, quest/reward/
   achievement state; native save/reload/re-export. Capture one genuine purchase/win and deck change.
5. Repeat the earlier level/XP/ability/Crowns/quantity checklist; report whether each edited value
   persisted or was rebuilt by the game. Only the owner may approve Stable.

No F5/F7 transaction existed in the prior implementation to enable. Item identity allocation,
ownership/reference cleanup, equipment exchange, mutation research/respec and story/world dependency
models are incomplete; creating them by guessing can corrupt unknown structure. They remain
unimplemented, with no remaining console-evidence switch blocking the four new capabilities.

Write-round validation: **88 passed / 0 failed / 0 skipped** with `SAVETOOLKIT_ARTIFACTS` exported in
the same test command. Full API build succeeds with **0 warnings / 0 errors**. Changes stay entirely
inside `plugins/Witcher3/`; no sample files, credentials, game assets or new source datasets were added.

## Completion round — version 0.6.0

See [COMPLETION.md](COMPLETION.md) for every requirement/acceptance disposition and the exact
additional #31 capture checklist; README records F1–F11 adoption and pending F12/F13 (#32/#33).

- F9 skill graphs retain native branches/costs/current ranks without fabricated prerequisite edges.
  Mutation graphs decode 13 definitions and 11 exact `requiredMutations` edges in every supplied
  save. Research/availability is Unknown; core ranks are not purchases. Positive editable ranks
  represent stored rank only. Changes and undo/redo rebuild the published diagram.
- Native map reads validate separate counted known/discovered/disabled CName blocks under the
  unique `CCommonMapManager`. No absent tag becomes Missing; discovery is not completion/reward.
  Friendly region/coordinates still need matching native definitions and controlled captures.
  Community map links are fully attributed and external-only: no NC artwork/coordinates/marker
  dataset is imported. Existing friendly ability reference attribution stays visible in graphs.
- F8 full ability/map queries return bounded pages; direct lazy targets navigate to unloaded late
  details. Synthetic tests exercise 4,096 skills and 1,650 map tags; malformed graph/name/count/type/
  cycle/duplicate inputs fail with warnings or clear format exceptions.
- One additional enabled write capability: F6 refill of existing ordinary native Bread/Bottled water
  only when unequipped, unslotted and untagged. Imported quantities are ceilings, not legal caps.
  Higher/already-full, quest variants, unknown IDs, currency and protected records are skipped.
  All eight files have exact native-byte/F1/reopen/history checks for eligible refill stacks.
  Equipped water is correctly excluded; owner should provide two eligible stacks for a native
  multi-stack trial. Direct invocation and forged/stale/invalid scopes are rejected.
- Direct progression/quantity wrong-type/range validation now rejects atomically; parent record
  bounds reject truncated/out-of-range endpoints. Skill lookups use an indexed dictionary during
  view, write and reparse verification instead of repeated full-rank scans.

Software validation: **99 passed / 0 failed / 0 skipped**; restore-enabled API build **0 warnings /
0 errors**. All C# files remain below 300 physical lines. No console was accessed, no file outside
the plugin changed, and no sample/asset bytes were committed. All earlier owner validation remains
open; existing implemented writes stay enabled and Experimental.
