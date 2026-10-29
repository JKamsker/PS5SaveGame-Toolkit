# Owner capture and console-validation guide

This consolidates the owner checklists posted on [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31)
and each linked plugin issue (#1, #11–#26), checked against the current plugin
READMEs and code on `main` for documentation consolidation (#37), 2026-10-28.

**Supported writes are enabled with warnings, and all plugins remain Experimental.** These checks
validate effects and persistence; they are not another permission gate for implemented writes.
Edits are not console-validated and the game may reject, normalize or overwrite them. Yōtei's existing
quantity writes retain checksum-coverage warnings; BL4 is research-only. Other unimplemented structural
and dependency writers still need engineering after their semantics are established. BG3's completion
round is in progress. Only the owner can promote a tested feature to Stable.

## Capture protocol (F1)

The full [capture protocol and F1/F12 CLI reference](SAMPLES.md#capture-protocol-controlled-beforeafter-pairs)
define archive naming, metadata, decoder discovery and fixture manifests. For every title/build:

1. Keep an untouched recovery export and all companion/backup files. Use a disposable spare slot.
2. Capture a **no-action control**: save/export, load, take no action, save/export in the same place.
   Include restart/reload where requested. Investigate the differences before recording any churn mask;
   a primary/backup twin or self-comparison is not a temporal control.
3. Record title, native build, region, DLC/mods, locale, mode/NG+, slot/character, capture date, one
   exact action and visible before/after values. Keep account identifiers and credentials out of notes.
4. For each event below, take a separate immediate before/after pair. A grouped checklist entry means
   several independent pairs, never one combined gameplay session. Keep archives in the private
   external artifacts directory as `<TITLE>_<event>_before.zip` / `_after.zip`, with SHA-256 hashes.
5. For each enabled write, export unchanged and test recovery first; then change one small value,
   load, check its effect and unrelated state, perform the normal use/buy/equip/progression action,
   native-save, exit, reload, re-export and compare all affected files against the control. Include a
   second clean reload where specified. Record clamping, regeneration and rejection as well as success.
6. For a planned action, review direct/derived values, skips and warnings before apply. Verify one
   undo/redo step, export/re-import, and console persistence. Test explicit rows, document scope and
   supported all-matching F8 queries with an excluded unloaded row; empty/fully excluded scopes must
   change nothing. Apply only scopes that the plugin supports, within its documented bounds.
7. Use `savediff list-decoders` to select a view or native hook. Review bytes, semantic paths, primary/
   backup differences and incomplete-input warnings. Confirm event changes before adding fixture
   expectations or reasoned ignore rules. Missing samples and skipped fixtures are not evidence.

## Highest value first

1. Capture each title/build's no-action control and unchanged toolkit-export/recovery cycle.
2. Validate the simplest **already-enabled** currency, stack or setting edit and its normal next use.
3. Validate the remaining enabled scalar families and reviewed batch scopes listed below.
4. Capture identity/equipment/transfer, XP/skill and collectible/story events separately to explain
   missing joins and dependencies. A reference catalogue or set bit alone proves neither ownership nor completion.
5. Add boundary, duplicate-instance, full-container, later-build, NG+/DLC and regional variants.
6. Supply version/locale-matched reference data with provenance. Under the owner decision of
   2026-10-28, import unlicensed community facts/data with full attribution and reimplement unlicensed
   code from documented behaviour with source credit; no published licence is required for those paths.
   The 2026-10-29 owner decision also permits rights-reserved guide material and publisher art, icons,
   screenshots and game-extracted text with source, rights-holder and revision/date attribution.
   GPL-2.0-only code and NC/no-redistribution terms remain refusals. Follow
   [plugin guide rule 9](PLUGIN_GUIDE.md#hard-rules) and the [licence exclusions register](LICENCE_EXCLUSIONS.md) for current
   status and imports in progress. Seek compatible terms for remaining refusals; matching captures
   and ID joins still need evidence.

## Title checklists

Within each title: **control → enabled-write validation → further mapping/compatibility captures**.
Use the shared console cycle above for every write checkbox. The linked plugin files retain exact
field IDs, bounds and the full gap audit. Research-only entries request captures, not toolkit edits.

The **Rank** checks below cover the [15 edit-coverage workflows](EDIT_COVERAGE.md#implementation-status).
For every check, keep the recovery export/companions and use a spare slot; capture a matching no-action
save/reload/save control before editing. Record title/build/region/DLC/locale/mode/slot, toolkit version,
action/field ID, selected and excluded raw row IDs, opening/current/target values, preview changes/skips/
warnings, undo/redo and export/re-import results. After the stated gameplay observation, native-save,
exit/reload, re-export and diff all affected files against the control. Record screenshots, private
archive names/SHA-256 hashes and the diff report path, including rejection, clamping, regeneration and
unrelated effects; report the redacted result on [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31)
and the linked plugin issue. These requirements apply to each rank independently.

For restore-imported checks, open a save with eligible values, then stage a small reduction **in that
same workspace** before previewing restoration. Opening values are immutable for that workspace;
re-importing an edited export creates a new baseline and cannot recover pre-import losses. For table
actions select explicit rows first, then separately test supported document/all-matching scopes with
an excluded unloaded row and empty/fully excluded scopes. Review the dialog's actual scope: a filter
alone is not a selection. Cancel an unexpected scope or target list, record it on #31, and retest after
the fix. Field edits and document-only actions use the specific review steps below instead.

### PPSA18463 — Baldur's Gate 3

Build notes: the supplied PS5 LSMF sample is one independent snapshot without a recorded no-action resave;
build, region and gameplay chronology are unknown. Keep the character/slot/build constant.
[Live-write checklist](../plugins/BaldursGate3/NEWAGE.md) · [broader capture checklist](../plugins/BaldursGate3/EVIDENCE.md) · [issue #1](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/1)

**Current scope:** Completion round in progress. Enabled: supported LSF primitives, SaveInfo metadata and
bounded NewAge live candidates; no authoritative party/gold total.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Validate SaveInfo level, current XP, total XP and LeaderName metadata independently, then a multi-row
      batch; compare save-menu metadata with the live sheet. The supplied Item list has no direct quantity
      attribute: provide an explicit positive LSF quantity fixture before testing that conditional writer.
- [ ] Identify a settled ordinary NewAge stack and owner; lower one supported member quantity,
      use/trade/rest and inspect unrelated owners. Repeat a multi-contribution member and immediate versus
      settled rearrangements; no quantity of one or player-gold identity is invented.
- [ ] Validate current and temporary HP candidates separately, including zero/stored maximum; inspect sheet,
      combat, death/status, rest and other characters. Reduce each supported effective ability-cache
      candidate separately, with/without boosting gear; check level-up/respec and regeneration.
- [ ] Swap two unequipped entries within one container; verify grid order, ownership/equipment/quantities
      and undo/redo. Include non-sequential CSD ordinals. Lower two quantities and test imported-quantity
      restoration with explicit/query/excluded/document scopes.
- [ ] Gain exactly one gold; record the acting character, exact stack and all party gold totals.
- [ ] Spend exactly one gold in a separate pair.
- [ ] Consume one healing potion from a settled known stack; record count and HP.
- [ ] Split one known non-quest stack; take a settled no-action save afterward.
- [ ] Merge that stack in a separate pair; take a settled no-action save afterward.
- [ ] Move one known item between two named party members; record count, owner and location.
- [ ] Move that item into and out of a bag in separate pairs.
- [ ] Move that item into and out of a camp chest in separate pairs.
- [ ] Supply Act 1, Act 2 and Act 3/camp examples, including hireling, origin and Dark Urge
      identities and identical-build multiplayer avatars.
- [ ] Capture duplicate item copies, rings, dual-wield, torch and instrument equip/unequip pairs
      with visible character sheets and loadouts.
- [ ] Capture separate damage/heal/temporary-HP, boosted-gear, learn/prepare-spell,
      level-up/respec, quest accept/complete/fail, approval change, rest/romance and
      recipe/waypoint event pairs.
- [ ] Provide build/language/module-matched community template/stat/ID facts with full attribution,
      including unlicensed sources under the owner decision. Record source terms and rights-holder
      attribution for publisher LOCA text/icons under rule 9; NC prose remains excluded. Record spoiler choices.

### PPSA01325 — ASTRO's PLAYROOM

Build notes: the sample has two primary memory.dat generations and identical backup twins, but the
build, region and actions are unknown; no special-bot keys are present. [Plugin checklist](../plugins/AstroPlayroom/README.md) · [issue #11](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/11)

**Current scope:** Enabled: existing coin/play-count/boolean controls and four reviewed presets; unknown
integrity and pickup dependencies remain.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Validate coin and play-count edits independently, plus supported Progress/World/ownership/cinematic
      flags one at a time. Identify the native PlayCount event and actual coin cap; storage bounds are not
      game limits.
- [ ] **Rank 13 — restore ownership (`collectibles/restore-imported`).** On a backed-up spare slot,
      capture a no-action control, one pickup and an independent ownership-reversal trial. Open a save
      with an existing true IsGot, stage it false, select that collectible in All saved collectibles,
      open Record details → Restore imported ownership (review before apply) → Preview; check raw
      group/key, false→true and skips, then Apply → undo/redo → export/re-import. Above-opening grants
      must stay unchanged. Check museum/puzzles, notifications, cinematics, rewards and forward play;
      native-save/re-export and reload twice. Record linked-state changes and unknown-integrity/rejection
      results with the shared evidence on #31; this action does not revoke grants or reverse a world.
- [ ] Test artifact, puzzle and prop grants independently, then selected/full/all-matching collect-existing
      batches. Inspect museum/prizes, cinematics, notifications, rewards, UdsWorkaround and primary/backup
      diffs after a second clean reload.
- [ ] Preview Memory Meadow ownership (9 artifacts + 16 internal-prefix puzzles), then repeat with an
      excluded filtered record. Confirm actual membership; this IsGot-only preset does not repair world
      completion or rewards.
- [ ] Test gatcha top-up at 1 and 17 attempts on low balances and with an already higher balance. Confirm
      max(current, attempts × 100), actual 100-coin cost, random prizes, clamping and persistence; no
      guaranteed completion budget.
- [ ] Gain exactly one coin.
- [ ] Spend exactly 100 coins on one gatcha.
- [ ] Test coin boundary/clamping behavior and photograph displayed values.
- [ ] Pick up one named artifact; record catalogue, notification and cinematic behavior.
- [ ] Win one prop/gatcha prize separately.
- [ ] Clear one normal stage separately.
- [ ] Clear one boss separately.
- [ ] Capture one puzzle at the Renderforest/Teraflop Treetops boundary.
- [ ] Capture one puzzle at the Raytrace Ruins/Mt. Motherboard boundary.
- [ ] Capture one puzzle at the Bot Beach/Springy Spa boundary.
- [ ] Capture one puzzle at the Gusty Gateway/Fastlane Fields boundary; record the physical
      piece/location and UI piece count, never a guide ordinal.
- [ ] Provide catalogue screenshots for all 50 artifact aliases, especially ds3 (DualShock 3
      versus SIXAXIS), and identify the 18 prop scenes/prizes if possible.
- [ ] Supply old/new update pairs from one profile for event eligibility.
- [ ] Supply newer-build before/after pairs for each of the four special-bot discoveries/rescues.
- [ ] Supply newer-build before/after pairs for each added-artifact acquisition.
- [ ] For transfer research, capture both games before and after the actual transfer with
      offline/online context.

### PPSA10695 — Atomic Heart

Build notes: this plugin handles the custom SaveExtension format, not standard GVAS. The observed
codec is Kraken; inner integrity and duplicated state remain unresolved. Use the exact console build,
region, DLC and slot in every filename. [Plugin checklist](../plugins/AtomicHeart/README.md) · [issue #12](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/12)

**Current scope:** Enabled: populated Amount, constrained existing asset replacement and F6 Amount batches.
SHA-1 repair does not establish inner integrity or stored-Kraken acceptance.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Arrange independently reviewed decoder agreement for every original compressed Kraken chunk;
      investigate inner integrity/duplicate state separately. The stored-literal output oracle does not
      validate original compressed chunks.
- [ ] Validate one small populated Amount edit and zero separately; check use/spend, travel, output growth,
      PS5 slot capacity and normalization after restart/reload.
- [ ] **Rank 6 — restore populated Amounts (`inventory/restore-imported-amounts`).** On a backed-up
      spare slot, capture a no-action control and separate resource/ammo/healing-item use and native-refill
      pairs. Expand populated inventory slots, stage small Amount reductions, select those rows →
      Restore imported quantities (preview) → Preview opening targets, signed/container ceilings, skips
      and SHA-1/framing warnings → Apply → undo/redo → export/re-import. Equal/higher and empty/unsupported slots must stay
      unchanged. Load, use/spend and travel; check actual counts, output growth/slot capacity and unrelated
      state, then restart/reload/re-export. Record stored-Kraken rejection/normalization and unresolved
      inner integrity with the shared evidence on #31; repaired SHA-1 alone does not establish acceptance.
- [ ] Test one same-category weapon replacement and one ammo replacement using full paths already present in
      the save. Inspect old/new asset IDs, ItemId, modules, ownership, equipment, capacity and use.
- [ ] Test selected/full eligible and filtered-all-matching Amount batches with an exclusion; verify
      unloaded matching rows, excluded rows and unrelated decoded spans. Identify the active region
      separately from saved/unvisited levels.
- [ ] Provide a lawful matching-build/locale asset pack for exact resource/consumable/skill/module names,
      costs, limits, prerequisites, footprints and reward joins; formatted raw IDs are not localization.
- [ ] Gain one Neuropolymer.
- [ ] Spend one Neuropolymer separately.
- [ ] Pick up one material; record exact material and count.
- [ ] Craft or spend one material separately.
- [ ] Pick up one ammunition unit; distinguish loaded and reserve counts.
- [ ] Fire one round/reload separately; record loaded and reserve counts.
- [ ] Use one small healing item.
- [ ] Move one identified stack backpack to NORA storage.
- [ ] Move that stack back; include an empty-to-populated slot transition.
- [ ] Buy and refund one skill in separate pairs.
- [ ] Equip and upgrade one weapon in separate pairs.
- [ ] Learn one blueprint.
- [ ] Collect one Chirper.
- [ ] Collect one Testing Ground reward.
- [ ] Advance one objective.
- [ ] Add early, open-world, late, NG+ and DLC states before making broad claims.

### PPSA04027 — Cyberpunk 2077

Build notes: the Cyberpunk plugin's sample has no controlled console capture; the early sample has
no allocated modern perks or installed cyberware. Later PS5 versions/regions/DLC need their own
evidence. [Plugin checklist](../plugins/Cyberpunk2077/EVIDENCE.md) · [issue #13](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/13)

**Current scope:** Enabled: supported 2.21 binary gameplay writes and GenericJson v136 settings adapters;
equipment/perk/story dependency writers remain incomplete.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] **Rank 9 — unspent point targets (`progression/set-point-targets`).** On a backed-up spare slot,
      capture a no-action control and separate earn/spend pairs for attribute, perk and relic points.
      Select the gameplay document → Player development → Set unspent point targets (document scope;
      nonempty row/query selections are unsupported) → enter small explicit targets → Preview all three
      independent balances/skips/warnings → Apply → undo/redo → export/re-import. Check menus and actual
      spending, unrelated spent allocations/attributes and native save/reload/re-export. Record each
      opening/target/displayed budget and opaque-integrity/rejection effects with the shared evidence
      on #31; this performs no refund, perk purchase or entitlement grant.
- [ ] Validate money, supported stacks, each attribute and each point balance independently. For
      target/refill batches check ammo/components/consumables, 2.x charge/replenishment behavior, protected
      rows and quantities already above the opening value.
- [ ] Validate Level, Street Cred, both current-XP and both stored-threshold fields separately; capture
      threshold crossings/rewards and whether the game retains or recomputes thresholds. Test one known
      noncritical existing fact with related callbacks, forward play and stage checks.
- [ ] For settings, capture static choice/index and scalar changes, dynamic language/HDR choices and native
      resets separately. Validate paired value/index authority, recorded-default single/selected/all
      presets, exact menu effects, restart persistence and actual float steps. Keep unsupported versions
      separate.
- [ ] Buy exactly one known ammo/component stack; record item, cost and quantity.
- [ ] Fire/use exactly one unit in a separate pair, including automatic replenishment.
- [ ] Equip one existing weapon or clothing item; capture before, after equip and after unequip.
- [ ] Move one existing item between player and stash.
- [ ] Upgrade one existing item once; record tier, stats and materials.
- [ ] Install one existing cyberware implant.
- [ ] Remove that implant separately; record slot, capacity and bonuses.
- [ ] Obtain two identical-base items; equip each in the same slot and then unequip separately,
      including fists versus weapon/cutscene slots for RightArm.
- [ ] Attach one named mod/attachment and remove it in a separate pair.
- [ ] Buy and refund one ordinary perk rank.
- [ ] Buy and refund one Relic perk rank, with the Relic access unlock alone.
- [ ] Change one capacity-granting perk or shard separately from implant install/remove.
- [ ] Supply mature base-game and Phantom Liberty inventories with installed cyberware.
- [ ] Acquire one named car; summon, despawn and favorite it in separate pairs.
- [ ] Discover one named dataterm and use it in a separate pair; capture one quest-controlled
      travel lock/unlock separately if available.
- [ ] Capture one precise quest objective transition, one tracking-only toggle, one gig and one
      base/Phantom Liberty collectible separately.
- [ ] Capture one Level, one Street Cred and one skill reward threshold separately.
- [ ] If obtained, supply later PS5 versions/regions/DLC states, including the 277/2500 case.

GenericJson overlay captures for this same title are deduplicated here: one static enum change,
one scalar change plus reset-to-default, one dynamic language/HDR choice, a restart/resave after
each, and a second settings version to test value/index authority. [GenericJson checklist](../plugins/GenericJson/README.md) · [issue #18](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/18)

### PPSA03169 — Elden Ring

Build notes: the PS5 payload is memory.dat with ordinary backup twins; application and regulation/
calibration versions, region and DLC are not established by the sample. Keep one character and
version per pair. [Plugin checklist](../plugins/EldenRing/Evidence/OWNER_CAPTURES.md) · [issue #14](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/14)

**Current scope:** Enabled: six bounded rune/name/quantity/history operations.
Inventory/equipment/stat/world candidates do not imply further writers.

- [ ] Two no-action controls with elapsed time/unchanged menus, plus unchanged export/recovery; retain
      primary and backup separately.
- [ ] Validate held-only rune field and preview separately; inspect slot/menu and both history copies. Test
      manual history and linked balance/history with no repeated accumulation. History ceiling max(imported,
      4427) is an editor policy, not a game limit.
- [ ] Validate mirrored live/menu names with Unicode, a shortening and exactly 16 UTF-16 units; preserve
      padding and exact undo bytes.
- [ ] Test each existing positive Rowa Fruit (1–12) and Smithing Stone [1] (1–3) stack in held and nonempty
      storage. Supply native storage fixtures: present coverage is synthetic. Refill two eligible rows,
      including a filtered exclusion; these observed ceilings are not legal caps.
- [ ] Observe Church of Elleh (76100) and The First Step (76101); capture one exact grace/boss/map-fragment
      event and surface/underground display toggle separately. Record source symbol/raw path; map
      ownership/reveal/completion meanings remain unconfirmed.
- [ ] Buy one Throwing Dagger; record count, rune balance and price.
- [ ] Use one Throwing Dagger; separately use the final N=1→0 item.
- [ ] Move five Rowa Fruit into storage and record held/storage counts.
- [ ] Move those five back out of storage.
- [ ] Equip and unequip one HP-affecting talisman.
- [ ] Move one owned Scimitar into an empty hand and unequip it separately.
- [ ] Swap two same-base weapons with distinct upgrades/Ash of War.
- [ ] Change only the active left-hand selector, then only the right-hand selector; capture
      one-hand/two-hand separately.
- [ ] Equip and unequip one held ordinary quick/pouch item; capture selector-only changes.
- [ ] Memorize and remove one owned spell from an empty slot.
- [ ] Change one owned talisman in an unlocked slot; separately earn one talisman pouch or memory stone.
- [ ] Upgrade one normal weapon +0→+1; supply +25 and somber +10 states when available and
      capture somber +0→+1 separately.
- [ ] Record highest-upgrade history after storing or discarding the highest-upgraded instance.
- [ ] Change one weapon affinity and one Ash of War separately, including the reverse changes.
- [ ] Capture one arrow stack, one bolt stack and one equip-slot change separately.
- [ ] Capture nonempty cracked-pot and perfume stacks and one crafting/use pair each.
- [ ] Rename with a non-ASCII name, shorten it to two code units and set exactly 16 code units
      in separate pairs; record padding/terminator behavior.
- [ ] Level Vigor, Mind and Endurance one point at a time.
- [ ] Respec a fixed total level; record class minima and all vitals.
- [ ] Repeat one attribute change with and without an HP-affecting effect, including damaged HP.
- [ ] Earn and spend runes separately; capture death, recovery and unrecovered loss separately.
- [ ] Show one action in one active slot leaves the other slot semantically unchanged.
- [ ] Supply JP/Asia/EU/US and older/current/base/DLC examples where available.
- [ ] Capture one grace, summoning pool, map fragment, boss and collectible separately.
- [ ] Capture base/DLC blessing changes and a selected quest branch/NG+ transition separately.
- [ ] Supply full/near-full held/storage and acquisition-history boundaries.

### PPSA10665 — Final Fantasy XVI retail

Build notes: retail and demo use different supported schemas and banks; capture normal/Final Fantasy
NG+, adult/child stage and DLC separately. [Plugin checklist](../plugins/FinalFantasy16/README.md) · [issue #15](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/15)

**Current scope:** Enabled: supported existing inventory/equipment, raw ability, player/party/time fields
and reviewed main-stack targets. No allocator/growth/dependency repair is claimed.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] **Rank 4 — named balances (`set-gil`, `set-ap`, `set-renown`).** On separate backed-up spare
      copies, capture a no-action control and exact gain/spend pairs for each resource. Select the
      document → Overview → Set Gil / AP / Renown balance, one control per trial → enter a small target
      for its unique existing main-bank row → Preview old/new balance, skips, dependency warnings and
      repaired CRC → Apply → undo/redo → export/re-import. These are single-resource controls, not a
      selected inventory batch. Load, use/spend, native-save/reload/re-export; record adult/child/stage,
      cap/reset, AP ranks/refunds and Renown reward-claim state with the shared evidence on #31.
- [ ] **Rank 12 — restore ordinary main stacks (`restore-imported-main-stacks`).** On a backed-up
      spare slot, capture a no-action control and separate consumable/material use/sell pairs. In
      Inventory, stage positive reductions on named eligible potions/tonics or common materials → select
      main rows → Restore imported ordinary main-bank stacks → Preview opening targets, skips and CRC
      → Apply → undo/redo → export/re-import. Verify zero, currency, unique/key/progression/upgrade,
      unmapped, ambiguous and secondary rows skip, and above-opening amounts stay unchanged. Load and
      use/sell; check active-bank authority, pouch/crafting dependencies and native save/reload/re-export.
      Record both banks, counts, context and side effects with the shared evidence on #31.
- [ ] Validate main/secondary quantities and nonzero-ID replacement separately; inspect both banks,
      duplicate-ID handling, zero-ID quantity behavior, acquire/use/sell and active adult/child/stage roles.
      Test equipment in each of the three banks, one slot at a time.
- [ ] Validate raw rank/newFlags/protectFlags one index/bit at a time; capture learn/upgrade/master/reset AP
      deltas, Eikon/slot and menus to join the 48 named reference abilities to actual saved indices. Reference
      progression is not saved learned state.
- [ ] Validate adult/child level and XP separately, then HP/recovery/LB, summon index/learned/revealed
      bits/IDs, command IDs, shortcuts, party scene/map/member IDs/count and time independently. Inspect
      ownership, AP, growth, battle, location and sibling dependencies; no teleport guarantee.
- [ ] Test main-stack target on two rows and populated retail F8 category-filtered results with one excluded
      unloaded row; inspect CRC/skips, undo/redo and use/save/reload. Test zero matches and fully excluded
      results separately.
- [ ] Capture acquire/sell-last/reacquire/slot-reuse/capacity and one obelisk/collectible event separately
      before allocator or completion writers can be substantiated.
- [ ] Buy one potion, use one, sell one material and reacquire one allocated zero-stack item,
      each in a separate pair.
- [ ] Equip one weapon, belt, vambrace and accessory separately, recording all six UI slots.
- [ ] Include boyhood and stage-replay pairs to identify active banks.
- [ ] Unlock one Eikon separately from any story transition.
- [ ] Earn XP without leveling, then cross one level; repeat adult/child and normal/Final Fantasy mode.
- [ ] Supply matched, legally owned PC binary/XML states with build/locale/DLC if available.
- [ ] Supply attributed community labels, including unlicensed tables under the owner decision, or observed
      item-label/acquisition pairs for the 41 Unknown IDs; attribute publisher-extracted text under rule 9.
- [ ] Capture one upgrade each for potency/capacity IDs 506, 507, 508 and 509, with both banks,
      displayed capacities/potencies, quest, mode, DLC, stage, zero-stack control and stage replay.
- [ ] Capture learn, upgrade, master and reset for skill IDs 39, 45 and 49 independently, with
      AP deltas, rank, Eikon assignment, all rank bytes and both flag arrays.

### PPSA13803 — Final Fantasy XVI demo

Build notes: demo records never inherit the retail extended player schema. Keep demo evidence and
console checks separate from PPSA10665. Use the same checklist and record demo stage, mode and bank
identity explicitly. [Plugin checklist](../plugins/FinalFantasy16/README.md) · [issue #15](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/15)

**Current scope:** Enabled writes use the demo schema and observed bounds; never inherit retail
extended-player semantics.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Repeat the retail enabled-write cycle only for fields actually exposed by the demo: inventory/gear,
      ranks/flags, supported player/party context, time and quantity action. Constant observed bounds can
      leave a field with no alternative value.
- [ ] Repeat **ranks 4 and 12** from the retail checklist independently on backed-up demo spare slots,
      with demo no-action controls. Use only exposed, eligible resource/main-stack rows; record missing
      controls or skipped rows rather than borrowing retail meanings. Report demo observations on #31.
- [ ] Test selected/full/filtered quantity scopes, exclusions, empty results and one-step history
      independently. Verify demo stage/bank/AP and downstream effects; keep the 48-ability reference names
      separate from saved-index claims.
- [ ] Capture one potion buy/use, material sale and zero-stack reacquisition separately.
- [ ] Capture one weapon, belt, vambrace and accessory equip separately.
- [ ] Capture one skill learn/upgrade/master/reset and one Eikon unlock separately.
- [ ] Capture one no-level XP gain and one level crossing.
- [ ] Capture the demo-specific 506/507/508/509 capacity/potency transitions with stage and bank.
- [ ] Capture demo-specific skill IDs 39/45/49 learn/upgrade/master/reset with exact flags.

### PPSA01911 — Final Fantasy VII Remake / INTERmission

Build notes: Remake main and INTERmission are separate roles under the same title ID. The supplied
samples show writable gil, occupied quantities and ordinary materia AP, but no PS5 console test.
[Plugin checklist](../plugins/FinalFantasy7/README.md) · [issue #16](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/16)

**Current scope:** Enabled: Remake/INTERmission mapped HP/MP/XP/level, gil/counts, ordinary materia
AP/stars, progress bits, restock/star presets, recovery/level batches and stored first-record time.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] **Rank 1 — current HP/MP recovery (`character/recover-current-vitals`).** For Remake main and
      INTERmission separately, use a backed-up spare slot, capture a no-action control and separate
      damage/heal/MP-use pairs identifying each active character. Characters → select intended rows →
      Recover selected characters' current HP/MP **inside the table** → Preview deficient current values
      against stored maxima, unselected rows and skips → Apply → undo/redo → export/re-import. The
      controls above the table use document scope. Check current/max HP/MP in menus and battle, MP spend,
      unrelated XP/equipment/status and native save/reload/re-export; over-max current values must not
      decrease. Record character identity, displayed effects and unknown-integrity results on #31.
- [ ] **Rank 8 — character levels (`character/set-level`).** On backed-up spare copies for Remake main
      and INTERmission, capture a no-action control and one native level-up with current/max HP/MP,
      XP/level, abilities and rewards. Characters → select intended rows → Set selected character levels
      **inside the table** → choose a small supported 1–50 target → Preview each level/derived-XP pair
      and invalid/guest skips → Apply → undo/redo → export/re-import. Check exact original XP/level
      restoration on undo, including unchanged target XP. Load, battle, check abilities/growth/rewards,
      native-save/reload/re-export; maxima/rewards are not recalculated by this action. Record selected
      versus unselected characters and all observed normalization with the shared evidence on #31.
- [ ] **Rank 15 — stored play time (`game/playtime`).** For each role, capture timed no-action pairs
      and displayed time on a backed-up spare slot. Select the document → Overview → Play time → reduce
      whole seconds within zero through opening time and press Enter or leave the field → review
      Changes against original → undo/redo → export/re-import. This scalar has no row selection or Preview/Apply dialog;
      review its committed change before export. In the original workspace also test restoration up to
      opening time. Check displayed time and native save/reload/re-export normalization; older records
      and other menu/play counters are outside this setter. Record elapsed control time, opening/target/
      displayed seconds and any counter disagreement with the shared evidence on #31.
- [ ] Validate every scalar family separately for main game and INTERmission: battle/MP use, coupled
      XP/level, gil and mapped non-equipment counts. Ordinary AP/stars need below/at/above threshold and
      increase/decrease checks on equipped/unequipped copies.
- [ ] Test restock of existing positive ordinary consumables and materia-star preset with derived AP;
      inspect skipped rows, scopes, use/abilities and grouped undo. Summon progression needs independent
      evidence.
- [ ] Test a single mapped progress bit per available domain; inspect actors/rewards/counters/prerequisites
      and chapter reset/reversal. Capture Remake upgrade purchase/reset with SP/grid/sockets; progress edits
      can softlock gameplay.
- [ ] Capture one XP level-up.
- [ ] Capture one materia AP star-up.
- [ ] Capture one gear swap.
- [ ] Capture one materia move.
- [ ] Repeat the pairs for Remake main and INTERmission separately, recording chapter/difficulty.
- [ ] Capture capacity/creation/deletion boundaries: buy two identical materia, equip/swap/sell
      one, acquire/discard one weapon and reach a full inventory.
- [ ] Supply known-state party/gear/materia screenshots paired to sample identities.

### PPSA08664 — Final Fantasy VII Rebirth retail

Build notes: Rebirth retail has its own linked-record schema and 23 MiB gameplay file; it is not
interchangeable with Remake or the demo. [Plugin checklist](../plugins/FinalFantasy7/README.md) · [issue #16](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/16)

**Current scope:** Enabled: retail Rebirth mapped scalars, ordinary AP/stars, qualified owned-weapon
references, counts/progress bits, restock/star presets, recovery/level batches and stored first-record time.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Repeat HP/MP/gil/count and coupled XP/level validation on a retail slot. Test ordinary AP/star
      thresholds and both presets, including scope exclusions and equipped abilities.
- [ ] Repeat **ranks 1, 8 and 15** from the Remake checklist on backed-up Rebirth retail spare slots,
      with independent damage/heal/MP-use, native level-up and timed no-action captures per active
      character. Use the retail 1–70 level curve; check battle/abilities/growth/rewards and stored-time
      normalization after native save/reload/re-export. Report retail evidence separately on #31.
- [ ] Test one qualified existing owned weapon reference; inspect displaced gear, sockets/capacity,
      proficiency, folios, ownership and battle effects.
- [ ] Validate progress bits independently with actors/rewards/counters, chapter reset and reversal. Capture
      folio purchase/reset per character with SP/prerequisites; crafting recipe/ingredients/result/rank,
      minigame score/rank/reward, assessment/defeat and relationship events separately. Confirm card IDs
      012/013 and summon tiers independently.
- [ ] Capture one XP level-up.
- [ ] Capture one materia AP star-up.
- [ ] Capture one gear swap and one materia move separately.
- [ ] Capture card acquisition/opponent/deck changes, stop repair and World Intel events separately.
- [ ] Capture capacity/creation/deletion and full-inventory boundaries, including duplicate materia.
- [ ] Supply screenshots for Rebirth guests, past records, summon levels and owned relationships.

### PPSA18936 — Final Fantasy VII Rebirth demo

Build notes: demo and retail use distinct schema identities despite matching dimensions. Demo
meanings remain Inferred; supported writes are enabled with their own warnings. [Plugin checklist](../plugins/FinalFantasy7/README.md) · [issue #16](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/16)

**Current scope:** Enabled: supported demo mappings with Inferred meaning; demo and retail are separate
schema identities.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Independently validate exposed demo gil/counts, HP/MP/XP/level, ordinary materia AP/stars, qualified
      weapon references, mapped progress bits and both presets where eligible records exist. Check battle,
      abilities, sockets, rewards and chapter reset/reversal; never infer absent retail systems.
- [ ] Repeat **ranks 1, 8 and 15** independently on backed-up Rebirth demo spare slots, using its own
      no-action/damage/heal/MP-use/level-up/timed captures and supported 1–70 targets. Record locked or
      guest-unqualified skips, demo active-character identity and Inferred time meaning; check battle,
      abilities and native resave/normalization. Report unavailable events explicitly on #31.
- [ ] Test selected/document/query presets only where exposed, with skipped/protected rows, exclusions and
      one-step undo. Keep summon tier/progression evidence separate.
- [ ] Capture one XP level-up.
- [ ] Capture one materia AP star-up.
- [ ] Capture one gear swap and one materia move separately.
- [ ] Capture demo card/progression events only when they actually exist in the demo.

### PPSA01463 — Borderlands 3

Build notes: BL3 uses GVAS with engine 4.20.3 and branch OAK; patch, DLC and hotfix completeness
are unknown, and serial identity must be retained. [Plugin checklist](../plugins/Gearbox/README.md) · [issue #17](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/17)

**Current scope:** Enabled: BL3 character/item edits, constrained serial parts, structural inventory actions
and explicit F7 character/profile transactions.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] **Rank 2 — match gear level (`items/match-level`).** On a backed-up spare slot with matching
      character/profile/backups, capture a no-action control and owned item cards/character level.
      Select backpack rows → Serial and parts → Match selected gear to character level → Preview this
      character's XP-derived target clipped to BL3's 72 policy, old/new levels, unsupported skips and CRC
      → Apply → undo/redo → export/re-import. Use no unrelated staged XP changes. Load, compare cards,
      equip/use and native-save/reload/re-export; record legality/rejection/removal and unrelated parts/
      rarity/stats with the shared evidence on #31. Clipping does not prove legal gear or synthesized stats.
- [ ] **Rank 3 — restore ammo (`ammo/restore-imported`).** On a backed-up spare slot, capture a
      no-action control and separate firing/reload/native-refill pairs with loaded/reserve displays and
      SDU capacity. In Progression, stage small supported pool reductions → Restore imported ammo amounts
      (document scope; no table selection required) → Preview every pool's opening target and skips →
      Apply → undo/redo → export/re-import. Equal/higher pools stay unchanged; no missing pool or SDU is
      created. Load, fire/reload, check reserves/capacity/clamping and native save/reload/re-export.
      Record each pool, displayed values and normalization with the shared evidence on #31.
- [ ] **Rank 10 — item marks (`items/mark`).** On backed-up spare copies, capture a no-action control
      and separate ordinary/favourite/junk mark/unmark pairs. Select existing backpack/bank rows → Serial
      and parts → Mark selected items → choose one mark → Preview raw old/new flags and conflicting/
      unsupported skips → Apply → undo/redo → export/re-import. BL3 byte-only bank/lost-loot rows skip.
      Load, check UI marks and sale-protection behavior, then native-save/reload/re-export. Record item
      cards, unknown-bit/serial preservation, unselected items and unexpected effects with the shared
      evidence on #31; the action itself does not sell/remove items.
- [ ] Validate name/level/XP/points, money/eridium/keys/ammo, SDUs, Mayhem and supported item
      level/reroll/marks independently. Test selected/full-query level actions and inspect capacities,
      loot/modifiers and rewards; editor policies are not measured PS5 limits.
- [ ] Replace one existing regular/generic serial part with a legitimate same-category alternative. Record
      serial/card/stats and build/DLC/hotfix, including duplicate/excluded-part behavior and
      rejection/removal; category/width/CRC alone do not prove a legal pool.
- [ ] Test import/duplicate/remove with first/middle/last, equipped, duplicate and full-container cases.
      Test F7 deposit/withdraw/lost-loot and scalar batches with matching character/profile/backups; reopen
      exported files and inspect persisted counts, order, marks, displaced/equipped indices and one
      whole-group undo.
- [ ] Validate existing Guardian character/profile ranks, XP, tokens, rewards/perks/enabled flags
      separately; inspect every mirrored character and budget/precedence. Capture one named skill allocation
      0→1, another rank and respec for every class/tree, including fourth-tree DLC. Calculator order does
      not join opaque saved skill IDs.
- [ ] Capture mission branch/completion/reward/claim separately; the F9 stored checklist does not implement
      mission repair.
- [ ] Pick up one item.
- [ ] Sell/remove one item.
- [ ] Equip and unequip one item separately.
- [ ] Toggle favourite/junk separately.
- [ ] Move one item backpack↔bank in separate directions.
- [ ] Claim one lost-loot item.
- [ ] Enchant/reroll one item.
- [ ] Capture BL3 starter-gun, cosmetic-pickup and legacy-serial versions.
- [ ] Gain one level, allocate/respec one skill, purchase one SDU and refill ammo separately.
- [ ] Capture Guardian profile plus multiple characters around one token/perk.

### PPSA01493 — Tiny Tina's Wonderlands

Build notes: TTWL has a separate schema and XP/leveling policy; current characters are level 12.
Supply level-40/endgame gear, nonzero rerolls and each Chaos tier. [Plugin checklist](../plugins/Gearbox/README.md) · [issue #17](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/17)

**Current scope:** Enabled: TTWL character/item and group operations, constrained serial parts, Hero/Myth
fields and Chaos settings, all with inferred limits.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Repeat the BL3 applicable scalar/item/part/import/duplicate/remove and selected/full-query action
      checks using TTWL schemas. Add rerolls, item Chaos tier, current Chaos and unlocked ceiling; inspect
      trials/rewards/loot/caps independently.
- [ ] Repeat **ranks 2, 3 and 10** independently on backed-up TTWL spare slots with matching profile/
      backups and TTWL no-action controls. Match gear using this character's level clipped to 40; test
      supported bank mark metadata rather than assuming BL3 bank behavior. Record cards/equip/use,
      firing/reload/refill, capacity/clamping and ordinary/favourite/junk sale protection through native
      save/reload/re-export. Report separate TTWL evidence on #31; a profile supplies no character level.
- [ ] Test F7 bank/lost-loot transfers and combined scalar batches with matching character/profile/backups.
      Verify native persisted counts, unknown item metadata, equipment/order and whole-group undo, including
      duplicates/full containers.
- [ ] Validate existing Hero raw/base/bonus values, Myth XP, each indexed allocation/cap and last-tree
      rotation independently. Capture respec/budget/mirror precedence, primary/secondary class choices and
      isolated named skill rank/respec pairs; do not infer names from calculator order.
- [ ] Capture one pickup, sale/removal, equip/unequip, favourite/junk, backpack↔bank move,
      lost-loot claim and reroll separately.
- [ ] Include duplicate and full/near-full containers and transcribed item cards.
- [ ] Supply level-40/endgame items with nonzero rerolls at each observed Chaos tier.
- [ ] Gain one level, allocate/respec one skill, purchase one SDU and refill ammo separately.
- [ ] Capture each indexed Myth allocation, rotation and capped stat with matched profile.

### PPSA01494 — Borderlands 4

Build notes: BL4 is research-only, with one 1,888-byte gbxsavegame.sav occurrence, no backup and
no established PS5 codec or file role. [BL4 checklist](../plugins/Gearbox/BL4-feasibility.md) · [issue #17](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/17)

**Current scope:** Research-only and excluded from the Gearbox editor; no toolkit gameplay writer exists.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Supply complete fresh-profile, settings-only-toggle and first-gameplay dumps with every file
      and available backup; record files appearing, vanishing or changing.
- [ ] Capture one known money change.
- [ ] Capture one legitimate item pickup with matching profile/settings files.
- [ ] Establish an owner-local PS5 derivation route without sharing credentials, keys or secrets.
- [ ] After role/codec proof, test padding, compression boundaries, trailer lengths, unknown-token
      retention, corruption rejection and byte-identical full-file no-op output.

### PPSA02343 — It Takes Two (EU) settings/profile

Build notes: the sample has three distinct JSON profile generations (3,993/3,522/1,312 bytes)
with byte-identical backup twins; archive order is not chronology. [Plugin checklist](../plugins/GenericJson/README.md) · [issue #18](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/18)

**Current scope:** Enabled: present string flags/counters, existing chapter/checkpoint strings/tuple and
reviewed present-flag/counter-zero presets.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Validate discovery versus played flags and casing, Cody/May wins/scores independently, native
      units/caps and zero behavior. Test selected/full present-flag and counter-zero presets; absent keys
      must remain absent.
- [ ] Validate each chapter/checkpoint string and the existing three-field tuple; inspect Continue, Chapter
      Select, replay, forward progression and unrelated story/rewards/trophies after restart. No complete
      unlock/story dependency graph is implemented.
- [ ] Confirm minigame aliases and checkpoint prerequisites on this build; provide missing-key/acquisition
      examples before title definitions or insertion templates are expanded.
- [ ] Record title/region, game build, wrapper/settings version, file roles and one action; retain
      all companions and duplicate/backup occurrences.
- [ ] Capture a fresh profile.
- [ ] Discover one minigame.
- [ ] Play that minigame.
- [ ] Capture Cody and May wins/scores separately.
- [ ] Enter one chapter.
- [ ] Cross one checkpoint.
- [ ] Capture incomplete and complete profiles on the same version, preferably from two owners.
- [ ] Record Continue, Chapter Select and replay behavior without treating archive order as time.

### PPSA07696 — Ridge Racer 2 (PSP classic)

Build notes: GenericJson covers the 1,320-byte imgui_emu_option.json wrapper; DATA.BIN gameplay
is separate and PS1 preset meanings do not establish PSP values. [Plugin checklist](../plugins/GenericJson/README.md) · [issue #18](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/18)

**Current scope:** Enabled: conservative wrapper primitive edits and restore-imported preset; DATA.BIN
gameplay is outside GenericJson.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Validate a supported scalar at a time and restore-imported single/selected/all scopes. Check
      zero-to-imported domains, zero-only unknown enums, native restart persistence and all
      companions/DATA.BIN. Imported reset is not a native-default preset.
- [ ] Capture each filter preset before/after.
- [ ] Capture each upscale preset before/after.
- [ ] Capture each anti-aliasing preset before/after.
- [ ] Capture one controller option.
- [ ] Capture one rewind option.
- [ ] Record the PSP wrapper build for every pair.

### PPSA08330 — God of War Ragnarök

Build notes: only PPSA08330 has sample coverage; the plugin catalogs other regional IDs and has no
Valhalla sample. Nested grammar and integrity remain unproven. [Plugin checklist](../plugins/GodOfWarRagnarok/OWNER_CHECKLIST.md) · [issue #19](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/19)

**Current scope:** Enabled: existing decoded wallet counters, play time and F6 restore. Integrity remains
unknown; preferences/gear/story writers are unimplemented.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Validate a small resource/Hacksilver reduction and use/acquisition, then raw EconomyXP, each raven
      counter, one unmapped ID and one positive non-hero row independently. Inspect all-character XP/skills,
      journal/chests/rewards and forward play; zero imports permit only zero.
- [ ] Reduce play time by one second; check displayed duration and later play. For every trial capture
      rejection text, native-save, second clean reload and decrypted diff against the control.
- [ ] Reduce two counters and restore to imported values via selected, document and filtered query/excluded
      scopes; check one undo/redo step. Reopening establishes new ceilings; restore never exceeds the
      import.
- [ ] Provide redacted authentication/derived-byte observations from legal owner-local experiments plus
      controlled PS5 fields. Identify gear/quest/collectible joins and regional/Valhalla dependencies;
      unsuccessful checksum searches do not prove acceptance.
- [ ] Change exactly one Hacksilver/resource quantity.
- [ ] Change one XP wallet.
- [ ] Make one gear upgrade.
- [ ] Capture one native quest transition.
- [ ] Change one collectible.
- [ ] Capture one native user-preferences setting change.
- [ ] Supply one late/Valhalla save and two additional regional PS5 IDs.
- [ ] Supply paired PS4 and PC saves for the same gameplay state where legally available.
- [ ] Preserve the originals and record load/reject text and in-game values for every pair.

### PPSA01474 — Ratchet & Clank: Rift Apart

Build notes: the plugin is Experimental for decrypted PPSA01474. Use one recorded build/region, normal
and Challenge Mode controls, all sibling files and backup twins. [Plugin checklist](../plugins/Insomniac/OWNER-CAPTURES.md) · [issue #20](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/20)

**Current scope:** Enabled: two bolt counters, seven existing unlocked-option booleans, 61 boolean
preferences and F6 calculated bolt target.

- [ ] Two normal and two Challenge no-action controls, plus unchanged gameplay/preferences full-set export
      and recovery.
- [ ] Capture independent bolt earn/purchase pairs, then validate CareerBolts +1 and SpentBolts ±1
      separately. Inspect HUD/purchases, final calculated balance and spending history; test F6 target lower
      by one, zero and a small increase with preview/undo/redo.
- [ ] Capture each gold-bolt option natively, then edit each existing already-unlocked boolean separately.
      Include legal RPG behavior with at least six gold bolts; editing an option does not grant
      collectibles.
- [ ] Independently export/recover preferences unchanged; capture one native setting and edit one supported
      boolean at a time, checking menu effect and persistence. Numeric/string preferences remain protected.
- [ ] Capture exact spybot-bit/planet/pickup joins, upgrade-grid masks/counts/costs/prerequisites and three
      non-collinear landmarks per proposed map transform. Storage diagrams are not calibrated world maps.
- [ ] Buy one ordinary weapon absent from the wheel.
- [ ] Earn XP with only that weapon; cross one level separately.
- [ ] Fire one shot; refill ammo separately; capture death/checkpoint reload separately.
- [ ] Earn one raritanium; buy one ordinary node, a second-row node and an unlocked gold node
      separately.
- [ ] Buy one Omega weapon on an existing Challenge slot, separate from Challenge creation.
- [ ] Supply a permitted build/locale config/localization pack if available.
- [ ] Collect one head, one torso and one leg separately; capture a full-set bonus.
- [ ] Equip and unequip each armour slot on each character separately.
- [ ] Change one equipped colour twice in separate pairs.
- [ ] Capture edition/anniversary entitlement changes separately.
- [ ] Capture one spybot per location, with separate Scarstu pairs.
- [ ] Capture one documented gold bolt per location with guide identity and map coordinates.
- [ ] Capture one Lorb, one Zurpstone and one CraiggerBear separately, plus known-complete states.
- [ ] Finish one pocket dimension and one arena challenge separately.
- [ ] Create Challenge Mode separately, then resave/reload to record carry-over/reset.

### PPSA02530 — Pragmata

Build notes: all supplied samples are fixed-size DSSS/Mandarin and uncontrolled; no causal runtime
or console evidence exists. Keep system and gameplay directories together. [Plugin checklist](../plugins/Pragmata/evidence/README.md) · [issue #21](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/21)

**Current scope:** Enabled: existing ID/quantity/header/clock/Raw edits and primitives in Gadgets, Loadout,
Progress, Quests and World. RSA trailer is preserved but unverified/unregenerated.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] **Rank 14 — restore quantities (`inventory/restore-imported-quantities`).** On a backed-up
      spare slot, capture a no-action control and identify/use one stack without assuming a friendly
      name. Expand its inventory quantity controls, stage a small nonnegative reduction with raw ID
      unchanged → select rows (or bounded all-matching F8 results with an exclusion) → Quantity actions →
      Restore imported quantities → Preview opening targets, raw IDs, skips and RSA/dependency warnings
      → Apply → undo/redo → export/re-import. Negative/changed-ID rows skip; duplicate/ambiguous/unbound targets must not
      apply. Load/use and inspect equipped/unlock effects and all siblings, then native-save/reload/
      re-export. Record raw IDs, stack identity, hash/encryption checks and RSA rejection or persistence
      with the shared evidence on #31; this grants no named resource, mission state or capacity.
- [ ] Validate one small quantity through preview/apply/undo/redo/export/re-import and one existing ID
      replacement. Inspect equipment/ownership/dependencies and every sibling file; known hash repair does
      not prove RSA acceptance.
- [ ] Validate one value per enabled family separately: Gadgets/shortcut use, Loadout weapon/perk/preset,
      Progress, Quests and World. Check downstream prerequisites, interactions, rewards, normalization and
      rejection.
- [ ] Provide a real inventory above 200 rows; test a filtered whole-query quantity action with one excluded
      row, including unloaded rows and console persistence. Existing large-list software coverage is
      synthetic.
- [ ] Join a checkpoint/hatch raw hash to a named location with one unlock pair. For the six existing item
      IDs linked to 18 mission references, capture selectability, chapter/runtime prerequisites and quantity
      separately; PC opening-condition data does not establish a full PS5 unlock rule.
- [ ] Supply three independent before/after pairs for each family below; keep every operation in
      its own pair and identify the visible item/event and exact delta.
- [ ] Capture one known item pickup, use and purchase separately.
- [ ] Capture one gadget/shortcut equip and unequip separately.
- [ ] Capture one weapon purchase and equip separately.
- [ ] Capture one perk upgrade and equip separately.
- [ ] Capture one objective/occurrence-event completion.
- [ ] Capture one checkpoint unlock.
- [ ] Capture one chapter transition.
- [ ] Capture one named collectible pickup.
- [ ] Supply empty/full inventories and difficulty, chapter, cleared and NG+ examples.
- [ ] Supply attributed community ID/name evidence, including unlicensed sources under the owner decision;
      record source, rights-holder and revision/date attribution for publisher-extracted text/art under rule 9.

### PPSA02225 — Ghost of Tsushima (Director's Cut)

Build notes: Tsushima uses container versions 47–49 and has existing quantity edits, but none are
console-tested. Keep campaign/Legends, NG+/Iki, DLC and locale separate. [Plugin checklist](../plugins/SuckerPunch/README.md) · [issue #22](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/22)

**Current scope:** Enabled: existing resources/ammo and their count mirrors, reviewed quantity actions and
the v48/v49 float timer.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] **Rank 7 — restore ammo (`inventory/restore-imported-ammo`).** On a backed-up spare slot,
      capture a no-action control and separate fire/pickup/reload pairs across ammo kinds. In Inventory,
      stage small eligible ammo reductions → select ammo rows → Restore selected ammo counts inside the
      table → Preview opening counts, both copies, checksum and skips within version/flag/0–32767 gates
      → Apply → undo/redo → export/re-import. Cancel if unselected rows appear; equal/higher values
      must stay unchanged. Load, fire/pick up/reload and check displayed counts, carry capacity/clamping
      and unrelated state through native save/reload/re-export. Record both count copies, kinds and
      side effects with the shared evidence on #31; the action grants no capacity.
- [ ] Validate a small resource and ammo edit separately, then a selected/full/filter-excluded multi-target
      action. Inspect carry caps, paired ammo counts, use/buy and clamping; 32767 is an editor bound, not a
      capacity.
- [ ] Test a small supported timer edit while leaving param.sfo SUBTITLE and per-difficulty counters
      unchanged; inspect both menus and normalization after reload. Do not infer v47 timer support.
- [ ] Gain one Supplies; pick one flower; sell one Predator Hide, iron, yew wood and wax wood in
      separate pairs.
- [ ] Fire and pick up one arrow; upgrade one pouch/quiver.
- [ ] Equip one owned armor, sword kit and charm separately.
- [ ] Switch one enabled armor loadout.
- [ ] Acquire a second copy of one minor charm.
- [ ] Buy one dye.
- [ ] Upgrade one existing armor/weapon tier.
- [ ] Earn one technique point; spend one named technique point; buy one prerequisite chain; unlock
      one stance; finish one mythic tale; gain one legend rank; take damage; heal—each separately.
- [ ] Discover one shrine, fox den, hot spring, haiku, bamboo strike, pillar and quest separately
      from completion/reward; record region, journal state and rewards.

### PPSA26344 — Ghost of Yōtei

Build notes: eleven nonempty samples have zeroed tails; exact game checksum coverage remains unknown.
[Plugin checklist](../plugins/SuckerPunch/README.md) · [issue #22](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/22)

**Current scope:** Enabled: 14 eligible existing quantity types and a target preview, Experimental/Inferred
with checksum/game-rejection warnings. Restore-imported quantities remain deferred; gear/world state is read-only.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Capture one nonzero Mon/resource/ammo single-action pair.
- [ ] Capture manual-save, autosave and memory role controls.
- [ ] **Deferred checksum-coverage capture (outside the 15 ranks).** On a backed-up disposable slot,
      capture identical gameplay in manual/auto/memory roles plus a no-action load/save control, keeping
      all gameplay/memory/SFO files. Supply naturally nonzero trailing data or an owner-controlled
      checksum/load/resave trial per [capture 7](../plugins/SuckerPunch/YOTEI_FORMAT.md#exact-owner-captures-for-31).
      Record load acceptance/rejection, native resave/reload/re-export and tail/checksum diff evidence
      on #31, referencing the [deferral](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6489232331).
      Zero tails fit both declared-end and whole-file hypotheses; synthetic tails and same-candidate
      reparsing do not settle game coverage. There is no restore action to preview/apply in this trial.

### PPSA02745 — Tales of Arise

Build notes: base-game slots are the supported evidence source; fields remain Experimental/Inferred.
Record active artifact effects, DLC and build. [Plugin checklist](../plugins/TalesOfArise/OWNER_CHECKLIST.md) · [issue #23](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/23)

**Current scope:** Enabled: gald, existing active-character level/HP/SP, recognized existing consumables and
the F6 quantity/restore actions. Party current/max CP are read-only, Inferred; no CP writes or restoration.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] **Rank 5 — restore consumables (`inventory/restore-imported`).** On a backed-up spare slot,
      capture a no-action control and separate buy/use/zero/reacquire pairs for each eligible ID 1–13.
      Expand Inventory, stage a small reduction with opening/current quantities both 0–15 → select rows
      → Restore imported consumable quantities → Preview every target, skip, alias and warning → Apply
      only the reviewed scope → undo/redo → export/re-import. The documented UI selection/exclusion defect can preview
      document scope: cancel unexpected targets and report/retest after the fix. Load, consume, check
      zero/reacquisition and inferred caps/artifact/DLC effects, native-save/reload/re-export. Record
      IDs/counts, preserved higher quantities and unrelated state with the shared evidence on #31.
- [ ] **CP authority/growth captures (before CP writes).** On backed-up spare copies, capture labelled
      no-action resaves, one CP spend, one rest/refill and one level-up as independent before/after pairs;
      keep Orange Gel refill and Astral Flower/other bonus changes separate. In Party, record read-only
      `party/current-cp` / `party/max-cp` alongside photographed menu current/max CP, active party, levels,
      HP and consumables before/after, then native-save/reload/re-export and diff against the control.
      There is no CP edit or Preview/Apply step. Report authority, refill and maximum-growth observations
      with the shared evidence on #31 and the [CP handoff](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6489330493);
      equal sample values and backup twins cannot establish semantics or a growth formula.
- [ ] Validate gald buy/use, then level/HP/SP separately for each relevant active character; inspect normal
      progression, effective stats and untouched preview/dependent caches.
- [ ] Validate recognized consumable IDs 1–13 at quantities 0–15, including zero followed by normal
      reacquisition. Test the reviewed selected/document/query quantity action and effective native 15/99
      caps with artifact/DLC state; no insertion or stack-cap override is implemented.
- [ ] Buy one Apple Gel; consume one separately.
- [ ] Lose HP in one fight without leveling; heal separately.
- [ ] Gain SP without spending; purchase one named skill separately.
- [ ] Gain one level; use one herb separately.
- [ ] Recruit one character; change party order separately.
- [ ] Capture CP heal, rest, orange-gel use and astral-flower acquisition separately.
- [ ] Obtain one named ingredient, crafting material, herb, fishing item and key item separately.
- [ ] Equip and unequip one owned weapon and armor per character.
- [ ] Obtain two same-type accessories with different ranks/abilities; equip each, enhance one and
      transfer one ability in separate pairs.
- [ ] Capture empty/full accessory pools and common inventory; obtain a zero-quantity item and
      obtain it again after setting its existing quantity to zero.
- [ ] Capture later key-item, accessory-material/chest, crafting and acquisition after edited-stack
      save/reload.
- [ ] Use one identified arte; capture a proficiency threshold and auto-learned arte separately.
- [ ] Unlock one title without buying nodes.
- [ ] Buy one paid title node and the final node of one panel separately.
- [ ] Capture one DLC title/arte separately.
- [ ] Find one owl; report it to the Owl King; claim its reward, all as separate pairs.
- [ ] Capture one quest objective, stage transition, completion and reward claim separately.
- [ ] Learn one recipe; cook it first time and repeat-cook separately.
- [ ] Catch one fish species; repeat it; claim Fishing Expert reward separately.
- [ ] Unlock one campsite and one map/gimmick separately.
- [ ] Obtain one artifact; toggle its effect; start NG+ with one carry-over option separately.

### PPSA01603 — Hogwarts Legacy

Build notes: the plugin decodes UE4.27.2/package 524 GVAS and 30 SQLite images; HL0010 stored-frame
reconstruction grows to 8.79 MB, below the 16 MiB editor cap but with no proven PS5 allocation or acceptance. [Plugin checklist](../plugins/UnrealGvas/HOGWARTS.md) · [issue #24](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/24)

**Current scope:** Enabled: bounded nonidentity integer/real/text cells in either decoded database image; no
reward/ownership transaction is inferred.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Validate unchanged no-op output separately from a stored-frame reconstruction; retain both images,
      SaveGameList and companions. Record 16 MiB editor cap versus actual PS5 slot allocation, growth,
      load/restart/reload acceptance and independent re-decoding.
- [ ] Validate one supported cell per image/type on a spare slot; identify its exact row role and all
      dependent counters/rewards. Identity/blob/reference cells and metadata-only/partial image pairs remain
      protected.
- [ ] Buy one item.
- [ ] Sell one item.
- [ ] Earn and spend an exact currency amount separately.
- [ ] Use one potion.
- [ ] Cross one XP/level threshold.
- [ ] Spend one talent point.
- [ ] Equip one gear instance.
- [ ] Pick up one identified collectible.
- [ ] Determine per-event image/row roles, dependent rewards/counters and slot-list changes;
      include houses and ownership states.
- [ ] Establish effective limits and PS5 stored-output/save allocation.

### PPSA02199 — STAR WARS Jedi: Fallen Order

Build notes: Jedi gameplay saves contain zlib actor records and Crc32, but checksum coverage and
console acceptance are unresolved. [Plugin checklist](../plugins/UnrealGvas/README.md) · [issue #24](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/24)

**Current scope:** Enabled: warned existing gameplay scalars; known checksum fields remain read-only and
coverage is unresolved.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Validate one supported scalar at a time on a spare slot, including effect and native
      resave/reload/re-export. Capture checksum behavior and actor/property relationships; successful
      reparse does not establish acceptance.
- [ ] Earn and spend one currency separately.
- [ ] Capture one skill change.
- [ ] Capture one world change.
- [ ] Capture one cosmetic change.
- [ ] Supply at least three states per hypothesis, with build and action notes.

### PPSA01288 — Sackboy: A Big Adventure

Build notes: General is UE4.25.1 tagged progression with CollectabellsStored and Checksum; image
sets are opaque compressed textures. General scalar writes are enabled with warnings. [Plugin checklist](../plugins/UnrealGvas/README.md) · [issue #24](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/24)

**Current scope:** Enabled: warned General existing scalar writes; known Checksum stays read-only.
Compressed image sets remain opaque.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Validate a small supported General scalar edit, inspect Collectabells/menu/world effects,
      native-save/reload/re-export all companions and report rejection/normalization. Investigate checksum
      coverage separately; no photo writer is claimed.
- [ ] Earn one Collectabell and spend one Collectabell separately.
- [ ] Capture one costume change.
- [ ] Capture one world change.
- [ ] Supply at least three states and build/action notes.
- [ ] Capture one in-game photo and its matching screenshot, recording dimensions, orientation and colors.

### PPSA04018 — The Witcher 3 (Complete Edition)

Build notes: supplied PS5 saves use observed SAV3 schema 64/23/163; that tuple is not a known patch,
and broader current-patch/DLC/NG ownership is unknown. [Plugin checklist](../plugins/Witcher3/EVIDENCE.md) · [issue #25](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/25)

**Current scope:** Enabled: original progression/Crowns/quantities plus durability, existing non-core
ranks/Gwent copies, reviewed repair and imported food/water refill.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] **Rank 11 — ordinary Gwent copies (`gwent/set-ordinary-copies`).** On backed-up spare copies,
      capture a no-action control and independent ordinary-copy gain/removal pairs. In the Gwent table,
      select saved ordinary card rows → Set ordinary Gwent counts (review) → enter a small shared target
      → Preview old/new copies within each opening bound, leader/unmapped/missing-pack skips and warnings
      → Apply → undo/redo → export/re-import. Separately test document scope and supported filtered
      all-matching/excluded rows. Load, check collection, decks and a match plus quest/reward/achievement
      behavior, then native-save/reload/re-export. Record card indices/counts, deck invalidation on
      decreases and unrelated state with the shared evidence on #31; no new cards/decks/rewards are created.
- [ ] Validate original level/XP/free points/Crowns/stack fields separately; then one damaged sword
      durability and two-item repair. Inspect combat, unrelated inventory, Crowns/repair rewards and area
      transitions.
- [ ] Test non-core rank increase/decrease independently; inspect free/spent/path budgets, prerequisites,
      slots, modifiers/stats and combat. Test ordinary Gwent duplicate count and leader removal/restoration
      separately, including deck/match/quest/achievement behavior.
- [ ] Provide two ordinary unequipped/unslotted/untagged Bread/Bottled water stacks; lower/refill
      individually and together, then document/query scopes with an exclusion. Verify protected
      equipped/quest/currency rows and already-higher quantities stay unchanged; inspect consumption/slots
      and whole-action undo.
- [ ] Capture mutation research/activation/respec costs, points/mutagens and effects; native skill
      purchase/equip and prerequisite arrows. For maps capture discovery, travel use, Place-of-Power
      visit/consume and site completion separately, with native region/names and matching tag resources.
- [ ] Capture add/delete/split/merge, displaced combat/Roach equipment, stash, sockets/enchants and quest
      cutoff/branch/objective/reward/world-revisit separately. Safe identity cleanup and related-system
      writers are unimplemented.
- [ ] Buy one Bread, consume one Bread, and drop/pick up one ordinary stack separately; include a
      near-cap stack.
- [ ] Repair one damaged steel sword; record durability and Crowns.
- [ ] Move one stack into and out of stash separately.
- [ ] Equip and unequip one Roach item separately.
- [ ] Equip and unequip one ordinary combat item separately.
- [ ] Earn XP across one level.
- [ ] Buy one non-core skill rank.
- [ ] Equip and unequip that skill separately.
- [ ] Buy/win one Gwent card, change one deck, learn one recipe and discover one signpost separately.
- [ ] Supply mid/endgame/current-patch, Hearts of Stone/Blood and Wine, NG+, Ciri, large-stash and
      socketed/enchanted-gear samples.

### PPSA16064 — Minecraft Dungeons II / obfuscated JSON

Build notes: the sample is offline, SoftVersion 2, FormatHash 3777917472; GlobalSaveDataDefault is
byte-minus-one JSON; supported offline character and settings edits are enabled with warnings. [Plugin checklist](../plugins/XorJson/README.md) · [issue #26](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/26)

**Current scope:** Enabled: shifted-settings and supported offline-character scalars/structures, F6 value
batches and F7 character/global group edits; external integrity remains Unknown.

- [ ] No-action control and unchanged toolkit-export/recovery cycle for this title/build, with all
      companions and backups.
- [ ] Validate a settings scalar first, then each supported offline hero scalar and one JSON
      add/remove/replace independently. Check gameplay dependencies, effective ranges,
      rejection/clamping/repair and unrelated bytes; valid JSON is not a legal game state.
- [ ] Test F6 selected/document/query value batches, skips/exclusions, empty scope and one-step undo/redo.
      Test F7 with both role members, full-group undo/export and console reload; same generation does not
      prove a shared gameplay invariant.
- [ ] Validate attribute/stack/item/rarity/effect/enchantment actions only on supported existing records,
      one family at a time; inspect equipment, rewards/world/quest/achievement side effects. Do not broaden
      offline schema evidence to online or other versions.
- [ ] Capture a new offline hero and a progressed offline hero,
      retaining SoftVersion/FormatHash.
- [ ] Capture one currency gain/spend, XP/level gain, item pickup, equip/unequip, rarity/effect
      change, quest step/reward, map/station discovery and master-volume change separately.
- [ ] Include nonempty effects/enchantments, a talisman, merchant/cosmetic rows and several
      progression states.
- [ ] Capture one controlled malformed/altered token; record rejection, clamping, repair or
      external-integrity behavior.
- [ ] Before each picker replacement, capture a natural item change with TypeTags, rarity, power,
      stack, generator seed, EquippedSlot, TargetSlotOverride and effects/templates; include the
      same base item in merchant stock, bag and equipped slots.
- [ ] Capture repeated same-type items, a reorder and equip/unequip to establish seed persistence.
- [ ] Capture quest partial-progress, completion and reward claims separately; record achievement,
      collection, door, station, fog and actor side effects.
- [ ] For character/global coordination, capture both files before/after active-character selection
      and a suspected shared progression action, identifying slot/generation membership.

## Reporting and handoff

For every checked item, record the private archive names/hashes, SaveDiff report path, fixture ID,
exact displayed values, build metadata and console result. Post a redacted result on the linked
plugin issue and [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31); keep saves and
identity data out of git and public reports. A checked box means evidence exists, not that an entire
plugin is Stable. If an event is unavailable in a demo/build, leave it unchecked and state why.
Uncontrolled snapshots, synthetic tests, successful reparsing and unperformed console trials are
not substitutes for a native capture/load/effect/resave/reload cycle.
