# Owner capture and console-validation guide

This is the single handoff for owners who can make controlled PS5 captures. It turns the
per-plugin requests into one ordered queue. A checked box means that the private before/after
archives, metadata and console observations exist; it does not by itself authorize a write.
Only the owner can promote a feature to Stable.

## Capture protocol (F1)

Use one native PS5 build, slot and character for a pair. Choose the smallest event that changes one
thing: buy one item, use one item, gain one level, equip one piece, or collect one object. Do not
combine a quest step, cutscene, area transition or autosave side effect with the event under study.

1. Record a no-action control first for each build: save, reload, take no action, save in the same
   place and export both snapshots. Differences in this pair are churn and need a recorded reason
   before they can be ignored.
2. Record the game version (Settings → Information), platform, region, DLC, mods/load order,
   difficulty or mode, save slot and character. Record the exact event and the values shown by the
   game before and after. Never put account IDs, PSN names, UIDs, tokens or credentials in a
   manifest, issue or report.
3. Export a decrypted dump with the primary and backup files. Name archives
   TITLE_event_before.zip and TITLE_event_after.zip, for example
   PPSA01325_collect-one-coin_before.zip. Keep them in the private artifacts directory; never
   commit or copy save bytes into this repository. If one archive contains repeated snapshots,
   retain its generations and record which generation is used.
4. For an event pair, export immediately before, perform exactly one action, save in the same slot,
   and export immediately after. Record the displayed value transition, screenshots or a
   transcription, archive SHA-256 values and the capture date.
5. Check the pair with the read-only SaveDiff tool:

       dotnet run --project tools/SaveToolkit.SaveDiff -- list artifacts/TITLE_saves_dec.zip
       dotnet run --project tools/SaveToolkit.SaveDiff -- compare before.zip after.zip --semantic auto
       dotnet run --project tools/SaveToolkit.SaveDiff -- compare artifacts/TITLE_saves_dec.zip artifacts/TITLE_saves_dec.zip --before-gen 0 --after-gen 1 --semantic plugin-id --ignore plugins/Name/fixtures.json
       dotnet run --project tools/SaveToolkit.SaveDiff -- verify plugins/Name/fixtures.json --format json

   Use the same archive on both sides when comparing generations. Pair files by title,
   logical directory, backup flag and path, not by ZIP position. Review changed ranges and semantic
   paths, backup twins and any truncation. Turn only confirmed changes into expected fixture entries.
6. Record the pair in a fixture manifest with title ID, game, build, platform, region, slot,
   character, DLC/mods, event, control status, shown values, archive names and hashes, decoder,
   files, expected changes and an honest confidence note. savediff verify may skip absent private
   archives; a skip is not evidence.
7. Perform the console check for every proposed write: restore a disposable spare-slot copy; load
   it; verify the intended value and relevant side effects; perform the normal next action that
   consumes, equips or advances the value; save in the game; exit; reload; verify the value again;
   export and compare the rewritten files. Record acceptance, clamping, normalization, rejection
   text, unrelated changes and the build/slot. Repeat a second reload when a checklist asks for it.
   Preserve the untouched recovery export.

## Highest value first

These are ordered by the number of existing write families they can unlock for the least play time.
The title sections retain each plugin's more specific prerequisites.

1. Capture the no-action save/reload/save control for every build and title. It identifies churn
   once and makes every later diff trustworthy.
2. Capture one small currency or stack transition (gain, spend, buy, use, sell) and the existing
   value's console load/save/reload check. This calibrates quantities, caps and the simplest
   scalar writers.
3. Capture one existing-item equip/unequip or storage transfer, including the owner, slot and
   displaced item. These pairs unlock identity and dependency checks for inventories and loadouts.
4. Capture one XP/level or point transition, then one skill/perk/upgrade transition. Keep each
   event separate so mirrors, limits and prerequisites are visible.
5. Capture one named collectible, quest/objective or world unlock. These are slower but unlock
   progress views only when the changed record and dependent rewards are identified.
6. Capture full/near-full, duplicate-instance, DLC/NG+/mode and late-game variants only after the
   small pairs above. They establish boundaries and prevent a write that works only on the
   starter save.
7. Run codec/integrity, companion-file, corruption and cross-version experiments only where the
   title section asks for them. They are gates for safe serialization, not substitutes for a
   causal gameplay pair.

## Title checklists

### PPSA18463 — Baldur's Gate 3

Build notes: the supplied PS5 LSMF sample is one independent snapshot with no no-action resave;
build, region and gameplay chronology are unknown. Keep the character/slot/build constant.
[Plugin checklist](../plugins/BaldursGate3/EVIDENCE.md) · [issue #1](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/1)

- [ ] Control: save/export, reload without acting, save/export in the same place.
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
- [ ] Provide permitted build/language/module-matched template, stats and LOCA data with a
      rights manifest; record the icon/commercial and spoiler decisions separately.

Console checks: restore an untouched re-export first and load/resave/re-export it. Test metadata-only
changes separately. For every future live edit, confirm the effect, unrelated characters, rest/trade/
combat where relevant, resave, reload and redump, including failures and warnings.

### PPSA01325 — ASTRO's PLAYROOM

Build notes: the sample has two primary memory.dat generations and identical backup twins, but the
build, region and actions are unknown; no special-bot keys are present. [Plugin checklist](../plugins/AstroPlayroom/README.md) · [issue #11](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/11)

- [ ] No-action save/load/resave control for each build.
- [ ] Gain exactly one coin.
- [ ] Spend exactly 100 coins on one gatcha.
- [ ] Test coin boundary/clamping behavior and photograph displayed values.
- [ ] Make one small independent coin edit on a spare save.
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

Console checks: for each proposed write, load a spare edited save, confirm the intended effect,
play normally, resave, export, reload a second time and record persistence and linked collection,
cinematic, notification and UdsWorkaround effects. Only the owner can approve Stable or a bulk
dependency.

### PPSA10695 — Atomic Heart

Build notes: this plugin handles the custom SaveExtension format, not standard GVAS. The observed
codec is Kraken; inner integrity and duplicated state remain unresolved. Use the exact console build,
region, DLC and slot in every filename. [Plugin checklist](../plugins/AtomicHeart/README.md) · [issue #12](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/12)

- [ ] Label every capture with console build, region, DLC and slot; retain a clean recovery copy.
- [ ] Idle load/save with no gameplay change.
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

Codec/console checks: compare all decoded chunks with an independently reviewed decoder before
enabling writes. If a codec-only stored-frame candidate is separately reviewed, restore it on PS5,
record growth/capacity, load, inspect inventory, travel, save, restart and reload. For each semantic
edit, observe the effect, spend/use it, save/restart/reload and compare unrelated spans.

### PPSA04027 — Cyberpunk 2077

Build notes: the Cyberpunk plugin's sample has no controlled console capture; the early sample has
no allocated modern perks or installed cyberware. Later PS5 versions/regions/DLC need their own
evidence. [Plugin checklist](../plugins/Cyberpunk2077/EVIDENCE.md) · [issue #13](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/13)

- [ ] Idle save/reload/save control.
- [ ] Buy exactly one known ammo/component stack; record item, cost and quantity.
- [ ] Fire/use exactly one unit in a separate pair, including automatic replenishment.
- [ ] Equip one existing weapon or clothing item; capture before, after equip and after unequip.
- [ ] Move one existing item between player and stash.
- [ ] Upgrade one existing item once; record tier, stats and materials.
- [ ] Install one existing cyberware implant.
- [ ] Remove that implant separately; record slot, capacity and bonuses.
- [ ] Validate one supported money value on a spare slot.
- [ ] Validate one supported stack value on a spare slot.
- [ ] Validate each supported attribute value separately.
- [ ] Validate each supported point balance separately.
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

Console checks: restore the spare slot, load, inspect/use/equip, save and reload, then compare
unrelated inventory and progression. Record the exact build and result before promoting Stable.

### PPSA03169 — Elden Ring

Build notes: the PS5 payload is memory.dat with ordinary backup twins; application and regulation/
calibration versions, region and DLC are not established by the sample. Keep one character and
version per pair. [Plugin checklist](../plugins/EldenRing/Evidence/OWNER_CAPTURES.md) · [issue #14](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/14)

- [ ] No-action control, repeated once, recording elapsed time and unchanged menus.
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

Console checks: validate the existing held-rune edit from a restorable duplicate, including balance,
slot/menu state and no unexpected effects; save/re-export/reopen and load the re-saved output again.
Repeat through Review held-rune change and verify preview, undo equality and redo equality. Apply the
same sequence to every future family.

### PPSA10665 — Final Fantasy XVI retail

Build notes: retail and demo use different supported schemas and banks; capture normal/Final Fantasy
NG+, adult/child stage and DLC separately. [Plugin checklist](../plugins/FinalFantasy16/README.md) · [issue #15](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/15)

- [ ] Save/reload/save no-action control.
- [ ] Buy one potion, use one, sell one material and reacquire one allocated zero-stack item,
      each in a separate pair.
- [ ] Capture each pouch capacity/potency upgrade separately.
- [ ] Equip one weapon, belt, vambrace and accessory separately, recording all six UI slots.
- [ ] Include boyhood and stage-replay pairs to identify active banks.
- [ ] Learn, upgrade, master and reset one skill in separate pairs; record AP and Eikon/slot.
- [ ] Unlock one Eikon separately from any story transition.
- [ ] Earn XP without leveling, then cross one level; repeat adult/child and normal/Final Fantasy mode.
- [ ] Supply matched, legally owned PC binary/XML states with build/locale/DLC if available.
- [ ] Edit one existing stack quantity on a spare slot, load, verify, spend/use/sell, save, reload
      and re-export; compare unrelated fields.
- [ ] Supply licence-compatible labels or observed item-label/acquisition pairs for the 41 Unknown IDs.
- [ ] Capture one upgrade each for potency/capacity IDs 506, 507, 508 and 509, with both banks,
      displayed capacities/potencies, quest, mode, DLC, stage, zero-stack control and stage replay.
- [ ] Capture learn, upgrade, master and reset for skill IDs 39, 45 and 49 independently, with
      AP deltas, rank, Eikon assignment, all rank bytes and both flag arrays.

Console checks: restore the edited spare-slot copy, verify UI, spend/use/sell, save, reload and
re-export. CRC success alone is insufficient; record unrelated changes.

### PPSA13803 — Final Fantasy XVI demo

Build notes: demo records never inherit the retail extended player schema. Keep demo evidence and
console checks separate from PPSA10665. Use the same checklist and record demo stage, mode and bank
identity explicitly. [Plugin checklist](../plugins/FinalFantasy16/README.md) · [issue #15](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/15)

- [ ] Capture the demo no-action control.
- [ ] Capture one potion buy/use, material sale and zero-stack reacquisition separately.
- [ ] Capture one weapon, belt, vambrace and accessory equip separately.
- [ ] Capture one skill learn/upgrade/master/reset and one Eikon unlock separately.
- [ ] Capture one no-level XP gain and one level crossing.
- [ ] Capture the demo-specific 506/507/508/509 capacity/potency transitions with stage and bank.
- [ ] Capture demo-specific skill IDs 39/45/49 learn/upgrade/master/reset with exact flags.

Console checks: apply the demo spare-slot sequence independently; do not transfer retail meanings or
claims to the demo.

### PPSA01911 — Final Fantasy VII Remake / INTERmission

Build notes: Remake main and INTERmission are separate roles under the same title ID. The supplied
samples show writable gil, occupied quantities and ordinary materia AP, but no PS5 console test.
[Plugin checklist](../plugins/FinalFantasy7/README.md) · [issue #16](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/16)

- [ ] Capture one XP level-up.
- [ ] Capture one materia AP star-up.
- [ ] Capture one gear swap.
- [ ] Capture one materia move.
- [ ] Capture an edited save load/reload/resave pair.
- [ ] Repeat the pairs for Remake main and INTERmission separately, recording chapter/difficulty.
- [ ] Capture one card acquisition, opponent win, deck change, crafting-rank gain, folio-node
      purchase, stop repair and World Intel step separately.
- [ ] Capture capacity/creation/deletion boundaries: buy two identical materia, equip/swap/sell
      one, acquire/discard one weapon/card and reach a full inventory.
- [ ] Supply known-state party/gear/materia/card screenshots paired to sample identities.

Console checks: load each edited spare slot, check menus/effect, play a battle, resave, import again
and reload. Confirm dependent inventory/detail/owner bytes are preserved.

### PPSA08664 — Final Fantasy VII Rebirth retail

Build notes: Rebirth retail has its own linked-record schema and 23 MiB gameplay file; it is not
interchangeable with Remake or the demo. [Plugin checklist](../plugins/FinalFantasy7/README.md) · [issue #16](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/16)

- [ ] Capture one XP level-up.
- [ ] Capture one materia AP star-up.
- [ ] Capture one gear swap and one materia move separately.
- [ ] Capture one edited save load/reload/resave pair.
- [ ] Capture the card, opponent, deck, crafting, folio, stop and World Intel events separately.
- [ ] Capture capacity/creation/deletion and full-inventory boundaries, including duplicate materia.
- [ ] Supply screenshots for Rebirth guests, past records, summon levels and owned relationships.

Console checks: use a Rebirth spare slot, include a battle, resave and second import/reload, and
confirm no dependent owner/detail bytes are lost.

### PPSA18936 — Final Fantasy VII Rebirth demo

Build notes: demo and retail use distinct schema identities despite matching dimensions. Demo
meanings remain Inferred and gameplay is read-only until its own evidence exists. [Plugin checklist](../plugins/FinalFantasy7/README.md) · [issue #16](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/16)

- [ ] Capture one XP level-up.
- [ ] Capture one materia AP star-up.
- [ ] Capture one gear swap and one materia move separately.
- [ ] Capture one edited demo save load/reload/resave pair.
- [ ] Capture demo card/progression events only when they actually exist in the demo.

Console checks: validate the demo independently; no retail mapping or write is inherited from
PPSA08664.

### PPSA01463 — Borderlands 3

Build notes: BL3 uses GVAS with engine 4.20.3 and branch OAK; patch, DLC and hotfix completeness
are unknown, and serial identity must be retained. [Plugin checklist](../plugins/Gearbox/README.md) · [issue #17](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/17)

- [ ] No-action save/reload/save control with character, matching profile and backup.
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

Console checks: restore a backed-up spare slot, confirm visible effect and unrelated inventory,
resave, reload and return the dump and observations. Report clamping.

### PPSA01493 — Tiny Tina's Wonderlands

Build notes: TTWL has a separate schema and XP/leveling policy; current characters are level 12.
Supply level-40/endgame gear, nonzero rerolls and each Chaos tier. [Plugin checklist](../plugins/Gearbox/README.md) · [issue #17](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/17)

- [ ] No-action control with character, matching profile and backup.
- [ ] Capture one pickup, sale/removal, equip/unequip, favourite/junk, backpack↔bank move,
      lost-loot claim and reroll separately.
- [ ] Include duplicate and full/near-full containers and transcribed item cards.
- [ ] Supply level-40/endgame items with nonzero rerolls at each observed Chaos tier.
- [ ] Gain one level, allocate/respec one skill, purchase one SDU and refill ammo separately.
- [ ] Capture each indexed Myth allocation, rotation and capped stat with matched profile.

Console checks: use the same spare-slot load/effect/resave/reload sequence; report clamping and
unrelated character/profile changes.

### PPSA01494 — Borderlands 4

Build notes: BL4 is research-only, with one 1,888-byte gbxsavegame.sav occurrence, no backup and
no established PS5 codec or file role. [BL4 checklist](../plugins/Gearbox/BL4-feasibility.md) · [issue #17](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/17)

- [ ] Supply complete fresh-profile, settings-only-toggle and first-gameplay dumps with every file
      and available backup; record files appearing, vanishing or changing.
- [ ] Capture a no-action save/reload/save control.
- [ ] Capture one known money change.
- [ ] Capture one legitimate item pickup with matching profile/settings files.
- [ ] Establish an owner-local PS5 derivation route without sharing credentials, keys or secrets.
- [ ] After role/codec proof, test padding, compression boundaries, trailer lengths, unknown-token
      retention, corruption rejection and byte-identical full-file no-op output.

Console checks: BL4 remains read-only until a separately implemented write family passes load,
effect, resave, reload and comparison on a backed-up spare slot.

### PPSA02343 — It Takes Two (EU) settings/profile

Build notes: the sample has three distinct JSON profile generations (3,993/3,522/1,312 bytes)
with byte-identical backup twins; archive order is not chronology. [Plugin checklist](../plugins/GenericJson/README.md) · [issue #18](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/18)

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

Console checks: restart, resave and re-export every proposed edit; compare unrelated state and
companions. Do not promise trophy, cross-platform or arbitrary raw-edit compatibility.

### PPSA07696 — Ridge Racer 2 (PSP classic)

Build notes: GenericJson covers the 1,320-byte imgui_emu_option.json wrapper; DATA.BIN gameplay
is separate and PS1 preset meanings do not establish PSP values. [Plugin checklist](../plugins/GenericJson/README.md) · [issue #18](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/18)

- [ ] Capture each filter preset before/after.
- [ ] Capture each upscale preset before/after.
- [ ] Capture each anti-aliasing preset before/after.
- [ ] Capture one controller option.
- [ ] Capture one rewind option.
- [ ] Record the PSP wrapper build for every pair.

Console checks: restore, restart and re-export to establish numeric enum domains before exposing
named presets; do not transfer PS1 meanings.

### PPSA08330 — God of War Ragnarök

Build notes: only PPSA08330 has sample coverage; the plugin catalogs other regional IDs and has no
Valhalla sample. Nested grammar and integrity remain unproven. [Plugin checklist](../plugins/GodOfWarRagnarok/README.md) · [issue #19](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/19)

- [ ] Change exactly one Hacksilver/resource quantity.
- [ ] Change one XP wallet.
- [ ] Make one gear upgrade.
- [ ] Change one quest flag.
- [ ] Change one collectible.
- [ ] Change one user-preferences setting.
- [ ] Supply one late/Valhalla save and two additional regional PS5 IDs.
- [ ] Supply paired PS4 and PC saves for the same gameplay state where legally available.
- [ ] Preserve the originals and record load/reject text and in-game values for every pair.

Console checks: trial any resulting edit only on a restored spare slot, record in-game values and
rejection text, then save/reload and keep maturity ReadOnly until owner approval.

### PPSA01474 — Ratchet & Clank: Rift Apart

Build notes: the plugin is ReadOnly for decrypted PPSA01474. Use one recorded build/region, normal
and Challenge Mode controls, all sibling files and backup twins. [Plugin checklist](../plugins/Insomniac/OWNER-CAPTURES.md) · [issue #20](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/20)

- [ ] Capture two normal and two Challenge no-action save/load/save controls.
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
- [ ] Export unmodified through the toolkit and verify complete sibling-set recovery.
- [ ] Validate the descriptor-derived CareerBolts +1 candidate at its observed width.
- [ ] Capture a legal RPG toggle, then validate the existing RPGEnabled one-byte candidate on a
      slot with at least six gold bolts.
- [ ] Validate preferences with an unchanged export, one in-game setting pair and one reviewed
      single-field candidate.

Console checks: use BEFORE/AFTER/AFTER-RELOAD, normal and Challenge controls, HUD/menu evidence,
full private sibling sets and SaveDiff. Do not offer XP writes until mirror/dependency integrity is
proven; only specific accepted paths may become Experimental.

### PPSA02530 — Pragmata

Build notes: all supplied samples are fixed-size DSSS/Mandarin and uncontrolled; no causal runtime
or console evidence exists. Keep system and gameplay directories together. [Plugin checklist](../plugins/Pragmata/evidence/README.md) · [issue #21](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/21)

- [ ] Record title ID, build, region, DLC, difficulty/mode, slot and displayed values.
- [ ] Capture an unchanged save/re-save control.
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
- [ ] Supply licensed ID/name evidence or a user-owned extraction pack.

Console checks: restore/export unchanged, load, resave and reload; then repeat with one small
existing-quantity edit and verify its displayed effect. Progression/structural tests wait for mappings
and a writer.

### PPSA02225 — Ghost of Tsushima (Director's Cut)

Build notes: Tsushima uses container versions 47–49 and has existing quantity edits, but none are
console-tested. Keep campaign/Legends, NG+/Iki, DLC and locale separate. [Plugin checklist](../plugins/SuckerPunch/README.md) · [issue #22](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/22)

- [ ] Unchanged load/save control.
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
- [ ] Validate one supported quantity edit through load, observe, consume/buy, resave, reload/export.

Console checks: repeat the quantity edit for base, NG+, Iki and Legends before claiming those modes.
Record clamping/normalization and every changed span.

### PPSA26344 — Ghost of Yōtei

Build notes: Yōtei is opaque and read-only; supplied files include non-empty and all-zero patterns
but no authoritative gameplay record or checksum. [Plugin checklist](../plugins/SuckerPunch/README.md) · [issue #22](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/22)

- [ ] Capture one nonzero Mon/resource/ammo single-action pair.
- [ ] Capture manual-save, autosave and memory role controls.
- [ ] Capture natural nonzero trailing bytes or an owner-controlled integrity/load/resave experiment.

Console checks: resolve checksum coverage and authoritative records before considering any write.

### PPSA02745 — Tales of Arise

Build notes: base-game slots are the supported evidence source; fields remain Experimental/Inferred.
Record active artifact effects, DLC and build. [Plugin checklist](../plugins/TalesOfArise/OWNER_CHECKLIST.md) · [issue #23](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/23)

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
- [ ] Validate 15-stack and effective 99-stack states separately with capacity artifact/DLC state.
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

Console checks: restore an edited spare-slot copy, verify intended and unrelated menus, perform the
next normal acquisition/crafting/progression event, save, reload and record expected/actual results.
Supply mid/late and NG+ samples; other builds/regions and Beyond the Dawn need independent evidence.

### PPSA01603 — Hogwarts Legacy

Build notes: the plugin decodes UE4.27.2/package 524 GVAS and 30 SQLite images; HL0010 stored-frame
reconstruction grows to 8.79 MB, with no slot-budget or console acceptance claim. [Plugin checklist](../plugins/UnrealGvas/HOGWARTS.md) · [issue #24](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/24)

- [ ] Unchanged load/re-save control preserving both database images, SaveGameList and companions.
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

Console checks: validate unchanged reconstruction, then each approved edit on a backed-up spare
slot: import, load, observe, save in game, exit, reload, verify, export and integrity/re-decode.
Retain a byte-identical unmodified path and independently re-decodable output.

### PPSA02199 — STAR WARS Jedi: Fallen Order

Build notes: Jedi gameplay saves contain zlib actor records and Crc32, but checksum coverage and
console acceptance are unresolved. [Plugin checklist](../plugins/UnrealGvas/README.md) · [issue #24](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/24)

- [ ] Unchanged resave pair with primary and backup retained independently.
- [ ] Earn and spend one currency separately.
- [ ] Capture one skill change.
- [ ] Capture one world change.
- [ ] Capture one cosmetic change.
- [ ] Supply at least three states per hypothesis, with build and action notes.

Console checks: resolve checksum coverage, then load a backed-up spare, observe the intended value
and side effects, save, exit, reload and restore if needed.

### PPSA01288 — Sackboy: A Big Adventure

Build notes: General is UE4.25.1 tagged progression with CollectabellsStored and Checksum; image
sets are opaque compressed textures. General writes remain disabled. [Plugin checklist](../plugins/UnrealGvas/README.md) · [issue #24](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/24)

- [ ] Unchanged resave with General and relevant companions.
- [ ] Earn one Collectabell and spend one Collectabell separately.
- [ ] Capture one costume change.
- [ ] Capture one world change.
- [ ] Supply at least three states and build/action notes.
- [ ] Capture one in-game photo and its matching screenshot, recording dimensions, orientation and colors.

Console checks: preserve General, companions and backups; validate checksum and load/reload before
any write or photo interpretation.

### PPSA04018 — The Witcher 3 (Complete Edition)

Build notes: supplied PS5 saves use observed SAV3 schema 64/23/163; that tuple is not a known patch,
and broader current-patch/DLC/NG ownership is unknown. [Plugin checklist](../plugins/Witcher3/EVIDENCE.md) · [issue #25](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/25)

- [ ] Save/reload/save control with title ID, patch, tuple, DLC, NG/NG+, mods and companions.
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

Console checks: restore a spare slot, verify existing level/XP/ability/Crowns/stack edits and
unrelated inventory/equipment/quests, transition areas, check combat, save natively, reload and
return the decrypted re-export. Story presets require their own branch/objective and world revisit.

### PPSA16064 — Minecraft Dungeons II / obfuscated JSON

Build notes: the sample is offline, SoftVersion 2, FormatHash 3777917472; GlobalSaveDataDefault is
byte-minus-one JSON and the character remains read-only. [Plugin checklist](../plugins/XorJson/README.md) · [issue #26](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/26)

- [ ] Record title/content ID, region, exact build, DLC, offline/online mode, slot and cloud-sync.
- [ ] Capture no-intentional-change control, a new offline hero and a progressed offline hero,
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
- [ ] Capture nonempty effect/enchantment rows and quest partial-progress, completion and reward
      claims separately; record achievement, collection, door, station, fog and actor side effects.
- [ ] For character/global coordination, capture both files before/after active-character selection
      and a suspected shared progression action, identifying slot/generation membership.

Console checks: validate a settings scalar first (import, load, verify displayed value, save,
reload, recapture and compare unrelated fields). Validate each hero action only after its mappings
and dependencies are established; bulk actions require individual edits, limits, F6 preview,
atomic apply and one undo.

## Reporting and handoff

For every checked item, attach the private archive names and hashes, the SaveDiff report path,
fixture ID, displayed before/after values, build metadata and console result to the plugin's own
checklist. Keep identity data and saves out of git. If a pair is impossible on the current build,
leave it unchecked and state why; an uncontrolled snapshot is not a substitute.
