# Owner capture and validation checklist

Use a restorable spare save and retain originals. For **each pair** record PS5 title ID/region,
application version, displayed regulation/calibration version, DLC installation/ownership, character
slot and menu observations. Export decrypted `memory.dat` plus its ordinary backup using the same
method. Use filenames `PPSA03169_<event>_before.zip` and `_after.zip` (substitute your actual title).
Record quantities/levels/instance upgrades/AoW in text or menu captures; never include credentials,
account IDs or multiplayer/group passwords in notes or reports.

Do one gameplay action per pair; keep equipment, buffs, nearby events and menus constant except
the action under study. Quit/save normally, export, and record any unavoidable timer/rune changes.
Primary/backup twins do **not** replace an independent before/after pair. Controlled no-action pairs
are needed before classifying timer/settings differences as churn.

## First pairs (ELDENRING-R2/R12)

- [ ] `control`: export A, load the same character, make no gameplay/menu changes, quit/save,
  export B; repeat once. Record elapsed time and unchanged menus. This establishes save noise.
- [ ] `buy-one-dagger`: inventory menu showing Throwing Dagger count N; buy exactly one;
  menu showing N+1; quit/save/export. Record rune balance and price too.
- [ ] `use-one-dagger`: start with at least two Throwing Daggers; use exactly one; record N→N−1
  and equipment/quick-slot state. Separately capture using the final item N=1→0 to identify removal.
- [ ] `storage-in`: with an observed Rowa Fruit stack (e.g. held 12/storage 0), move exactly five
  into storage (held 7/storage 5), recording both screens. Keep all other stacks unchanged.
- [ ] `storage-out`: move those same five back (held 7→12/storage 5→0). A nonempty storage
  snapshot is required even if starting counts differ from the example.
- [ ] `equip-talisman`: record HP current/effective maximum and Vigor, equip one owned HP-affecting
  talisman, record the same values and all talisman slots. Capture unequip as a separate pair.
- [ ] `equip-weapon`: move one already owned Scimitar into one empty hand slot without upgrading,
  changing AoW or adding inventory. Record hand/active-slot UI; unequip as another pair.
- [ ] `duplicate-weapons`: provide two same-base weapons with distinct upgrades/AoW, identifying
  each in menus. Capture swapping exactly those two owned instances, with no acquisition/removal.
- [ ] `upgrade-one`: upgrade one identified normal weapon +0→+1; record ID-visible name, AoW,
  all duplicate instances and material/rune spending. Supply normal +25 and somber +10 snapshots
  when available; capture somber +0→+1 independently rather than inferring its rules.
- [ ] `arrows-bolts`: one nonempty arrow stack, one bolt stack, and one equip-slot change per pair.
- [ ] `containers`: provide nonempty cracked-pot/perfume-related stacks and one crafting/use pair
  each. These stay excluded from ordinary quantity-edit assumptions.

## Character and history pairs (ELDENRING-R1/R2/R9/R14)

- [ ] `rename-unicode`: live/menu name before→`Ploxiテスト` (or another recorded non-ASCII name
  of at most 16 UTF-16 code units), with all other character values unchanged; capture both menus.
- [ ] `vigor-one`, `mind-one`, `endurance-one`: separately level only the named attribute by one;
  record all eight attributes, displayed level, HP/FP/stamina current/max, class, equipment and effects.
- [ ] `respec`: respec a fixed total level, record all before/after attributes and HP/FP/stamina;
  keep equipment/effects constant and identify class minima observed in the respec screen.
- [ ] `runes-earn`: earn a known amount; record exact held balance before/after, source and any
  automatically spent amounts. `runes-spend`: spend once with recorded price and balances.
- [ ] `runes-death`, `runes-recover`: separate captures for death and recovery, each with exact
  observed balances. Capture unrecovered loss separately if available; do not infer lifetime history
  from held-rune balance alone.
- [ ] `multi-slot`: two active slots with different equipment/stacks; demonstrate an action in one
  slot leaves the other's semantic values unchanged.
- [ ] Provide JP/Asia/EU/US and older/current/base/DLC examples with declared versions when available.
  No conversion/repair or cross-platform compatibility is claimed from these reader changes.

## Progress pairs for later phases (ELDENRING-R13)

- [ ] Separate same-slot pairs for one grace, one summoning pool, one map fragment, one boss and
  one collectible, with the exact entity/menu observed before/after. Do not combine unlocks.
- [ ] Base/DLC blessing changes and selected quest branch/NG+ transitions, with explicit observed
  milestones and mutually exclusive outcomes. Candidate event bytes alone do not prove quest completion.
- [ ] Full/near-full held/storage and acquisition-history boundaries before any structural writer.

## Data rights and version matching (ELDENRING-R3/R11/R21)

- [ ] Decide the permitted distribution route for **larger** factual catalogues, CC BY-SA prose,
  localized FMG text and icons; obtain permission for restricted/unlicensed upstream content before reuse.
  The shipped original six-item factual crosswalk is not permission to copy those sources.
- [ ] Supply an audited normalized F2 pack with source/author/licence/date/modifications per dataset,
  locale, game/regulation versions, category-qualified IDs, and exact validated caps only where proven.
  Keep technical slot capacity, carry limit, storage limit and shared container capacity distinct.
- [ ] Exact-build pack compatibility uses the 64-character lowercase SHA-256 shown in the regulation
  directory view (hash of allocation `0x1960070..0x1BA007F`). Put that key in `gameBuilds`.
  Build-agnostic identity-only packs must not claim verified caps or DLC coverage.
- [ ] Approve spoiler scope before publishing named quest/boss/progress catalogues. No such catalogue ships.

## Console acceptance (ELDENRING-R1)

This round adds no gameplay edit family. Validate the existing held-rune edit separately:

- [ ] From a restorable duplicate, record held runes N and choose a small change within the existing
  lifetime-history cap; export with the toolkit and retain the original.
- [ ] Load on the matching PS5 game version; check intended balance, character/slot selection,
  inventory/equipment/menu state and absence of unexpected effects.
- [ ] Quit/save normally, re-export, reopen in the toolkit and confirm the intended value persists.
- [ ] Load that re-saved output again; report title/region/app/regulation/DLC versions and outcome.
  Only the owner can use these observations to decide Stable maturity. Parser tests are insufficient.

Any future quantity/name/stat/equipment edit family needs the same load → inspect → resave → reopen
→ reload sequence, exact changed-range review and undo equality before it is enabled. Phases 3–6
remain gated; none of these checklist requests grants permission to guess those writes.
