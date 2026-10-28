# Witcher3 implementation evidence — 2026-10-28

Scope: requested F1/F2/F3/F4/F8 foundations, plus bounded ability/Gwent reads from Phase 1 and richer
inventory reads from Phase 2. Neither entire phase is complete. No new gameplay write class is enabled.
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
consume, repair, equip, reward or card operation. Durability writes and automatic stack rules stay gated.

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

Phase 1 still needs typed recipes, journal resource/GUID/status joins and exactly consumed facts
including opaque words/time entries. They remain in the preserving raw explorer; prior planning
observations are not claimed as implemented readers. Phase 2 still needs controlled durability/stack
rules and actual stash/Roach identity joins. Phases 3–6 write dependencies remain open. The current
pack intentionally covers 18 reviewed IDs, not a comprehensive installed-game catalog.

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
7. For existing Experimental level/XP/ability/Crowns/ordinary-stack edits: restore only a spare slot,
   verify intended values and unrelated inventory/equipment/quest state, transition areas and check
   combat, save natively, reload and provide the decrypted re-export. Record which values persist.
   Only the owner can approve Stable.

No story write is enabled. Any later story preset requires its own exact branch/objective pair plus
quest continuation and world revisit; a parseable file is insufficient evidence.
