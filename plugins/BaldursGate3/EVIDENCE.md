# BG3 evidence corpus and owner validation

## Completion round (0.6.0)

This section supersedes historical read-only/live-writer limitations below. The
owner directive remains **Enable writes. Keep warnings.** The supported live-storage
paths are enabled; no implemented write waits for a console test. [NEWAGE.md](NEWAGE.md)
is the current detailed layout, bounds and owner-check record. All new meanings
remain Unknown and the plugin stays Experimental.

Shipped: independent bounded ownerlist candidates, explicit stack/member amounts,
current container entries, PS5 Health/Stats profiles, class selections and spell
books. The sample census is 38 groups / 39 member quantities; 638 entries / 239
nonempty containers; 380 Health rows; 256 Stats rows; 256 class selections;
291 books / 2,242 spell occurrences. These are **storage counts, not party totals**.
Creator/template candidates preserve repeated EntityId provenance and avoid class,
transform, largest-book or adjacent-entity identity guesses.

Enabled five new write kinds plus two actions: existing member quantity, current
HP candidate, temporary HP candidate, effective ability-cache family, and two-key
container reorder; F6 imported-quantity restoration reuses the quantity writer,
and the reorder uses F6 planning/F5 scoped mementos. Current/temporary HP and each
ability have independent bounds. No base-score, respec or current-party guarantee.
Every field/action documents absent console validation, unknown inner integrity /
dependencies and possible game rejection. Known outer digest repair remains on.

Final verification: **200 passed, 0 failed, 0 skipped** with the actual sample
environment exported in the test command. An empty artifacts directory gives
**170 passed, 30 skipped, 0 failed**. API build: zero warnings/errors. F1 fixture
verification: one baseline, two unchanged files, identical backup pair and zero
changed bytes. All changes remain within this plugin; no sample data is committed.

### Updated gap audit (plan and all issue handoffs)

| Plan requirements / acceptance area | Classification | Result / remaining limit |
| --- | --- | --- |
| P1 / R12: directory, bounds, EntityId occurrences, heaps/strings, ownerlists | Do now: shipped supported PS5 profiles | Dynamic directory retained; bounded explicit references and owner candidates added. Arbitrary archetype shifts/enum pools remain unimplemented; unsupported families have raw diagnostics. |
| P1/P3 / R1/R10/R11/R15: authoritative party/body/stats/spell/owner joins, duplicate builds, bags and transient stacks | Owner-blocked calibration plus unimplemented general joins | Existing snapshot has no native event/control ground truth; supported storage stays Unknown. Full identity/recursive-inventory acceptance is not claimed. Exact captures remain in #31/NEWAGE.md. |
| P2 / R4/R6/R7/R8/R18: framework data injection, cells, pickers, paging | Do now: used | F2/F3/F4/F8 adopted; current-template references stay read-only because replacement effects are unmodeled. Rebased onto main for F12/F13: bounded live F8 semantic traversal, browse-only references and host catalogue filter/sort. |
| P2 / R3/R19/R20/R21: matching multilingual/module data, full descriptions/art/icons and offline asset conversion | Owner-blocked matching data/rights | Existing permitted attributed subset retained. No matching source pack/artwork is present; no giant extracted dataset or speculative icon conversion is embedded. Existing permission to use clearly licensed data is honored. |
| P4 / R1/R12/R14/R15: existing stack quantities | Do now: shipped | Explicit NewStack→member/entry references, positive sums, fixed u32 patches and intended-byte tests. Player gold ownership/target-total distribution remains unavailable; world/trader gold is not merged. |
| P5: character values/spells | Do now: supported subset shipped | Current/temp HP and effective ability-cache candidates enabled. Classes and spell presence inspectable. Base/derived authority, boosts, learned/prepared/source joins and dependent progression writes remain unresolved. |
| P6 / R5/R16/R17/R22: F5/F6 inventory transactions | Do now: fixed-shape reorder/restoration shipped | Two-key swap preserves all allocation/references and has scoped structural undo. F6 quantity restoration supports #27 query scopes/exclusions. |
| P6: allocate/delete/transfer/equip/split/merge | Won't do in this completion implementation | Safe cross-resource entity allocation, relocation, stack/ownership/equipment-effect repair is not modeled. Guessing would alter unknown structure. A two-key reorder does not establish those algorithms; no unsupported generic action is exposed. |
| P7: quest/approval/romance/waypoint mutations | Won't do in this completion implementation | No event/state-machine/dependency writer; markers/presence do not prove effective state. Event captures and native validation remain in #31. |
| R2/R10/R11: PS5 acceptance and edge-case captures | Owner-blocked #31 | Untouched/metadata/new live edit cases must be tested independently. Exact checklist is in NEWAGE.md, plus the historical broader capture list below. |
| R9 / optional F9 | Do now: spell presence graphs shipped | Unknown state, no invented prerequisite edges, raw-ID controls/text alternatives. Map not applicable without discovery/coordinate semantics and data. |
| R13: reuse unclear-license implementations | Won't do | Format research only; independent implementation and existing MIT notices. No downloaded parser/solver/executable copied or run. |
| R14: evidence manifest | Do now: retained | Sole baseline self-comparison; no event/control pair or churn mask fabricated. ByteDiff assertions cover intended new write ranges; F12 traversal is tested across live F8 pages, including incomplete-budget warnings. |
| Acceptance: scope, corruption/bounds, missing refs, exact no-op/undo, writes/reopen, source sizes, docs | Do now: software verification | Synthetic and real-sample tests cover each new family, wrong scopes/values, preservation and Core history; final results recorded in README/handoff. |
| Acceptance: native load/effect, authoritative identities/gold/journal, Act 3 breadth and timing | Owner-blocked, full phase exits not claimed | Need controlled native PS5 captures, matching data and larger supported fixtures. A single tutorial snapshot cannot establish these. |

The full seven-phase roadmap is **not complete**. Remaining general decoders and
dependency writers are explicitly implementation limits; they are not relabeled
as “waiting for console permission.” Only the supported bounded storage subset
and its software acceptance are completed this round.

### Owner validation added to #31

Follow all seven checks in [NEWAGE.md](NEWAGE.md#exact-owner-validation-31): an
untouched/no-action control; identified settled stack and multi-contribution
quantity edits; independent damage/heal/temp HP; each effective ability with and
without boost gear; two ordinary unequipped entries' map-key reorder; F6 selected /
filtered-excluded / document restoration and grouped undo; native resave/reload /
decrypted redump and unrelated-character/owner checks. Record build/ruleset/mods,
source rows and visible before/after outcomes, excluding account identifiers.
Historical metadata and broader transfer/equipment/spell/story captures below
remain requested. Only the owner may mark Stable.

## Historical write-enable round (0.5.0)

The owner's 2026-10-28 decision supersedes the old evidence gates: **Enable writes.
Keep warnings.** [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31)
tracks console validation separately. No enabled write waits for these captures.
The historical phase/capture notes below remain useful for validating semantics.

- Enabled three existing SaveInfo summary field kinds (level/current XP/total XP),
  nine cells in this sample. Confidence stays Inferred; these are metadata only.
- Enabled direct, unambiguous positive LSF Item integer quantity candidates with
  Unknown confidence and observed per-type bounds. The supplied 761 Item nodes
  contain **zero** such candidates: no runtime gold/quantity address is fabricated.
- All fields warn about absent console validation and possible game rejection.
  Known v18 MD5+1 is repaired/verified; other game consistency/dependency checks
  are unknown. Unknown NewAge/Osiris data stays byte-identical.
- Real-sample plugin run: **168 passed, 0 failed, 0 skipped**. All sample summary
  cells pass edit/reopen, F1 intended-token diff, compressed-entry preservation,
  range rejection and Core batch undo/redo. Synthetic direct quantities also pass
  persistence, byte/section preservation, invalid-batch rollback and session history.
- Live NewAge values, creation/removal/transfers/equipment and story actions have
  no safe writer for their packed/reference structure. They remain unavailable for
  this structural reason, not pending a console test. No prepared F5/F6/F7 action
  existed to switch on. Full roadmap phases 4–7 are not claimed complete.

### Current owner checks in #31

1. Spare-slot untouched export: load, resave, reload and redump; log warnings.
2. Separate metadata exports: change one party level 1→2, current XP 30→29 and
   total XP 30→29. Inspect save-selection summary and the unchanged live character
   sheet; resave/reload/redump each. Also test one multi-row metadata batch.
3. Test existing LeaderName metadata separately; confirm it does not rename the
   live character and record whether the game preserves or regenerates the summary.
4. Supply a same-build save with an explicit positive LSF Item quantity attribute,
   identify owner/stack and visible count, reduce that value by one, then
   load/use/trade/rest/resave/reload/redump and inspect unrelated owners. Such a
   control is enabled when present; the current snapshot cannot exercise it.
5. Continue the no-action, gold ±1, potion, split/merge/transfer, character/spell,
   equipment and story captures below to support the missing decoders and future
   confidence improvements. Record build/ruleset/mods and observed outcomes without
   account identifiers. Only the owner may promote Stable.

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
   rest/romance and recipe/waypoint event pairs for decoder development and confidence validation.
   Include one dump whose StorySave marker set is unchanged by a no-action resave and
   one controlled approval/quest event with the corresponding visible UI outcome; this
   is required before marker presence can be promoted to a tuple decoder.
8. **Reference rights:** provide build/language/module-matched permitted template,
   stats/LOCA data and a rights manifest; decide icon/commercial scope, legacy
   wiki content, spoiler policy and required write-validation standard.
9. **Console validation:** first restore an untouched re-export into a spare slot and
   load/resave/re-export it. Test metadata-only changes separately. Test any future
   enabled edit separately; confirm effect, unchanged
   unrelated characters, rest/trade/combat where relevant, resave, reload and redump.
   Supply warnings/failures as well as successful results. Only the owner promotes
   maturity to Stable.

No new SDK request blocks this subset. F1's existing view adapter, F2 packs/context,
F4 reference labels remain read-only; F8 quantity pages share canonical primitive
bindings. F5/F6/F7 remain framework-owned and are not needed for these scalar edits.
Future full parser work must preserve packed streams/references before safely
exposing structural gameplay operations.
