# BG3 evidence corpus and remaining gates

Inspected 2026-10-28. The corpus contains no saves, extracted bytes, account IDs,
player names or console metadata values. `fixtures.json` pins the source archive
by SHA-256. The only current fixture is explicitly a **baseline self-comparison**,
not an in-game event or no-action resave pair.

## Reproduce the current evidence

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
dotnet run --project tools/SaveToolkit.SaveDiff -- list "$env:SAVETOOLKIT_ARTIFACTS/PPSA18463_saves_dec.zip"
dotnet run --project tools/SaveToolkit.SaveDiff -- verify plugins/BaldursGate3/fixtures.json --format json
dotnet test --project plugins/BaldursGate3/SaveToolkit.Plugins.BaldursGate3.Tests
```

`list` can show console title metadata and system-file fingerprints: keep its raw
output private. This document records only the non-identifying aggregate result.

F1 finds four save directories (primary/backup Profile and Save0001), all generation
0. The package fixture compares two copies of the same source snapshot, reports
two unchanged package files, one identical primary/backup pair and zero changed
bytes. There is no generation 1. Identical files short-circuit before F1's
`MirrorsMain` status, so both files are reported `Unchanged`. No churn exclusions
are claimed. A no-action **resave** control still needs to be captured.

The sample-specific tests check [the independent LSMF census](LSMF.md), 761 saved
LSF Item occurrences, four exact referenced gold-template occurrences and twenty
healing-potion-template occurrences. The seven-record starter pack labels 40/761
saved items, plus the sample's Laezel/DarkUrge origins. The third origin stays raw.
These are reference-label matches, not inventory totals or known in-game counts.
All-owner item rows must not be summed as player gold. SaveInfo's three level-1
rows and XP `(30,30)`, `(30,30)`, `(0,0)` remain metadata-only.

F1 integration is also tested with synthetic changes in an unknown component,
opaque header and trailing bytes: the existing view decoder reports the whole
NewAge fingerprint and, where applicable, the nominal component fingerprint,
without inventing a gameplay interpretation. Synthetic
tests cover version/count/range corruption, truncation, GUID byte order, repeated
GUIDs/templates, bounded pages, no-pack/build-mismatch fallbacks and attribution.

Round 2 adds six catalog datasets (`bg3_items`, `bg3_equipment`, `bg3_spells`,
`bg3_passives`, `bg3_companions`, `bg3_camp_supplies`) with pinned page revisions,
record-level source links and CC BY-SA attribution. The catalogs contain factual
labels and technical candidates only; no legacy wiki prose, icons or game assets are
embedded. Catalog and all-owner inventory collections implement bounded SDK query
pages with stable row IDs. Explicit owner/quantity attributes are surfaced as
unverified candidates when present; positional and quantity-only joins remain rejected.
The NewAge view now groups character/health/classes/spellbook/container/stack families
as read-only component candidates. StorySave inspection reports only bounded ASCII
database markers (`DB_ApprovalRating`, roster/team families and related names); it does
not decode Osiris tuples or claim approval, quest, camp or romance state.

## Phase status for this round

- **Phase 1, partial:** directory/arena range validation, duplicate EntityId
  provenance, independent census, bounded views and F1 baseline shipped. Ownerlist,
  packed-stream and character/Creator joins remain open; full phase exit is not met.
- **Phase 2, expanded subset:** F2 integration and attributed English catalogs for
  items, equipment, spells/passives, origins/companions and camp supplies shipped.
  No matching full game-data pack, localization/inheritance/override
  resolver, icons, limits or mod/build corpus is available; full phase exit is open.
- **Phase 3, expanded read-only subset:** metadata party, all-owner inventory candidates,
  queryable catalogs, component-family live-state candidates and bounded story marker
  inspection shipped. Live character sheets, proven inventories/equipment/camp,
  quantities, recursive containers and story tuple values remain open; full phase exit
  is open.
- **Phases 4–7:** no new gameplay writes, structural actions or story actions.
  Existing experimental primitive editing is unchanged. No console compatibility
  claim or maturity promotion is made.

## Exact next owner capture checklist

Use the same native PS5 build, character and slot for each pair. Record console
game version, region, difficulty/ruleset, language, DLC, mods/load order, action,
shown before/after values and capture date, without account/PSN identifiers.
Follow `docs/SAMPLES.md`; put dumps only in the external artifacts directory.

1. **Control:** save/export; reload, take no action, save/export again in the same
   place. This establishes churn; retain both untouched dumps.
2. **Gold:** before/after gaining exactly one gold and, separately, spending one
   gold. Note the acting character, exact stack and all visible party gold totals.
3. **Potion:** consume one healing potion from a settled known stack, with before/
   after count and HP. Supply the known inventory screenshot/transcription.
4. **Split/merge:** split a known non-quest stack, then merge it; capture each event
   separately and take a second no-action settled save after each rearrangement.
5. **Transfer/bags:** move one known item between two named party members; separately
   move it into/out of a bag and camp chest. Record stack count, owner and location;
   include a second settled save after every move.
6. **Identity/equipment:** provide Act 1–3/camp examples, hireling/origin/Dark Urge
   and identical-build multiplayer avatars, duplicate copies, rings, dual wield,
   torch/instrument equip/unequip pairs with visible character sheets/loadouts.
7. **Character/story (later):** separate damage/heal/temp-HP, boosted-gear,
   learn/prepare-spell, level-up/respec, quest accept/complete/fail, approval ±,
   rest/romance and recipe/waypoint event pairs before enabling those features.
   Include one dump whose StorySave marker set is unchanged by a no-action resave and
   one controlled approval/quest event with the corresponding visible UI outcome; this
   is required before marker presence can be promoted to a tuple decoder.
8. **Reference rights:** provide build/language/module-matched permitted template,
   stats/LOCA data and a rights manifest; decide icon/commercial scope, legacy
   wiki content, spoiler policy and required write-validation standard.
9. **Console gate:** first restore an untouched re-export into a spare slot and
   load/resave/re-export it. Test metadata-only changes separately. Test any future
   live edit only after its evidence fixture exists; confirm effect, unchanged
   unrelated characters, rest/trade/combat where relevant, resave, reload and redump.
   Supply warnings/failures as well as successful results. Only the owner promotes
   maturity to Stable.

No new SDK request blocks this subset. F1's existing view adapter, F2 packs/context,
F4 reference fields and F8 queryable collections are used read-only. F6 and structural
undo remain framework-owned. Future full parser/evidence work is still required before
those UI features could safely expose gameplay operations.
