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

## Write-enable decisions (2026-10-28)

The owner authorized **“Enable writes. Keep warnings.”** Console evidence is tracked in
[#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31), separately from implementation.
The enabled operations below do not wait for these captures; all remain Experimental/Inferred.
Unknown integrity/dependency rules may make the game reject an edited save.

The complete pinned MIT name catalog is now embedded under the owner's issue #14 policy. No new
permission decision is needed for those lists or properly attributed CC BY-SA text. Catalog labels
do not establish legal stack limits, equip eligibility, upgrade paths or temporal dependencies.

| Family | Current result | Owner validation / remaining implementation |
| --- | --- | --- |
| Held runes | Enabled field and held-only F6 preview/review/undo | Console load/resave/reload; final balance <= history |
| Quantities/refill | Enabled positive existing Rowa Fruit <=12 and Smithing Stone [1] <=3, in held/storage; shared F6 refill | Legal carry/storage caps remain unverified; nonempty storage has synthetic coverage only; validate both IDs and grouped undo |
| Live/menu name | Enabled both UTF-16LE mirrors, 1–16 code units, strict surrogate/control checks, original trailing bytes and exact undo | Unicode/max-length/shortening pairs and menu/reload behavior |
| Attributes/respec | No complete writer: read-only | Missing class-aware modifier/vital transaction; sample 504/504/476 cannot safely become one base value; capture attribute/modifier pairs |
| Linked rune history | Enabled manual history mirrors and F6 linked increase; max(imported history, observed 4427), held additionally <=999999999 | Earn/spend/death/recovery/loss semantics; test raising balance beyond prior history and resaving |
| Owned equipment/loadout | Read-only four-representation/quick/pouch joins | Missing profile-summary mirror/empty/unarmed/unlock/modifier writer; changing only displayed joins would leave inconsistent state |
| Upgrades/affinity/AoW | No complete writer: read-only | Missing compatible PARAM rules and maximum-ever matchmaking-history transaction |
| Structural/F5 and grouped/F7 | No prepared writer | Free handles, growth/count/acquisition and dependent-reference cleanup unmodeled; only one memory.dat is currently modified; backups remain framework-owned |
| Progress/maps/classes | 499 source candidates and ten class references, read-only, with F9 maps/character dependency graph | Raw bits are Inferred; marker status remains Unknown. Verify entities/source IDs independently; no quest edges, pools, world writes or legal class minima claimed |

F6 handles held-only runes, linked history and ordinary refill with shared review/group undo.
The remaining restrictions protect unmodeled dependent structure; owner evidence alone is not their
only missing ingredient. No local preview substitute or hidden item creation is published.

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
- [ ] `hand-selector`: change only the active left-hand slot, then separately only the active
  right-hand slot. Record all six carried weapons and arm style before/after. Capture one-hand ↔
  two-hand separately. This separates seven ChrAsm selectors from inventory/ID mirror updates.
- [ ] `quick-item` / `pouch-item`: equip one already-held ordinary item into one empty slot, then
  unequip it in a separate pair. Record quantity and active quick selector; changing the selector alone
  needs its own pair. Do not consume/reorder the inventory during these captures.
- [ ] `spell-slot`: memorize one known owned spell in one empty slot, then remove it separately;
  record spell name, all memory slots and available memory capacity. The baseline has no equipped
  spells and cannot prove spell-ID or unlock semantics.
- [ ] `talisman-slot`: change one owned talisman in an already unlocked slot, recording all
  current/max HP/FP/stamina and equipment. Supply before/after earning one talisman pouch or memory
  stone as separate events; do not assume key-item quantity alone proves unlocked capacity.
- [ ] `upgrade-one`: upgrade one identified normal weapon +0→+1; record ID-visible name, AoW,
  all duplicate instances and material/rune spending. Supply normal +25 and somber +10 snapshots
  when available; capture somber +0→+1 independently rather than inferring its rules.
- [ ] `upgrade-history`: after one normal/somber upgrade, separately store or discard the highest
  upgraded instance. Record the highest-ever upgrade and the remaining gear. Do not reset a history
  byte simply because the current highest gear changed; report matching-version history observations.
- [ ] `affinity-one` / `ash-one`: change only one identified owned weapon's affinity or Ash of War
  per pair; record upgrade, exact ash, weapon identity, duplicate instances and inventories. Retain
  the original ash/affinity and capture the reverse change separately.
- [ ] `arrows-bolts`: one nonempty arrow stack, one bolt stack, and one equip-slot change per pair.
- [ ] `containers`: provide nonempty cracked-pot/perfume-related stacks and one crafting/use pair
  each. These stay excluded from ordinary quantity-edit assumptions.

## Character and history pairs (ELDENRING-R1/R2/R9/R14)

- [ ] `rename-unicode`: live/menu name before→`Ploxiテスト` (or another recorded non-ASCII name
  of at most 16 UTF-16 code units), with all other character values unchanged; capture both menus.
- [ ] `rename-shorten` / `rename-max`: shorten a longer name to two code units, then separately
  set exactly 16 UTF-16 code units. Where the game permits it, include a non-BMP character and record
  the displayed result. Prove live/profile behavior for the two bytes following the 16-unit buffer;
  upstream labels them padding, while the current parser requires a terminating zero. The enabled
  writer changes the requested string plus its terminator, retains each original tail and rejects
  unpaired surrogates; confirm that behavior survives the game's own resave.
- [ ] `vigor-one`, `mind-one`, `endurance-one`: separately level only the named attribute by one;
  record all eight attributes, displayed level, HP/FP/stamina current/max, class, equipment and effects.
- [ ] `respec`: respec a fixed total level, record all before/after attributes and HP/FP/stamina;
  keep equipment/effects constant and identify class minima observed in the respec screen.
- [ ] `vital-modifier`: capture the same single-attribute change both with and without an identified
  HP-affecting talisman/effect, including damaged current HP where feasible. Baseline HP is
  **504 current / 504 effective max / 476 base max**: resetting all three to the base table is invalid.
- [ ] `runes-earn`: earn a known amount; record exact held balance before/after, source and any
  automatically spent amounts. `runes-spend`: spend once with recorded price and balances.
- [ ] `runes-death`, `runes-recover`: separate captures for death and recovery, each with exact
  observed balances. Capture unrecovered loss separately if available; do not infer lifetime history
  from held-rune balance alone.
- [ ] `multi-slot`: two active slots with different equipment/stacks; demonstrate an action in one
  slot leaves the other's semantic values unchanged.
- [ ] Provide JP/Asia/EU/US and older/current/base/DLC examples with declared versions when available.
  No conversion/repair or cross-platform compatibility is claimed from these reader changes.

## Progress/map validation (ELDENRING-R13/R15/R16)

Completion uses the corrected event start `0x36600` in the one baseline, with 16-byte acquisition
rows and explicit source byte/bit lookups. This is structural evidence, not a discovery/completion
certificate. The named F9 candidates deliberately stay Unknown; the UI exposes the raw bit and
source/event ID separately. Art/coordinates are external links only and require no new reuse permission.

- [ ] `progress-baseline`: on the original character, observe Church of Elleh and The First Step
  in the game's grace menu. Compare `grace:ChurchofElleh` (source event 76100) and the separately
  named candidate for event 76101 in the toolkit; record menu observations and raw bits. Do not
  infer every set bit is a discovered grace from these two checks alone.
- [ ] `grace-one`: pick one currently unlit grace present in the candidate list; record its exact
  name/source event, slot, raw bit and in-game menu before/after activating only that grace. Record
  unavoidable travel/time changes; do not activate another grace in the pair. Compare the field
  `slots/<slot>/progress/grace:<source-symbol>/raw` against the control pair.
- [ ] `boss-one`: capture before/after defeating one exact listed boss, recording source symbol/event
  and observed defeat/reward. Avoid resting at its grace or collecting unrelated items afterward;
  note unavoidable automatic rewards/unlocks. A grace with a boss's name is not the boss flag.
- [ ] `fragment-one`: collect exactly one named map fragment; record the inventory key item,
  source map-control ID, revealed region and raw before/after bit. Compare inventory ownership
  separately from map visibility. Baseline west Limgrave ownership alone does not establish the flag.
- [ ] `map-layer-only`: toggle only surface/underground map display and save/export, with no item
  acquisition, to distinguish `map-control:ShowUnderground` and viewport controls from fragments.
- [ ] `class-observation`: provide an independently identified class and respec-screen starting
  minima; compare the raw archetype byte and new class reference. Source starting tuples are not
  automatically legal minima on every version or a modifier-aware stat model.

- [ ] Separate same-slot pairs for one grace, one summoning pool, one map fragment, one boss and
  one collectible, with the exact entity/menu observed before/after. Do not combine unlocks.
- [ ] Base/DLC blessing changes and selected quest branch/NG+ transitions, with explicit observed
  milestones and mutually exclusive outcomes. Candidate event bytes alone do not prove quest completion.
- [ ] Full/near-full held/storage and acquisition-history boundaries before any structural writer.

## Data rights and version matching (ELDENRING-R3/R11/R21)

- [x] Owner policy: embed the complete ClayAmore MIT name lists with their notice and per-dataset
  attribution; attributed CC BY-SA text may also be embedded with share-alike. Implemented for all
  five name tables. Restricted/unlicensed/NC content remains excluded, including er-save-manager;
  Paramdex/TGA remain research-only until their licence is confirmed. No repeated approval needed.
- [ ] Supply an audited normalized F2 pack with source/author/licence/date/modifications per dataset,
  locale, game/regulation versions, category-qualified IDs, and exact validated caps only where proven.
  Keep technical slot capacity, carry limit, storage limit and shared container capacity distinct.
- [ ] Exact-build pack compatibility uses the 64-character lowercase SHA-256 shown in the regulation
  directory view (hash of allocation `0x1960070..0x1BA007F`). Put that key in `gameBuilds`.
  The bundled build-agnostic identity-only pack claims complete pinned-source coverage, not verified
  caps, DLC completeness or verified records on every region/version.
- [x] Completion exposes named source candidates as optional spoiler content in **World / progress**,
  following the owner directive to adopt F9. It does not publish quest branches, named quest prerequisites
  or verified completion. No further permission decision is required for the attributed MIT subset.

## Console acceptance (ELDENRING-R1)

Validate each enabled operation from its own restorable duplicate, recording exact intended values.
Repeat the load → inspect → quit/resave → toolkit reopen → console reload sequence for each:

- [ ] From a restorable duplicate, record held runes N and choose a small change within the existing
  lifetime-history cap; export with the toolkit and retain the original.
- [ ] Load on the matching PS5 game version; check intended balance, character/slot selection,
  inventory/equipment/menu state and absence of unexpected effects.
- [ ] Quit/save normally, re-export, reopen in the toolkit and confirm the intended value persists.
- [ ] Load that re-saved output again; report title/region/app/regulation/DLC versions and outcome.
  Only the owner can use these observations to decide Stable maturity. Parser tests are insufficient.
- [ ] Repeat the held-rune change through **Review held-rune change**; verify the preview's old/new
  balance, one undo restoring the original export and redo restoring the same proposed output.
- [ ] Set a Unicode name, separately shorten it, then set exactly 16 UTF-16 code units. Check both
  character/menu names, resave/reload and toolkit re-open. Verify one undo restores original bytes.
- [ ] Change an existing held Rowa Fruit stack within 1–12, then an existing Smithing Stone [1]
  stack within 1–3. Validate both menus and quantities; do not create/remove rows or use zero.
- [ ] Supply nonempty storage for each supported ID; edit only its existing quantity within the same
  conservative bound. Confirm held quantities, record counts and equipment remain unchanged.
- [ ] Reduce both supported stacks, use **Refill supported ordinary stacks**, inspect old/new values,
  protected/skipped rows and warnings. Confirm both changes apply together and one undo/redo restores
  the exact pre/post export. Repeat with a filtered selection excluding one supported stack.
- [ ] Change lifetime history within max(imported history, 4427), keeping it >= held runes. Verify both
  copies persist and normal save/reopen never repeatedly adds to history. Record earn/spend/loss behavior.
- [ ] On a save whose history is below the conservative ceiling, use **Review linked rune/history
  increase** to request a balance just above previous history. Inspect the derived history change,
  confirm both copies, and validate one-step undo/redo plus load/resave/reload.

These captures validate the enabled Experimental writes; they are no longer prerequisites for
enabling them. Only an owner can use console results to promote maturity. Future stat/equipment,
upgrade/progress or structural writers still need complete dependency/structure implementations;
their current readers cannot safely be turned into partial writes merely by changing a setter.
