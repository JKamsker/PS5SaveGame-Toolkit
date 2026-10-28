# Tales of Arise: owner captures and console qualification

Round 2, 2026-10-28. Existing fields remain Experimental/Inferred. Catalog coverage
does not establish a saved ID, availability, ownership, learnt state or safe edit.
The new character, arte, skill and progress surfaces are read-only.

For **each pair below**, use one PPSA02745 base-game slot and build. Capture before,
an unchanged control re-save, and after exactly one listed event. Record build,
region, DLC/active artifact effects, slot, event and exact displayed old/new values.
Keep decrypted archives outside git, using the capture protocol in `docs/SAMPLES.md`.
Do not record account identifiers, PSN names or credentials in shared notes.

## Existing edits and character identity

- [ ] Nonzero gald: buy one Apple Gel; record price, gald before/after, quantity
  before/after, and item menu name. Then consume one in a separate pair.
- [ ] Lose HP in one fight without levelling; record all displayed HP/max HP and
  equipment. Heal once in a separate pair. Re-save and reload after each event.
- [ ] Gain SP without spending; purchase exactly one skill in a separate pair,
  noting character, title, node, cost and unspent SP before/after.
- [ ] Gain exactly one level; record XP, level, base/effective HP and every displayed
  stat. Capture one herb use separately. Do not infer growth dependencies from zeros.
- [ ] Recruit one character, then change party order separately. Record active/reserve
  membership. Six preinitialized level-5 blocks do not establish recruitment.
- [ ] CP heal, rest, orange-gel use and astral-flower acquisition: separate pairs with
  current/max CP, level, equipment and active artifact bonuses recorded.

## Catalog-to-save joins, equipment and capacity

- [ ] Obtain one named ingredient, crafting material, herb, fishing item and key item
  in separate pairs. Record exact item name, quantity, obtained/new marker and order.
  Wiki ordering and symbolic keys must never be used as numeric saved IDs.
- [ ] Equip and unequip one owned weapon and armor per character, in separate pairs.
  Capture a spare slot before/after changing only the equipped identity.
- [ ] Obtain two accessories of the same type with different rank/abilities, equip
  each separately, enhance one and transfer one ability in separate pairs. Record
  instance distinctions, rank, ability slots, source consumption and costs.
- [ ] Capture empty/full accessory pools and common inventory. Obtain an item at
  zero quantity and obtain it again after editing its existing quantity to zero.
- [ ] Validate normal 15-stack and effective 99-stack states separately, with the
  capacity artifact/DLC state documented. Test later key-item pickup, accessory
  material/chest pickup, crafting and acquisition after edited-stack save/reload.

## Artes, titles and skills

- [ ] Use exactly one identified arte once; record its usage counter and proficiency
  before/after. Capture a proficiency threshold separately, including auto-learned arte.
- [ ] Unlock one title without purchasing nodes; record title name and requirements.
- [ ] Buy one paid node, then the final node of one panel separately; record SP,
  skill name, node position, title and panel bonus. Distinguish initially granted
  nodes (`-` in published SP tables) from paid or explicitly zero-cost nodes.
- [ ] Record a DLC title/arte separately from base-game learning; catalog presence
  does not establish entitlement, learnability or Beyond the Dawn save support.

No purchase, refund, respec, unlock-all, equipment swap, refill or preset action is
enabled this round. Any future bulk mutation must implement the SDK's **F6 planning**
contract (`IActionPlanningDocument`, `SupportsPlanning`): show changes, costs,
dependencies, skipped unsupported/key/story rows and warnings; revalidate at apply;
undo the whole batch as one step. F8 query selections must use Core's resolved scope.

## Progress: separate flags and rewards

- [ ] Find exactly one owl; report it to the Owl King in a separate pair; claim the
  reward in another pair. Include displayed count, location and reward identity.
  Include the postgame/special owl prerequisites before interpreting artifact rewards.
- [ ] One quest objective, stage transition, completion and reward claim: four
  independent pairs, naming the quest, objective text, scenario prerequisite and reward.
- [ ] Learn one recipe, then cook it the first time in a separate pair; repeat cooking
  separately. Record recipe, ingredients, cook, active effect and knowledge/menu state.
- [ ] Catch one new fish species, repeat it, then claim the Fishing Expert reward:
  separate pairs with species, Fishing Notes and reward recorded.
- [ ] Unlock one campsite and one map/gimmick separately, with named locations and
  control re-saves. Raw nonzero words are not counts or completion flags.
- [ ] Obtain one artifact, toggle its effect, then start NG+ selecting one carry-over
  option: separate captures. Record ownership, active effect, carry-over and all
  displayed score/records separately. No editable Grade currency is established.

## Console qualification and schema coverage

- [ ] For each enabled feature family, restore an edited **spare-slot copy**, load,
  verify the intended effect and unrelated menus, perform the next normal acquisition,
  crafting/learning/progression event, save, reload, and record expected/actual results.
  Byte-identical and edit/reopen tests cannot replace this console check.
- [ ] Supply labelled mid/late base-game and NG+ saves to test nonzero records and
  dependent state. Other regions/builds and Beyond the Dawn each need independent
  no-op, corruption, identity and console evidence; preserve system siblings separately.
- [ ] Validate whether the intentionally preserved load-screen preview affects only
  display or gameplay loading. Do not patch it or opaque `SystemSaveData.sav` by analogy.
- [ ] Only the owner may promote a feature or plugin to Stable after the recorded tests.
