# Owner actions

Prioritised owner-only queue for [#40](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40),
checked **2026-10-29** against the current plugin READMEs, their owner checklists,
[#31 and its comments](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31),
and the open issues (only #31 and #40 at this check). Agents can analyse captures and implement
follow-ups; they cannot supply your console observations, choose repository policy or grant upstream rights.
Implemented writes stay enabled with warnings and Experimental maturity. Captures do not by themselves
implement the missing structural/dependency writers. Current plugin READMEs supersede older handoffs,
including older Yōtei read-only and BG3 work-in-progress descriptions.

Work down the sections; within a game session, start with currency/settings or one existing stack,
then the remaining scalar families, and finally dependency-sensitive or structural trials. Each
semicolon-separated experiment below means a **separate restored copy or single-event capture**,
not several changes in one trial. Collect the matching section 2 pairs during the same play session.

For **every console checkbox**: use a disposable spare slot; keep an untouched recovery dump and all
companions/backups. Test an unchanged toolkit export first and capture a no-action native save/reload/save
control. For each small edit, load → inspect the intended effect and unrelated state → perform ordinary gameplay
action → native-save → exit/reload → re-export and diff. Take a second clean reload where the source
requires it. For supported planned actions, review direct/derived changes, skips and warnings; check
one-step undo/redo, export/re-import, selected/document/all-matching scopes and an exclusion **only where
supported**. Report rejection, clamping, regeneration and side effects as well as persistence.
See the [console/capture protocol](OWNER_CHECKLIST.md#capture-protocol-f1) and
[export instructions](USER_GUIDE.md#export-your-save).

For **every capture checkbox**: one event per before/after pair, plus the no-action control; use
`<TITLE>_<event>_before.zip` / `_after.zip`, record hashes, exact title/build/region/DLC/mods/locale,
mode/NG+, slot, file roles, action and displayed values. Retain full dumps privately outside git;
post the non-sensitive observations, field/action IDs, toolkit version, expected/actual result and
diff summary on [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31) **and its linked plugin
issue**. Do not post save payloads, account identifiers or secrets. An identical backup or a comparison
of unrelated slots is not a control. An agent must check independent pairs before upgrading a mapping;
one successful edit does not verify every field, cap or build. See
[capture details](SAMPLES.md#capture-protocol-controlled-beforeafter-pairs),
[reporting](USER_GUIDE.md#report-a-problem) and [confidence meanings](USER_GUIDE.md#confidence-labels-and-warnings).

## 1. Console validation queue

### Edit-coverage workflows, in shortlist order

Follow [EDIT_COVERAGE's value/risk ranking](EDIT_COVERAGE.md#ranked-top-15-additions), ranks 1–15,
before the broader family checks below. Each checkbox uses the shared backup/control/evidence cycle
above and the step-by-step trial in [the title checklist](OWNER_CHECKLIST.md#title-checklists).
Restore-imported targets are this workspace's opening values: stage a reduction after import, then
preview restoration in that same workspace. Re-importing an edited export establishes a new baseline.
Review actual scope/targets before Apply; cancel unexpected rows and report the problem on #31.

- [ ] **Rank 1 — FF7 current HP/MP recovery** (`character/recover-current-vitals`). Select character
  rows, preview stored-max recovery, apply/export; check damage/heal/MP use, battle and persistence.
  Repeat Remake main, INTERmission, Rebirth retail/demo separately.
- [ ] **Rank 2 — Gearbox gear matching** (`items/match-level`). Select owned gear, preview this
  character's level clipped to title policy, apply/export; compare cards, legality and equip/use in BL3/TTWL.
- [ ] **Rank 3 — Gearbox ammo restoration** (`ammo/restore-imported`). Preview opening targets after
  staged reductions in Progression's document control, apply/export; capture firing/reload/native refill,
  reserve displays and SDU capacity/clamping separately for both titles.
- [ ] **Rank 4 — FF16 named Gil/AP/Renown balances** (`set-gil`, `set-ap`, `set-renown`). Preview/apply/
  export each resource independently; capture exact gain/spend, caps and adult/child/stage context.
  Check AP rank/refund and Renown claim separation; repeat supported demo trials independently.
- [ ] **Rank 5 — Tales consumable restoration** (`inventory/restore-imported`). Select existing IDs
  1–13, preview opening targets within 0–15, apply/export; capture buy/use/zero/reacquisition and consume
  after restoration. The documented UI selection/exclusion defect requires checking every preview target;
  cancel broadened scope and report/retest after the fix.
- [ ] **Rank 6 — Atomic Heart Amount restoration** (`inventory/restore-imported-amounts`). Select
  populated rows, preview/apply/export staged reductions; capture resource/ammo/heal use and native refill,
  stored-Kraken acceptance, output growth/slot capacity and inner-integrity/normalization results.
- [ ] **Rank 7 — Tsushima ammo restoration** (`inventory/restore-imported-ammo`). Use selected-row
  control, preview both copies/checksum, apply/export; capture fire/pickup/reload across ammo kinds,
  capacity/clamping and native persistence. Verify unselected/excluded rows stay unchanged.
- [ ] **Rank 8 — FF7 level batches** (`character/set-level`). Select characters, preview levels and
  derived XP, apply/export; verify exact paired undo and battle/abilities/growth/rewards. Capture one
  native level-up separately for Remake main, INTERmission, Rebirth retail/demo.
- [ ] **Rank 9 — Cyberpunk point budgets** (`progression/set-point-targets`). Preview explicit
  document-level attribute/perk/relic targets, apply/export; capture earn/spend per type, menus and
  actual spending, independent budgets, opaque integrity and native persistence.
- [ ] **Rank 10 — Gearbox marks** (`items/mark`). Select items, preview raw old/new flags, apply/export;
  capture ordinary/favourite/junk transitions and sale protection separately in BL3/TTWL. Check skipped
  conflicting/unsupported records, preserved unknown bits/serials and native persistence.
- [ ] **Rank 11 — Witcher ordinary Gwent counts** (`gwent/set-ordinary-copies`). Select saved ordinary
  rows, preview a bounded target, apply/export; capture copy gain/removal and collection/deck/match,
  quest/reward/achievement behavior, including deck invalidation after decreases.
- [ ] **Rank 12 — FF16 ordinary stack restoration** (`restore-imported-main-stacks`). Select lower
  positive eligible main stacks, preview skips/CRC, apply/export; capture consumable/material use/sell,
  bank authority, pouch/crafting dependencies and native persistence, retail/demo independently.
- [ ] **Rank 13 — ASTRO ownership restoration** (`collectibles/restore-imported`). Select existing
  IsGot rows cleared after opening, preview/apply/export; capture pickup and independent reversal,
  museum/puzzle/notification/cinematic/reward effects and forward play, with two clean reloads.
- [ ] **Rank 14 — Pragmata quantity restoration** (`inventory/restore-imported-quantities`). Identify
  a stack, select bound rows or bounded resolved query results, preview opening targets/raw IDs/skips,
  apply/export; check use, equipped/unlock/dependency effects, siblings and RSA rejection/acceptance.
- [ ] **Rank 15 — FF7 stored play time** (`game/playtime`). Commit Overview's bounded seconds field,
  review Changes against original, undo/redo/export; no action Preview/Apply is provided. Capture timed
  no-action/display pairs and native resave normalization per variant; other counters are not synchronized.

Report every rank's expected/actual effect, private archive hashes, preview/history/scope results and
native save/reload/re-export diff on [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).
Passing local tests/merging implementation does not establish console acceptance or Stable maturity.

### Other enabled-write checks

- [ ] **GenericJson — settings/profile session.** Cyberpunk v136: test bool/number/text, recorded
  choice+index, dynamic text+index, single/default and selected/all-default actions; compare native
  reset, float steps and restart persistence. It Takes Two: present flags/casing, Cody/May counters
  and zeroing, individual chapter/checkpoint strings and the three-field tuple, selected/full flag
  presets; check Continue, Chapter Select, replay, forward play and rewards. Ridge Racer 2: wrapper
  primitives and imported resets; check filter/upscale/AA/controller/rewind and unchanged DATA.BIN.
  Generic scalar/type/array/object structure/reset operations also need title-specific acceptance;
  syntax validity alone proves none. [Write scope](../plugins/GenericJson/README.md#enabled-writes-and-their-evidence),
  [exact trials](../plugins/GenericJson/OWNER_CHECKLIST.md).
- [ ] **AstroPlayroom — coins, then museum session.** Test coins/set-coins, PlayCount and individual
  ownership/cinematic/Progress/World flags separately. Grant one artifact, puzzle and prop; test
  selected/full/query collect-existing and Memory Meadow collect-world with an exclusion. Inspect
  museum/puzzle/prize membership, notifications, cinematics, rewards and UDS state. Test gatcha top-up
  at 1 and 17 attempts, including a higher starting balance; check actual roll cost/caps. Re-export
  primary+backup and reload twice. [Exact trials](../plugins/AstroPlayroom/README.md#remaining-phases-and-exact-owner-checklist),
  [all writes](../plugins/AstroPlayroom/README.md#enabled-writes-and-their-evidence).
- [ ] **TalesOfArise — shop and battle session (PPSA02745 base game).** Test gald, level/HP/SP in each
  nonzero-level status block (active membership is unproven), recognised consumables and the quantity
  batch. Buy/use/acquire
  normally after each edit; test zero then later acquisition, normal 15 versus effective 99 stacks,
  artifact/DLC effects, crafting and forward progression. Check preserved load-screen preview versus
  actual gameplay. [Trials and coverage](../plugins/TalesOfArise/OWNER_CHECKLIST.md#console-qualification-and-schema-coverage),
  [stack checks](../plugins/TalesOfArise/OWNER_CHECKLIST.md#catalog-to-save-joins-equipment-and-capacity).
- [ ] **SuckerPunch — two separate title sessions.** Tsushima: resource/ammo edits and both quantity
  actions; consume/buy/fire, verify paired ammo and capacity. On v48/v49 change time by one minute;
  inspect per-difficulty counters and unchanged `param.sfo SUBTITLE` after resave. Yōtei: validate all
  14 eligible types (Mon/Flowers, seven ingredients, five ammo types), starting with separate +1
  trials, then a multi-row F6 target; check labels/caps, ammo mirrors and all siblings. Yōtei query
  scope is unsupported; checksum tail coverage is unknown.
  [Writes](../plugins/SuckerPunch/README.md#enabled-writes-and-their-evidence),
  [Tsushima trials](../plugins/SuckerPunch/README.md#remaining-work-and-owner-captures),
  [Yōtei trials](../plugins/SuckerPunch/YOTEI_FORMAT.md#exact-owner-captures-for-31).
- [ ] **Insomniac — bolt shop, options, then preferences session.** Independently test CareerBolts +1,
  SpentBolts by one with nonnegative balance, and F6 calculated-balance target (small decrease,
  increase and zero); inspect HUD/purchase accounting. Test each already-unlocked gold-bolt switch
  (RPG, infinite health/ammo, golden weapons, mirror, home-run, confetti) without changing unlocks.
  Independently validate all exposed scalar boolean preferences (61 observed keys) against menus.
  [Exact acceptance experiments](../plugins/Insomniac/OWNER-CAPTURES.md#acceptance-experiments-for-the-enabled-writes).
- [ ] **EldenRing — grace/menu and ordinary-stack session.** Test held runes and held-only F6;
  Unicode/shortened/exactly-16-UTF-16-unit names in both live/menu mirrors; existing positive Rowa
  Fruit (1–12) and Smithing Stone [1] (1–3) in held and nonempty storage; filtered/selected refill.
  Test manual history mirrors within max(imported,4427), held ≤ history, then linked rune/history
  increase above prior history within its bound. Check both copies, unrelated equipment and no repeated
  history accrual on resave. These observed ceilings are not legal caps.
  [Exact trials](../plugins/EldenRing/Evidence/OWNER_CAPTURES.md#console-acceptance-eldenring-r1).
- [ ] **Cyberpunk2077 — gameplay session, sharing settings captures with GenericJson.** Independently
  test money, five attributes/mirrors, three point budgets, supported stacks, Level/Cred, each XP and
  stored threshold. Spend/earn normally; check rewards/unlocks and retained/recomputed thresholds.
  Test one known noncritical existing fact with related callbacks/stages and forward play. Test
  set-target/refill, skipped money/unsupported rows, a stack above opening quantity, and 2.x healing
  charge/replenishment separately from ammo/components.
  [Exact trials](../plugins/Cyberpunk2077/EVIDENCE.md#owner-validation-for-enabled-writes-31),
  [complete write scope](../plugins/Cyberpunk2077/README.md#enabled-writes-and-their-evidence).
- [ ] **FinalFantasy7 — repeat separately for Remake main, INTERmission, Rebirth retail and demo.**
  Test gil, ordinary/mapped non-equipment counts (including materials/tokens/keys/cards/boosters),
  HP/MP in battle, coupled XP/level, ordinary AP below/at/above a tier and direct stars up/down
  on equipped/unequipped copies. Test AP review, restock and selected-star presets. Rebirth only: qualified
  owned-weapon reference, checking sockets/capacity/proficiency/folios/combat. Last, test individual
  progress bits on disposable copies (Remake dresses/intel/VR; Rebirth hard chapters, Queen's Blood,
  Intel and Chocobo stops); inspect actors, prerequisites, rewards, chapter reset and reversal.
  Ordinary AP rules do not apply to summons.
  [Writes](../plugins/FinalFantasy7/README.md#enabled-writes-and-their-evidence),
  [trial protocol](../plugins/FinalFantasy7/README.md#exact-owner-capture-and-validation-checklist).
- [ ] **FinalFantasy16 — shop, adult/child battle, then context trials; retail/demo separately.**
  Test main stack/ID, secondary stack/nonzero ID (including ID-zero quantities), each equipment bank,
  rank bytes/new/protect bits, adult/child level and XP independently, HP/recovery/LB, summon
  index/flags/IDs, commands, shortcuts, party scene/map/ID/member count and time. Check ownership,
  AP, stats, active banks, battle/use and stage transitions. Test two-row quantity target 1 and
  filtered/all-matching exclusion, document/empty scopes. Demo LB's constant bound has no alternative
  value to test. [All families](../plugins/FinalFantasy16/README.md#enabled-writes-and-their-evidence),
  [exact 14-part checklist](../plugins/FinalFantasy16/README.md#owner-capture-and-console-validation-checklist).
- [ ] **Witcher3 — merchant, combat and Gwent session.** Test retained level/XP/free points/Crowns/
  quantities; damaged-item durability and two-item repair; non-core ranks up/down with points,
  prerequisites, equipped slots/modifiers and an area transition. Test ordinary Gwent copies and leader
  removal/restoration against collection/deck/match/quest rewards. Lower then refill eligible unslotted,
  unequipped Bread/Bottled water, including two stacks and exclusions; check consumption and unrelated
  money/rewards. [Writes](../plugins/Witcher3/README.md#enabled-writes-and-their-evidence),
  [acceptance checklist](../plugins/Witcher3/EVIDENCE.md#write-enable-round--version-050).
- [ ] **Gearbox — repeat for BL3 and Wonderlands, with matched character/profile sets.** Test name,
  level/XP/recorded-level stat, points/tree grade, currencies/profile keys and ammo; then item level,
  rerolls, Chaos tier, marks, SDUs, difficulty/unlocked ceiling, regular and generic/anointment parts
  and bulk levels. Inspect cards, stats, sale protection, caps, loot and milestones. Test legitimate
  serial import, duplicate/remove (including equipment indices), bank moves, lost-loot and field batches.
  Finally test each present Guardian rank/XP/token/reward/perk/enabled field and TTWL Hero/Myth
  XP/allocation/last-tree field in F7; inspect every character's budgets, caps, rotation and cloud/mirror
  precedence after reopening. [All writes](../plugins/Gearbox/README.md#enabled-writes-and-their-evidence),
  [trials](../plugins/Gearbox/README.md#owner-capture-and-validation-checklist).
- [ ] **XorJson — supported offline hero and settings session.** Test settings scalars, existing
  FCharacterSaveV1 scalars (including achievement/count/collection fields), F5 object/array
  add/remove/replace, scalar batches and stored-attribute/stack/item-ID/rarity actions separately.
  Check merchant skips, equipped slots, effects/enchantments, power/XP/rewards and world side effects.
  Test character+GlobalSaveDataDefault F7 with both members restored together, whole-group undo/export
  and reload; note rejection/clamping/repair. [All writes](../plugins/XorJson/README.md#enabled-writes-and-their-evidence),
  [exact trials](../plugins/XorJson/README.md#owner-capture-and-console-checklist).
- [ ] **Pragmata — inventory, then one trial per family tab.** Test quantity/F6 and one F4 existing-ID
  replacement; individually test Gadgets, Loadout, Progress, Quests, World and retained header/clock/raw
  primitives. Check equip/use, ownership/unlocks, prerequisites/downstream progression and all siblings.
  On a real inventory >200 rows, test all-matching quantities with one exclusion. RSA trailer
  verification/regeneration remains absent; record game rejection despite repaired hashes.
  [Writes](../plugins/Pragmata/README.md#enabled-writes-and-their-evidence),
  [trials](../plugins/Pragmata/evidence/README.md#scope-and-owner-checklist).
- [ ] **AtomicHeart — inventory/NORA session.** Test small populated Amount and zero, separate
  same-category weapon and ammo replacements using existing full paths, selected/full/query-excluded
  quantity batches. Check IDs, modules, ownership, loaded/reserve ammo, use/spend/travel and excluded
  rows. Record stored-Kraken growth, actual PS5 slot capacity, restart/reload and unrelated decoded spans;
  outer SHA-1 repair does not settle inner integrity.
  [Exact trials](../plugins/AtomicHeart/README.md#owner-capture-and-validation-checklist).
- [ ] **BaldursGate3 — metadata first, then identified live-owner trials.** Test SaveInfo level 1→2,
  current XP 30→29, total XP 30→29, LeaderName and a multi-row metadata batch; compare save menu with
  unchanged live sheet. Test intended raw LSF primitive edits separately; direct LSF quantity needs
  a new positive-quantity sample (none in the current 761 Item nodes). Identify an ordinary NewAge
  stack/owner, lower its member quantity, repeat multi-contribution stacks and use/trade/rest. Test
  current/temp HP and each of six effective ability-cache candidates separately against sheet/combat/
  boosts/rest/regeneration. Swap two unequipped existing container keys and restore imported quantities
  with supported scopes; check grid/order, equipment and unrelated owners. These are not base-stat,
  respec or player-gold writers. [All writes](../plugins/BaldursGate3/README.md#enabled-writes-and-their-evidence),
  [latest trials](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31#issuecomment-6482483373).
- [ ] **GodOfWarRagnarok — wallet, skill and Raven Tree session.** Independently reduce Hacksilver,
  resource, raw EconomyXP, each raven counter, selected unknown-ID and positive non-hero counters by
  one; inspect all characters, XP/skills, journal/reward/chest states and normal use/gain. Reduce time
  one second. Test selected/query-excluded/document restore with two counters; verify 0..imported
  ceilings and reopening behavior. Unknown authentication remains a rejection risk; use a second clean
  reload. [Exact trials](../plugins/GodOfWarRagnarok/OWNER_CHECKLIST.md#enabled-write-trials).
- [ ] **UnrealGvas — separate Jedi, Sackboy and Hogwarts sessions.** Test supported existing Jedi
  gameplay/settings and Sackboy General scalars, leaving checksums/actor lengths unchanged; observe
  effects and native rewrites. Hogwarts: unchanged reconstruction first, then eligible integer/real/text
  cells in each decoded image; check actual row role, dependencies and stored-output slot allocation
  (HL0010 research output was 8.79 MB; 16 MiB editor budget is not proven capacity). Generic writable
  GVAS primitives and scalar-container remove-last also need title-specific relationship/reload tests;
  wrapped Hogwarts files remain read-only. [All writes](../plugins/UnrealGvas/README.md#enabled-writes-and-their-evidence),
  [Jedi/Sackboy trials](../plugins/UnrealGvas/ROUND2.md#exact-owner-capture--validation-checklist),
  [Hogwarts trials](../plugins/UnrealGvas/HOGWARTS.md#write-investigation-and-owner-checklist).

## 2. Captures needed (#31)

These are the mapping/limit/dependency captures to collect alongside section 1, then broader coverage.
Keep each action separate, including the reverse action. Follow the linked full lists for exact fields
and lower-priority variants; these summaries do not convert reference names into Verified gameplay facts.

- [ ] **Yōtei — deferred checksum-coverage evidence (outside the 15 ranks).** On a backed-up spare
  slot, capture manual/auto/memory roles and an unchanged load/save control with all gameplay/memory/SFO
  files. Supply naturally nonzero trailing data or a disposable console checksum/load/resave experiment
  distinguishing declared-end from whole-file coverage; record acceptance/rejection and native
  resave/reload/re-export diffs on #31. Eleven zero-tail samples, synthetic tails and same-candidate
  reparsing cannot settle the game's coverage. Restore-imported remains deferred; existing quantity
  writers retain warnings. [Exact capture 7](../plugins/SuckerPunch/YOTEI_FORMAT.md#exact-owner-captures-for-31),
  [#40 deferral](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6489232331).
- [ ] **Tales CP — authority/refill/growth evidence before writes.** On backed-up spare copies, capture
  labelled no-action resaves, one CP spend, one rest/refill and one level-up in separate pairs; keep
  Orange Gel and Astral Flower/other bonus changes separate. Record menu current/max CP, active party,
  levels, HP and consumables alongside read-only `party/current-cp` / `party/max-cp`, then native-save/
  reload/re-export and compare with the control. Report authority/maximum changes and private archive
  hashes/diffs on #31; equal sample values and backup twins prove no growth rule. CP writes/restoration
  remain unavailable. [CP evidence and capture request](../plugins/TalesOfArise/README.md#party-cp-field-discovery-40-2026-10-29),
  [#40 handoff](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6489330493).

- [ ] **GenericJson:** Cyberpunk static enum, scalar and native reset, dynamic language/HDR choices,
  then a second settings version; show value/index authority, actual steps and companion coupling.
  It Takes Two: fresh profile → discover → play → Cody win/score → May win/score, chapter entry and
  checkpoint separately; incomplete/complete profiles, exact display aliases and missing-key behavior.
  Ridge Racer: each filter/upscale/AA/controller/rewind option with wrapper version and DATA.BIN.
  Establish required members/defaults/limits and actual shared-file invariants before future insertion/
  groups. [Capture list](../plugins/GenericJson/OWNER_CHECKLIST.md).
- [ ] **AstroPlayroom:** coin gain, 100-coin spend/cap, native PlayCount event; one artifact, prop prize,
  stage clear and boss clear. Puzzle pairs at Renderforest/Teraflop Treetops, Raytrace Ruins/Mt.
  Motherboard, Bot Beach/Springy Spa and Gusty Gateway/Fastlane Fields must name the physical piece.
  Record IsGot/cinematic/notification/UDS and catalogue counts; independently confirm ds3 and other
  aliases/18 props. Later: old/new update, eligibility, each of four bots, added artifacts and both
  games around transfer. [Exact pairs](../plugins/AstroPlayroom/README.md#remaining-phases-and-exact-owner-checklist).
- [ ] **TalesOfArise:** buy/consume Apple Gel, damage/heal, SP gain/node buy, level/herb, recruit/party
  order, CP heal/rest/gel/flower; ingredient/material/herb/fishing/key pickups and zero/reacquisition/
  15-versus-99 boundaries. Equip/unequip per character; duplicate accessories with different ranks,
  enhance/transfer ability and empty/full pools. Arte use/proficiency threshold, title unlock, paid/
  final node and DLC learning; owl find/report/reward, quest objective/stage/completion/reward,
  recipe/first/repeat cook, new/repeat fish/reward, camp/map unlock, artifact toggle and NG+ carry-over.
  [Exact pairs and build coverage](../plugins/TalesOfArise/OWNER_CHECKLIST.md).
- [ ] **SuckerPunch:** Tsushima +1 Supplies/flower, separate Hide/iron/yew/wax sales, arrow fire/pickup,
  pouch/quiver upgrade; owned armor/sword/charm/loadout, duplicate charm, dye and upgrade. Technique
  earn/spend/prerequisite, stance/tale/rank, damage/heal; discovery/completion/reward separately for
  shrine/fox den/spring/haiku/bamboo/pillar/quest; timer versus subtitle and difficulty counters.
  Yōtei: each raw ingredient/ammo pickup and capacity; armor/weapon/mask/charm equip, acquisition/dye/
  upgrade, technique earn/spend/prerequisite; Horizon View discovery/use/health reward, shrine and
  quest stage; idle 60 seconds, damage/heal/difficulty, manual/auto/memory roles and nonzero-tail
  checksum evidence. [Tsushima pairs](../plugins/SuckerPunch/README.md#remaining-work-and-owner-captures),
  [Yōtei pairs](../plugins/SuckerPunch/YOTEI_FORMAT.md#exact-owner-captures-for-31).
- [ ] **Insomniac:** normal/Challenge no-action controls; bolt earn/purchase and legal option/preferences
  toggles. Weapon acquisition, XP increment/level, shot/refill/death-checkpoint, raritanium earn and
  individual non-gold/second-row/gold node buys; Omega purchase separately from Challenge creation.
  Armor head/torso/legs acquisition versus each character's equip/unequip/color/entitlement; each spybot
  (Scarstu's two separately), gold bolt/hash, Lorb/Zurpstone/bear, pocket/arena reward. Record exact
  IDs/grid/cost/prerequisites; supply three non-collinear landmarks for a physical map transform and
  optional matching localization/config data. [Exact pairs](../plugins/Insomniac/OWNER-CAPTURES.md).
- [ ] **EldenRing:** dagger buy/use/final use, Rowa storage in/out, talisman/vital modifier, duplicate
  weapons, hand/quick/pouch/spell selectors, capacity unlocks, normal/somber upgrade and highest-ever
  history, affinity/Ash changes, arrows/bolts and pot/perfume crafting. Capture Unicode/short/max names,
  separate Vigor/Mind/Endurance levels/respec/class minima, earn/spend/death/recover/lost runes.
  Verify Church of Elleh (76100)/First Step (76101), one grace/boss/fragment, map-layer-only, pool,
  collectible, quest/NG+/DLC blessing; DLC grace 76800 and Bell Bearing acquire/offer/new journey
  separately. Include nonempty/full storage and regulation/build/region-matched references/caps.
  [Exact pairs](../plugins/EldenRing/Evidence/OWNER_CAPTURES.md).
- [ ] **Cyberpunk2077:** ammo/component buy/use, stash transfer, duplicate-instance equip/unequip
  (including RightArm special slots), upgrade and attachment add/remove. Ordinary/Relic perk buy/
  refund and PL access, implant install/remove versus capacity perk/shard; mature base/PL saves.
  Car acquire/summon/despawn/favorite; dataterm discover/use/quest lock; precise quest/gig/collectible
  and tracking-only pairs; Level/Cred/skill thresholds with rewards. Return a natural pair for the
  known fact tested above; later schemas only if actually obtained. Settings pairs are shared with
  GenericJson. [Exact pairs](../plugins/Cyberpunk2077/EVIDENCE.md#exact-round-2-additions).
- [ ] **FinalFantasy7:** for each variant, XP/level with stats and guest identities; ordinary AP below/
  across tier versus Rebirth summon tier with unchanged AP. Separate weapon/armor/accessory/socket
  swaps, linked/summon slots and swap-back, owners/proficiency/folios. Card acquire/booster/deck/opponent
  (identify 012/013 labels), Intel objective, stop discovery/repair/reward, folio/craft/outfit/minigame,
  chapter-select/clear/quest reset; retain counts, actors and rewards separately.
  [Exact pairs](../plugins/FinalFantasy7/README.md#exact-owner-capture-and-validation-checklist).
- [ ] **FinalFantasy16:** potion buy/use/material sale/zero-stack reacquire and each pouch upgrade;
  gear swaps identifying all banks in adult/boyhood/replay; learn/upgrade/master/reset with exact AP
  and Eikon unlock; XP without level then one level in adult/child/normal/FF modes. Independently name
  former unnamed IDs, potency/capacity 506–509 and skill IDs 39/45/49 versus saved indices/flags.
  One ability per Eikon (including Leviathan/Ultima/demo), obelisk/collectible, first/new/last/reacquired
  allocation and slot reuse/full boundary. Later demo LB needs a nonzero capture. Supply matched PC
  binary/XML or build/locale growth/command data if available.
  [Exact pairs](../plugins/FinalFantasy16/README.md#owner-capture-and-console-validation-checklist).
- [ ] **Witcher3:** Bread buy/use/drop/pickup and cap; blacksmith repair with durability/Crowns;
  stash/Roach/combat equip/unequip; XP-level, non-core skill purchase/equip, card buy/win/deck, recipe
  and signpost separately. Add mutation research/activation/respec, signpost travel, Place-of-Power
  visit/use and site disable/completion pairs before interpreting graph/map candidates. Include item
  add/delete/split/merge, exact quest branches/cutoffs and later world revisits. Supply two refill-eligible stacks and
  mid/endgame, HoS/B&W, NG+, Ciri, large-stash and socketed/enchanted variants.
  [Core pairs](../plugins/Witcher3/EVIDENCE.md#remaining-implementation-and-owner-checklist),
  [completion follow-up](../plugins/Witcher3/COMPLETION.md).
- [ ] **Gearbox (BL3/TTWL):** pickup/sale/equip/mark/reroll; first/middle/last removal; bank both
  directions/lost-loot with duplicates and empty/near-full/full destinations. Level/skill/respec/SDU/
  refill separately; named passive in each class/tree/tier, including BL3 DLC tree. Guardian token/perk
  plus sequential no-action loads of every character to determine mirror precedence. TTWL level-40
  Myth each tree, rotation and capped boundary; endgame Chaos/rerolls/serial variants and exact cards/
  parts/hotfixes. Include character/profile/all backups together, with explicit gameplay matching.
  [Exact pairs](../plugins/Gearbox/README.md#owner-capture-and-validation-checklist).
- [ ] **XorJson:** new/progressed offline hero plus SerializeMeta version/hash; currency gain/spend,
  XP/level, pickup/equip, rarity/effects, quest partial/completion/reward, map/station and master-volume
  pairs. Include talisman, nonempty enchantments, merchant/bag/equipped versions of the same base item,
  TypeTags/rarity/power/stack/seed/slot overrides and duplicate/reorder seed persistence. Capture both
  character/global files around active-character and shared-progression changes; note cloud-sync mode
  and all achievement/door/fog/actor side effects. [Exact pairs](../plugins/XorJson/README.md#owner-capture-and-console-checklist).
- [ ] **Pragmata:** three independent pairs per family: item pickup/use/purchase; gadget/shortcut
  equip/unequip; weapon purchase/equip; perk upgrade/equip; objective/occurrence completion;
  checkpoint/hatch, chapter and collectible. Record quantities and mission selectability for IDs
  558265447, 1887847758, 1942031930, 3036423139, 3090659029, 3746788365. Supply exact uint32/internal
  name/category/localized extraction with provenance; accepted-versus-clamped boundary pairs,
  empty/full/>200-row inventories and difficulty/cleared/NG+ variants. Unknown saved states need
  these joins even though reference map art exists. [Exact pairs](../plugins/Pragmata/evidence/README.md#scope-and-owner-checklist).
- [ ] **AtomicHeart:** Neuropolymer gain/spend, material pickup/craft, ammo pickup/fire/reload with
  loaded/reserve counts, healing use; backpack↔NORA and empty→populated slot. Skill buy/refund,
  weapon equip/upgrade, blueprint, Chirper, Testing Ground reward and objective separately. Identify
  active region versus saved unvisited levels; supply exact build/locale skill/module/resource IDs,
  names/costs/prerequisites and early/open-world/late/NG+/DLC states.
  [Exact pairs](../plugins/AtomicHeart/README.md#owner-capture-and-validation-checklist).
- [ ] **BaldursGate3:** gold ±1, potion use/HP, split/merge and party/bag/camp transfer with a second
  settled save after each rearrangement. Identify owners/epochs using Act 1–3/camp, origin/Dark Urge/
  hireling/multiplayer, duplicates/rings/dual wield/torch/instrument. Separate damage/heal/temp HP,
  boosting gear, learn/prepare spell, level/respec, quest accept/complete/fail, approval/rest/romance,
  recipe/waypoint. Match StorySave marker control versus exact visible event, and build/language/
  module/load-order template/stats/LOCA evidence; none proves live party/gold by position alone.
  [Exact pairs](../plugins/BaldursGate3/EVIDENCE.md#exact-next-owner-capture-checklist),
  [live-owner follow-up](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31#issuecomment-6482483373).
- [ ] **GodOfWarRagnarok:** two no-action controls; Hacksilver/resource earn/spend, Kratos/Atreus/
  Freya-context XP separately, gear equip/upgrade, exact skill unlock/weapon level/XP, quest objective/
  completion, raven pickup versus Raven Tree chest, artifact/lore and one preference change.
  Include late/Valhalla, additional PS5 regions and matched PS4/PC states if available. Retain all
  non-system files and rejection text; if available, provide authentication call names/ranges/exclusions/
  derivation structure with secrets redacted, never key values.
  [Exact pairs](../plugins/GodOfWarRagnarok/OWNER_CHECKLIST.md#further-format-and-integrity-captures).
- [ ] **UnrealGvas:** Jedi/Sackboy need untouched, no-action resave and second resave (three states).
  Sackboy: exact bells earn/spend, costume/tint/emote, world unlock, orb/prize and new-slot/build
  checksum behavior; photo plus matching image-set/screenshot for texture layout. Jedi: XP, skill,
  chest/stim, cosmetic, meditation and planet/area, preserving primary/Backup00 independently.
  Hogwarts: buy/sell/currency earn/spend/potion/XP-level/talent/gear/collectible pairs retaining both
  images, SaveGameList, companions and house/owner states; identify changed image/rows rather than
  updating both blindly. Supply wrapped/opaque-schema examples and matched IDs only when available.
  [Jedi/Sackboy pairs](../plugins/UnrealGvas/ROUND2.md#exact-owner-capture--validation-checklist),
  [Hogwarts pairs](../plugins/UnrealGvas/HOGWARTS.md#write-investigation-and-owner-checklist).
- [ ] **Gearbox / BL4 — lower-priority research only.** Capture fresh launch/profile, settings-only
  toggle, first active-character save, no-action control, money change and legitimate item pickup
  with every companion. Establish file roles and an owner-local PS5 preprocessing/derivation route
  using nonconfidential invariants; PC Steam/EGS rules do not establish it. PPSA01494 remains excluded,
  with no decoder or writes. [Exact research checklist](../plugins/Gearbox/BL4-feasibility.md#exact-owner-capture-checklist).

## 3. Decisions needed

- [ ] **Set release cadence and the rc→stable acceptance scope.** Choose when to cut the next rc and
  v0.1.0, which known limitations are acceptable, and what console evidence you require before that
  application release. No fixed cadence, required number of rc releases or universal console-test
  threshold is specified in [release policy](RELEASING.md#publish-a-release); this remains an explicit
  owner choice. Application SemVer and plugin maturity are separate.
- [ ] **Approve Stable only for a console-tested scope.** After section 1 results are reviewed,
  record the exact plugin/features/builds accepted and warnings still needed on
  [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31). Until then keep Experimental;
  a stable application tag does not automatically promote every plugin.
  [Human-only maturity rule](PLUGIN_GUIDE.md#hard-rules).
- [ ] **SDK-07 presets:** decide whether persisted toolkit presets are wanted now, their title/build
  compatibility policy and how missing/ambiguous owned instances should be resolved before an agent
  implements storage. Existing pure F6 parameter presets are the documented workaround; storage alone
  cannot make loadout/socket writes safe. [SDK-07](SDK_REQUESTS.md#prioritized-inventory),
  [FF7 request](../plugins/FinalFantasy7/README.md#sdk-requests).
- [ ] **SDK-10 subtitle:** after Tsushima timer/difficulty captures, decide whether `param.sfo SUBTITLE`
  should remain preserved or gain an opt-in, validated output/undo/export ownership contract. Current
  behavior preserves system metadata; automatic synchronization is not approved or evidence-backed.
  [SDK-10](SDK_REQUESTS.md#prioritized-inventory),
  [current timer contract](../plugins/SuckerPunch/README.md#enabled-writes-and-their-evidence).
- [ ] **macOS runner budget:** decide whether to retain the macOS *script-check* CI job given private
  repository minutes. Backend validation currently runs on Ubuntu/Windows; release packaging separately
  uses matching macOS architectures. No budget/cost figure or removal decision was found in #40;
  do not treat this as an existing CI failure or silently drop release coverage.
  [CI scope](RELEASING.md#continuous-integration),
  [macOS addition](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6486630995).
- [ ] **BG3 reference/spoiler scope:** specify desired additional build/language/module coverage and
  spoiler policy when supplying reference data. Existing owner attribution decisions already apply;
  no repeat approval is needed merely for publisher assets or missing community licences.
  [Open coverage/spoiler question](../plugins/BaldursGate3/EVIDENCE.md#exact-next-owner-capture-checklist),
  [current rights policy](LICENCE_EXCLUSIONS.md#owner-policy--2026-10-29).

Ordinary #40 implementation deferrals are not owner-only work: SDK/framework requests remain in
[SDK_REQUESTS.md](SDK_REQUESTS.md#coverage-and-dispositions); the later
[E2E application regressions](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6488518461)
and [field-aware API batching](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6488622173)
need engineering scope, not a console or a new licence grant. Older “deferred” comments may already
be superseded; for example the Ragnarök attribution was
[subsequently corrected](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6488183426).

## 4. External follow-ups

**Live `gh issue view --comments` check: 2026-10-29.** All 17 upstream licence requests below are
**OPEN and unanswered by upstream**; the TGA request has only the owner's +1. Each asks for MIT or
GPL-3.0 terms (TGA's original request is general; the owner suggested those terms). Follow responses
and record any actual licence grant/revision in the [request register](LICENCE_EXCLUSIONS.md#upstream-licence-requests).
These pending requests are not new blockers on reuse already permitted by the owner's policies.

- [ ] Follow [Cyberpunk-Modding-Docs #83](https://github.com/CDPR-Modding-Documentation/Cyberpunk-Modding-Docs/issues/83) — OPEN, no reply.
- [ ] Follow [bg3-savefile-parser #13](https://github.com/DanFinchUK/bg3-savefile-parser/issues/13) — OPEN, no reply.
- [ ] Follow [GingerBread #3](https://github.com/LBPHaxMods/GingerBread/issues/3) — OPEN, no reply.
- [ ] Follow [BetterCombatEvolved #37](https://github.com/Lknechtli/BetterCombatEvolved/issues/37) — OPEN, no reply.
- [ ] Follow [SackboyWorldUnlocker #1](https://github.com/MsKrypt1/SackboyWorldUnlocker/issues/1) — OPEN, no reply.
- [ ] Follow [ffxvi-modding #2](https://github.com/Nenkai/ffxvi-modding/issues/2) — OPEN, no reply.
- [ ] Follow [Serofix bg3-save-editor #1](https://github.com/Serofix-code/bg3-save-editor/issues/1) — OPEN, no reply.
- [ ] Follow [Tales_of_Cheat_Tables #120](https://github.com/Sora3100/Tales_of_Cheat_Tables/issues/120) — OPEN, no reply.
- [ ] Follow [witcher-3-enemy-scaling-and-other-changes #2](https://github.com/aschleg/witcher-3-enemy-scaling-and-other-changes/issues/2) — OPEN, no reply.
- [ ] Follow [GOWTool #14](https://github.com/kainotoa/GOWTool/issues/14) — OPEN, no reply.
- [ ] Follow [ree-save-editor #52](https://github.com/kvasszn/ree-save-editor/issues/52) — OPEN, no reply.
- [ ] Follow [Paramdex #10](https://github.com/soulsmods/Paramdex/issues/10) — OPEN, no reply.
- [ ] Follow [ER-Save-Lib #6](https://github.com/ClayAmore/ER-Save-Lib/issues/6) — OPEN, no reply.
- [ ] Follow [GoW-2018-PS4-editor #2](https://github.com/xcier/GoW-2018-PS4-editor/issues/2) — OPEN, no reply.
- [ ] Follow [Sandsmark borderlands3-save-editor #4](https://github.com/sandsmark/borderlands3-save-editor/issues/4) — OPEN, no reply.
- [ ] Follow [mooofin God-of-War-Ragnarok #2](https://github.com/mooofin/God-of-War-Ragnarok/issues/2) — OPEN, no reply.
- [ ] Follow [Elden-Ring-CT-TGA #158](https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/issues/158) — OPEN, owner +1 only.
- [ ] **If Atomic Heart wiki reuse is still wanted:** obtain compatible terms and accessible material
  for its two NC location/skill datasets; otherwise retain current alternatives. Access also returned
  402/403. [Exact exclusions](../plugins/AtomicHeart/README.md#licence-exclusions).
- [ ] **If BG3 legacy wiki editorial prose is wanted:** obtain compatible permission for the NC prose;
  the attributed game text/art and factual catalogue path do not grant rights to it.
  [Exact exclusion](../plugins/BaldursGate3/README.md#licence-exclusions).
- [ ] **If er-save-manager material is wanted:** obtain permission addressing its explicit distribution
  restriction; keep excluded meanwhile. This is distinct from the open ER-Save-Lib request.
  [Exact exclusion](../plugins/EldenRing/README.md#licence-exclusions).
- [ ] **If witcher3map community data/art is wanted:** obtain compatible permission for its NC terms;
  retain external links meanwhile. Publisher-asset policy does not override that community restriction.
  [Exact exclusion](../plugins/Witcher3/README.md#licence-exclusions).
- [ ] **Arrange independent compressed-Kraken evidence if available:** provide results from a separately
  reviewed decoder for original Atomic Heart chunks and Hogwarts stored output. An independent tool/
  expert may be needed; availability is unconfirmed. Agents can compare results once supplied.
  The existing stored-literal oracle is not independent compressed-corpus agreement.
  [Atomic Heart request](../plugins/AtomicHeart/README.md#owner-capture-and-validation-checklist),
  [Hogwarts request](../plugins/UnrealGvas/HOGWARTS.md#write-investigation-and-owner-checklist).

## 5. Release checklist: v0.1.0-rc.N → v0.1.0

Owner publication steps, in order. Agents may prepare code, test evidence and notes; this checklist
does not authorize a push, tag or workflow dispatch. Promotion means a **new stable release/version**,
not relabelling rc assets or moving an existing tag. [Release policy](RELEASING.md#publish-a-release),
[immutable versions](RELEASING.md#rollback-and-maintenance).

- [ ] **Choose the exact committed revision and acceptance scope.** Resolve the cadence/exit decision
  above, review #31 console outcomes and known limitations, and keep Experimental/confidence warnings
  where evidence remains absent. No documented rule requires all plugins to become Stable first.
  [Version policy](RELEASING.md), [maturity rule](PLUGIN_GUIDE.md#hard-rules).
- [ ] **Review complete validation for that revision.** Require green hosted Ubuntu/Windows validation,
  script checks (including the current macOS job), web TypeScript/tests/build, built-app E2E and
  documentation checks; review private-sample runs separately with their skips disclosed. Use the
  documented Release test commands; sample-free CI cannot establish gameplay acceptance.
  [CI](RELEASING.md#continuous-integration), [E2E](RELEASING.md#built-app-browser-smoke-tests),
  [sample checks](../README.md#tests-and-private-samples).
- [ ] **Review stable release notes and distribution scope.** Prepare optional curated
  `docs/release-notes/v0.1.0.md` at the chosen revision, with absolute links, known limitations and
  honest console status; inspect generated conventional-commit notes and licence notices. Exclude
  private saves. [Notes/publication](RELEASING.md#publish-a-release),
  [package contents](RELEASING.md#build-and-check-bundles-locally).
- [ ] **Publish through one documented entry path.** Confirm `v0.1.0` is unused and full history/tags
  are available. Either push a new `v0.1.0` tag at the chosen commit, or dispatch Release on that
  revision with `version=0.1.0` (no `v`); the manual path creates the remote tag only after package
  checks. Do not move the rc tag or merely uncheck its prerelease flag.
  [Tag/manual paths](RELEASING.md#publish-a-release).
- [ ] **Verify the real hosted release run finishes successfully.** Check validation, native-OS package
  and smoke results for win-x64, linux-x64, osx-arm64 and osx-x64, including `/api/version` reporting
  0.1.0. Local checks cannot establish hosted permissions/runner behavior. If publication fails after
  tag creation, resume failed publication jobs/draft as documented, not a conflicting fresh run.
  [Pipeline/recovery](RELEASING.md#publish-a-release), [hosted verification](RELEASING.md#rollback-and-maintenance).
- [ ] **Inspect the published stable release before announcing.** Confirm four archives, each `.sha256`,
  combined `SHA256SUMS`, expected notes and normal stable status. Verify downloaded checksums and
  extracted launcher/UI/version on the intended platforms; bundles are unsigned/unnotarized and
  require writable supported systems. [Asset verification](RELEASING.md#publish-a-release),
  [download/start checks](USER_GUIDE.md#download-and-verify).
- [ ] **Keep rollback possible.** Preserve the last known-good bundle, untouched saves and old `data/`
  before switching. Withdraw a broken release if necessary, document the fault and publish a corrective
  patch; never overwrite assets or move published tags. [Rollback policy](RELEASING.md#rollback-and-maintenance).
