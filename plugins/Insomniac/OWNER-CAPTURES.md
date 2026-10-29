# Owner captures and console validation (#20 / #31)

The write-enable round exposes career/spent bolts, seven gold-bolt switches and
existing boolean preferences as Experimental. Not validated on console. The game may
reject or overwrite the edited save. Integrity coverage is unknown. These captures now track
confidence and acceptance under [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31),
not permission to enable those edits. Only the owner can establish acceptance or Stable.

## Protocol for every experiment

Use one PPSA01474 slot on one recorded build/region. Record normal/Challenge Mode,
character, DLC/edition, locale and exact action. Preserve the entire decrypted set
and backup twins privately, including duplicate ZIP ordinals/generations. Never
commit saves, account metadata, extracted assets or patch outputs. Keep a pristine
recovery copy and verify recovery before any write experiment.

Capture BEFORE, perform **one** action, force the same kind of save, then capture
AFTER. Include HUD/menu screenshots and exact displayed numbers. Reload/re-save
once more as AFTER-RELOAD to detect normalization. Name archives
`PPSA01474_<event>_<before|after|after-reload>.zip` and record file hashes.
First obtain two save/load/save controls without gameplay, independently in normal
and Challenge Mode. Identify churn from those controls; never mask unexplained bytes.
Use F1/SaveDiff for complete byte/decoded changes with generation identities.

## Inventory and names

1. **Acquisition:** buy one ordinary weapon absent from the wheel. Record name,
   price, bolts before/after, character and wheel contents. Identify which
   `00D0A9DF` container/GUID/config appears or moves. Do not assign TVNI groups to
   characters or ownership solely from their position.
2. **XP/level:** earn a known XP increment with only that weapon, then separately
   cross one level boundary. Record level, XP/bar, ammo/capacity and mode. Compare
   weapon XP, grid masks, derived stats and career aggregates.
3. **Ammo:** fire one shot, then separately buy/refill ammo. Record loaded and
   checkpoint ammo, type, price and HUD count. Add a death/checkpoint-reload pair
   to distinguish LoadedAmmo from LastCheckpointAmmo.
4. **Upgrades:** earn one raritanium, then separately buy one named non-gold node.
   Record wallet before/after, cost, weapon/level and the entire grid with selected
   row/column highlighted. Repeat for a second node in another row, then an
   unlocked gold node. Record neighbors/prerequisites. Distinguish Mods, sixteen
   ModGrid masks, thirty-two ModCount slots and SelectedCell; no raw bit is yet a
   proven purchased-node identity.
5. **Omega:** on an existing Challenge Mode slot, buy one Omega weapon separately
   from challenge reset. Record cost, level, XP, tree and capacity. All 1,120
   measured weapon/melee records have Omega false; positive encoding and
   mode-dependent caps remain uncalibrated.
6. **Names:** optionally supply a permitted offline config/localization pack with
   build/locale. Join each known config hash/path to display tags, descriptions and
   upgrade configs. Twenty current English labels have inferred localization
   aliases although all twenty path hashes occur in saves.

## Armour collection versus appearance

7. Collect one missing base-game head piece, separately one torso and separately
   one leg piece. Record name/location and set-bonus screen. Compare the
   VanityPickupsCollected bit, per-location count, entitlement and vanity records.
   Obtain a full-set pair before claiming effective bonus percentages.
8. On **each** playable character, equip and unequip one head/torso/leg piece in
   separate pairs without collecting anything. Include appearance and bonus menus.
   Resolve container/GUID/config identities without assuming actor indices.
9. Change one equipped piece's color twice, separately, recording menu label/slot.
   Track ColorId, BeenPreviewed and override payloads. Measured colors include
   33–37; no 0–19 range is assumed.
10. Compare edition/anniversary entitlements separately from base-game acquisition.
    A schema entry does not establish availability or a perk.

## World and collectibles

11. Collect one spybot per location, with **separate** pairs for the two
    Scarstu/Zurkon's spybots. Record exact pickup and map/menu counts to validate
    the inferred ten-bit RYNO ordinal join independently.
12. Collect one documented gold bolt per location. Record guide identity and map
    screenshot/coordinates to join CollectedBoltHashes to instances. Per-location
    counts exist; individual gold-bolt hashes remain unresolved.
13. Make separate one-pickup pairs for a Lorb, Zurpstone and CraiggerBear. Record
    instance, quest stage and map/menu counts; add known-complete examples. An unset
    bit must not be equated with an existing missing pickup.
14. Finish one pocket dimension and one arena challenge separately. Record portal/
    challenge name, reward, mission/objective text and before/after state. Match
    graph/node/global/dimension keys; trophy codes/menu-viewed entries are not proof.
15. Capture Challenge Mode creation separately, then re-save/reload. Record carry-
    over/reset for weapons, armour/bonuses, collectibles, quests/arena and counters
    to establish lifetime versus current-run semantics.

## Acceptance experiments for the enabled writes

16. **Baseline:** export unmodified through the toolkit; confirm matching bytes,
    restore with the complete sibling set, load, play, re-save, reimport and reload.
    Record console/build and recovery outcome.
17. **Bolt edit:** use the plugin's Player / Economy / CareerBolts
    control to add exactly one within
    the stored integer width. Early/late autosaves use UInt16/UInt32 respectively.
    Keep SpentBolts and every unrelated byte unchanged; never resize or use an
    absolute offset. Retain complete byte/semantic diffs. Record load, wallet
    (+1 only if career-minus-spent holds), purchases, re-save/reimport and reload.
    Separately capture one legal purchase and test SpentBolts by one within its
    observed/type range, keeping CareerBolts fixed and a nonnegative balance.
    Confirm the wallet and a subsequent purchase; do not combine the two tests.
18. **Option candidate:** first capture a legal in-game RPG toggle. On a slot with
    at least six gold bolts and RPG unlocked, independently toggle 77F00BE8 /
    RPGEnabled in its existing one-byte bool slot. Keep collectible hashes/counts
    unchanged. Record menu/effect, load, re-save/reimport and second reload.
    Repeat independently for each other unlocked exposed option: infinite health,
    infinite ammo, golden weapons, mirror mode, home-run hitter and confetti
    headshots. Do not infer an unlock from a stored enabled flag.
19. **Preferences:** independently validate an unchanged export, one in-game setting
    pair and a reviewed single-field candidate with observed type/width. Gameplay
    acceptance does not establish preferences acceptance.
20. **F6 calculated-balance target:** on a disposable spare slot, first lower the
    calculated balance by one. Review CareerBolts, unchanged SpentBolts and derived
    balance/expanded diagnostic values; apply, undo, redo and export. Load, inspect
    wallet and a normal purchase, native-save, reload and re-export/diff. Separately
    test zero and a small increase within the shown bounds. Report clamping,
    rejection, ignored effects or history/accounting changes; no wallet calibration
    or body-integrity claim follows from the toolkit preview.
21. **F9 joins:** pair each known spybot pickup with the exact bit, planet and
    physical location; preserve unresolved/reserved bits. For each upgrade purchase
    record its weapon, exact node, grid row/column, cost, available prerequisites
    and before/after masks/counts. Physical map transforms require at least three
    non-collinear known landmarks per build/region/dimension. Current F9 storage
    diagrams and link-only maps deliberately contain no claimed coordinates or
    prerequisites; external map art/coordinates are not imported.

Automated probes and production-write tests run **in memory only**. They establish
single-atom parser acceptance, byte preservation and undo, not complete integrity
or PS5 acceptance. CareerExp has an
eight-byte LE mirror in BC60B495; changing only the campaign value still reparses.
Do not offer XP writes until all dependencies are tested. Acceptance failures need
complete before/candidate/re-saved diffs, not guessed checksum seeds.

The owner decision permits the bounded scalar edits with warnings before these
experiments. Dependency/structure-unsafe edits remain disabled as documented in the
README. Only the owner may set Stable after independent console acceptance.
