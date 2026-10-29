# Hogwarts talent and Field Guide discovery (#40)

Research date: **2026-10-29**. Result: **read-only, Inferred** talent-point,
learned-talent and collection-state fields. No new writable cells or grant action.
The former generic `DataValue` writer is disabled for `PerkPoints` and
`PageCollected*` rows, including unknown owners and operation without a catalogue.
Existing unrelated allowlisted cells remain Experimental.

## Method and limits

The only available Hogwarts archive is `PPSA01603_saves_dec.zip`. All **15 distinct
HL0000–HL0014 gameplay saves**, **30 independent database images**, were decoded
using `GvasFile`, `HogwartsImage` and shared in-memory `SqliteImage`. Research ran
read-only `SELECT` queries; synthetic databases alone use authored `CREATE`/`INSERT`.
The source archive was read in place; importer scratch and reports stayed under
`D:/Temp/saveeditor/test-scratch/research-unrealgvas-hogwarts`. No extracted private
database, row dump, personal identifiers or save bytes are committed.

Every slot is importer generation 0. Adjacent slot names establish neither event
causality nor chronology. Both images are measured separately; neither is declared
authoritative. Existing F1 row-hash/multiset tests compare all 14 adjacent pairs
and report 54 PerkDynamic and 860 CollectionDynamic multiset/row changes across
both images, without interpreting those changes as purchases or pickups.

## Stored fields and player relationship

| Field | Observed storage | Evidence and unresolved joins |
| --- | --- | --- |
| Unspent talent points | `MiscDataDynamic(DataOwner='Player0', DataName='PerkPoints').DataValue` | Exactly one row per image, SQLite **text**, values 4–9. Source [R1] identifies this owner/name. Retain the exact text; do not coerce or combine images. Unknown owners get an explicit unmapped-owner label. |
| Learned talents | `PerkDynamic(PerkID, Level)` | 15–25 unique, non-NULL PerkIDs per image, all Level = integer 1. No CharacterID/ActorID column. Save-local player association is Inferred from [R1]/[R3], not a relational player join. Level is not established as point cost. |
| Revelio collection entries | `CollectionDynamic(CategoryID, SubcategoryID, ItemID, ItemState, UpdateTime)`; `ItemID GLOB 'LORE_*'` | 45–56 distinct Obtained IDs, and 150 Unknown rows in **every** image. Every Obtained lore ID also has an Unknown row for the same category/subcategory/item tuple. Never choose a winning row or derive a completion percentage. |
| Moth/brazier/statue page flags | `MiscDataDynamic.DataName GLOB 'PageCollected*'`, `DataValue` | 11–20 rows per image; all observed values are text `1`. [R2] ties catalogue-qualified DataName IDs to those page types. An arbitrary prefix match is only a raw page-flag interpretation; absent rows do not prove uncollected, and no particular owner is assumed. |
| Flying pages and other map collections | `MapLocationDataDynamic(MapLocationID, State)` | [R2] uses State 11 for collected IDs. Existing exact table/column catalogue joins are retained; the state field now carries Inferred confidence. Neither an unknown ID nor every State 11 row is asserted to be a Field Guide page. |
| Field Guide challenge state | `AchievementDynamic(AchievementID, Instances, IsComplete, OneOfEach, RewardsDelayed)`; `AchievementID GLOB 'FGC_*'` | 58–76 tier rows per image. Counters, completion and delayed rewards are separate columns. Matching collection IDs to challenge/reward definitions remains unresolved. Expose raw values, including NULL, rather than inventing claimed rewards. |

All 30 images have empty `CharactersDynamic`, `BuffsDynamic` and `BuffSlotsDynamic`.
They have two `RPGPermaAbilityDynamic(ActorID, AbilityName)` rows, but **zero exact
AbilityName = PerkID matches**. `PerkDynamic.PerkID = LocksDynamic.LockID` likewise
has zero matches. No table SQL contains a `REFERENCES` declaration. These negative
joins do not exclude transformed IDs or runtime application of perks; no complete
talent-to-effect/actor/dependency map was obtained.

## Per-image regression values

Pairs below are **RawDatabaseImage / RawExclusiveImage**. Collection rows are
storage rows, not unique owned items. Conflicts count category/subcategory/item
groups having more than one distinct ItemState, not a count of corrupted items.

| Slot | Point text | Perks | Collection rows | Obtained lore IDs | Page flags | Conflicting tuples |
| --- | --- | --- | --- | --- | --- | --- |
| HL0000 | 9 / 8 | 15 / 15 | 1542 / 1542 | 47 / 47 | 11 / 11 | 206 / 206 |
| HL0001 | 9 / 9 | 15 / 15 | 1555 / 1545 | 49 / 49 | 11 / 11 | 212 / 208 |
| HL0002 | 8 / 8 | 17 / 17 | 1748 / 1736 | 49 / 49 | 11 / 11 | 226 / 226 |
| HL0003 | 9 / 9 | 17 / 17 | 1776 / 1776 | 49 / 49 | 11 / 11 | 226 / 226 |
| HL0004 | 4 / 4 | 23 / 23 | 1850 / 1850 | 49 / 49 | 11 / 11 | 248 / 248 |
| HL0005 | 4 / 4 | 23 / 23 | 1907 / 1850 | 49 / 49 | 11 / 11 | 299 / 248 |
| HL0006 | 5 / 4 | 23 / 23 | 2009 / 1850 | 51 / 49 | 13 / 11 | 314 / 248 |
| HL0007 | 5 / 4 | 25 / 23 | 2082 / 1850 | 56 / 49 | 20 / 11 | 331 / 248 |
| HL0008 | 7 / 6 | 15 / 15 | 1330 / 1274 | 46 / 45 | 11 / 11 | 183 / 176 |
| HL0009 | 8 / 8 | 15 / 15 | 1530 / 1471 | 46 / 46 | 11 / 11 | 199 / 197 |
| HL0010 | 4 / 4 | 25 / 23 | 2082 / 1850 | 56 / 49 | 20 / 11 | 331 / 248 |
| HL0011 | 5 / 4 | 25 / 23 | 2082 / 1850 | 56 / 49 | 20 / 11 | 331 / 248 |
| HL0012 | 5 / 4 | 25 / 23 | 2082 / 1850 | 56 / 49 | 20 / 11 | 331 / 248 |
| HL0013 | 4 / 4 | 25 / 23 | 2082 / 1850 | 56 / 49 | 20 / 11 | 331 / 248 |
| HL0014 | 4 / 4 | 25 / 23 | 2082 / 1850 | 56 / 49 | 20 / 11 | 331 / 248 |

One concrete uncontrolled difference, HL0010 → HL0011: primary point text changes
4 → 5 while its 25 perks, 2082 collection rows, 56 Obtained lore IDs and 20 page
flags stay equal. Completed FGC rows change 61 → 63 and nonzero RewardsDelayed
rows 7 → 9; the exclusive image retains 4 points / 23 perks / 1850 collection rows.
This cannot isolate talent earning, a reward claim or an unrelated progression event.

## Dependent systems and negative results

**Talents: no dependency-complete writer.** [R1] guards points against learned
talents, and its `playerEdits.ts` uses a vanilla lifetime ceiling of 36. Our observed
4–9 range does not prove a PS5 bound, initialization gate, spend/refund rule or
cost. [R5] separates ordinary talents and challenge-reward entries; it supplies no
build-matched PS5 grant transaction. No talent dictionary or costs were imported.
`RPGAbilityDynamic(AbilityID, ActorID, AssetPath, CurrentLifeTime, MaxLifeTime,
ResetOnSleep)` has zero or three rows here; that variation is not a perk-effect
join. [R3] connects it to Ancient Magic rewards on PC, not ordinary talent ranks.

**Collections: ownership is not use, completion, reward or achievement.** [R1]
updates `LootItemsDynamic(ItemID, Looted, ItemRandomWeight, ItemAdjustedWeight,
Variation)` for conjurations/appearance/traits and `LocksDynamic(LockID,
ELockState)` for wand handles, separately from CollectionDynamic. Its Revelio
operation explicitly leaves challenge XP and other page types alone. Category
spellings and counts are not blindly inherited: these PS5 images use `Gear`, not
the source's `Appearances`, and retain 150 Unknown lore rows, not its UI's 147.

Exact join probes reinforce that dependency existence does not imply completion:
in HL0010 RawDatabaseImage, 14 distinct WandStyle IDs join an unlocked (`0`) lock
under **each** of Unknown and Obtained states. Thirty-four distinct Conjurations
IDs join loot by exact ItemID or `Recipe_Transfiguration_` + ItemID under each
state. These are candidate joins, not a universal recipe or a validated grant.
Missing rows may require insertion, which is outside existing-cell allowlisting.

[R3] describes a PC live-event pipeline involving challenge counters, matching
locks, rewards and achievements. On PS5 the candidate cross-check surfaces are
`AchievementDynamic`, `LocksDynamic`, `MissionDynamic` (including Type, Text1–6,
Integer1–7), `StatsDynamic`, page/map flags, `LootItemsDynamic` and ability rows.
The supplied captures do not establish the quest prerequisite or reward joins,
whether delayed rewards have been claimed, the two-image merge/authority rule,
or trophy side effects. PC advice to update both images is not adopted.

**Write decision:** SQLite integrity and lossless GVAS round trips pass, but
bounds and dependencies are not all understood. Those are necessary gates even
with Experimental maturity, preview and undo available in the framework. No point,
talent, collection, challenge or reward grant is enabled. Required next captures:
same-slot no-action control; one point earned; one talent bought/refunded; one
identified page of each type collected; one challenge completed and its reward
claimed separately. Retain both images and companions, record build/region/DLC,
and verify actual effects plus native save/reload/re-export on a spare slot.

## Tests and reproducibility

`HogwartsDiscoveryViewTests` was written and run first: three red cases reproduced
editable point bindings and missing read-only semantics. The implementation
then passed all three. Authored GVAS + SQLite fixtures cover distinct images,
unmapped owners/talents/states, conflicting tuples and rowid paging, edit rejection
and atomic mixed batches, original-byte preservation, and operation with/without
the reference catalogue. Authored SQL raw strings normalize line endings to LF
before database-byte construction, so Windows CRLF checkouts and Linux agree.
The fixture also seeds inherited mutation fuzzing; no reference pack is changed.

`HogwartsDiscoverySampleTests` has 30 pinned cases for the values above and FGC
total/complete/delayed counts, negative joins and schemas. Each checks native SQLite
integrity, exact SQLite no-op serialize, stored-frame decode/reopen, byte-identical
GVAS no-op, and read-only point/perk/collection bindings. Existing all-sample
conformance, adjacent-slot F1 and mutation fuzz tests remain required.

Final validation on 2026-10-29: **349 passed, 0 failed, 0 skipped**, including
the 30 per-image cases, three synthetic view cases and one explicit contradictory
lock/loot-join case added here. Inherited mutation fuzz passed **800 cases** at
seed `0x40C0FFEE` (200 each for one authored GVAS+SQLite seed and three real-title
seeds), with zero failures or quarantines. API build: **0 warnings, 0 errors**.
Documentation checks (`python -B docs/tests/test_documentation.py --code-ref main`):
**28 passed**. The report matrix was also reconciled against all 30 independently
measured image summaries. `git diff --check` and the changed-file <300-line C#
check passed. Full-suite verification after fixture newline normalization again
passed 349 tests / 0 skipped and 800 mutation cases. Runner attachments/TRX are
under `$scratch/results-final` and `$scratch/results-normalized`.

```powershell
$scratch = 'D:/Temp/saveeditor/test-scratch/research-unrealgvas-hogwarts'
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP = $scratch
$env:TEMP = $env:TMP = $env:TMPDIR = $scratch
dotnet test --project plugins/UnrealGvas/SaveToolkit.Plugins.UnrealGvas.Tests --results-directory "$scratch/results-final" --report-xunit-trx --report-xunit-trx-filename results.trx
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

## Public sources and licence decisions

All sources accessed **2026-10-29**. Downloaded source was read as untrusted text
and never executed. Original implementation/observations are GPL-3.0-or-later;
no code, publisher prose, datamined talent dataset or new assets were copied.
There is no new licence-based refusal; missing evidence prevents writes.
Sources informing field captions are repeated in `HogwartsDiscoveryFields`,
`Info.References`, and each new field's description.

| ID | Source, author, revision and URL | Licence / use |
| --- | --- | --- |
| R1 | HLSGE contributors / falker47, [`saveGameDB.ts`](https://github.com/falker47/HogwartsLegacy-SaveEditor/blob/349c3f0b75e4d158e5b7d7920bded999b518de36/HLSE-src/src/client/resources/saveGameDB.ts), [`playerEdits.ts`](https://github.com/falker47/HogwartsLegacy-SaveEditor/blob/349c3f0b75e4d158e5b7d7920bded999b518de36/HLSE-src/src/client/resources/playerEdits.ts), [`collectionsPage.vue`](https://github.com/falker47/HogwartsLegacy-SaveEditor/blob/349c3f0b75e4d158e5b7d7920bded999b518de36/HLSE-src/src/client/pages/collectionsPage.vue), revision `349c3f0b75e4d158e5b7d7920bded999b518de36` | Embedded HLSGE package declares UNLICENSED, separate from manager MIT; see [upstream notices](https://github.com/falker47/HogwartsLegacy-SaveEditor/blob/349c3f0b75e4d158e5b7d7920bded999b518de36/THIRD_PARTY_NOTICES.md). No licence published; reused with attribution per owner decision 2026-10-28. Independently implemented storage captions/read-only guards; no code port. |
| R2 | Malin001 / Legilimens contributors, [`legilimens.py`](https://github.com/Malin001/Legilimens-Hogwarts-Legacy-Collectible-Finder/blob/02525dde0fd9bc0be72982478680377dccb41246/legilimens.py), revision `02525dde0fd9bc0be72982478680377dccb41246` | [MIT](https://github.com/Malin001/Legilimens-Hogwarts-Legacy-Collectible-Finder/blob/02525dde0fd9bc0be72982478680377dccb41246/LICENSE). Page-type/table predicates researched; existing attributed 699-record pack unchanged. |
| R3 | producedbytmsh, [`README`](https://github.com/producedbytmsh/hl-savetools/tree/e568dedcda2611f54cc793e6d0237c749ae25094), [`docs/schema.md`](https://github.com/producedbytmsh/hl-savetools/blob/e568dedcda2611f54cc793e6d0237c749ae25094/docs/schema.md), revision `e568dedcda2611f54cc793e6d0237c749ae25094` | [MIT](https://github.com/producedbytmsh/hl-savetools/blob/e568dedcda2611f54cc793e6d0237c749ae25094/LICENSE). Structure/dependency leads only; sample columns independently verified (`Level`, not notes' `Rank`). PC mirror/state/reward claims not promoted to PS5 guarantees. |
| R4 | jianastrero, [`README`](https://github.com/jianastrero/hogwarts-legacy-save-editor/blob/4100ac5c0972fda15809a41cfe48812c38fb6f12/README.md), revision `4100ac5c0972fda15809a41cfe48812c38fb6f12` | [GPL-3.0](https://github.com/jianastrero/hogwarts-legacy-save-editor/blob/4100ac5c0972fda15809a41cfe48812c38fb6f12/LICENSE). Talent-point editor surveyed; collections/achievements are listed as planned. No code copied. |
| R5 | GamesRef.com, [talent datamine](https://hogwartslegacy.gamesref.com/talents), [Confringo Mastery](https://hogwartslegacy.gamesref.com/talents/confringo-mastery), [Ancient Magic challenge talent](https://hogwartslegacy.gamesref.com/talents/ancient-magic-mastery-skill-amupgrade1), unpinned retrieval 2026-10-29 | Rights reserved by GamesRef.com / Warner Bros. Games / Portkey Games; used with attribution per owner decision 2026-10-29. Research lead for distinct talent/reward definitions; no text/data imported and no saved-ID join claimed. |

## Suggested EDIT_COVERAGE.md text (maintainer handoff)

UG03: **R / Inferred** — talent points located at
`MiscDataDynamic(Player0, PerkPoints).DataValue` (text); talents at
`PerkDynamic(PerkID, Level)`; collections at `CollectionDynamic`, page flags at
`MiscDataDynamic(PageCollected*)`, map state and FGC challenge rows exposed
read-only. Fifteen saves / 30 images pinned. Collection tuples conflict and images
diverge; actor/effect, quest, reward and achievement dependencies remain unproven.
Talent/collection grants stay outside the shortlist until bounds, dependency
writers and controlled PS5 captures exist. Generic PerkPoints/PageCollected
DataValue writes are disabled. Link this report; leave map/quest scope otherwise intact.
