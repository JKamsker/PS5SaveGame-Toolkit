# Owner evidence checklist — write-enable round

The owner authorized enabling writes with warnings on 2026-10-28. Existing decoded counters,
play time and F6 restoration are enabled at Experimental maturity; this checklist does not gate them.
Track results in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).
**Integrity unknown — the game may reject this save.** Software edit/reparse/preservation and
undo/redo checks pass, but authentication, gameplay effects and console acceptance remain unproven.

For every pair below, retain the original archive and an untouched backup. Use one fixed PS5 title,
build, slot and DLC configuration; record title ID, build, slot, filenames, archive generation,
event and exact UI before/after values. Do not use archive order as a chronology assumption.

## Enabled-write trials

Use a restored spare slot and retain the untouched original. Run each trial independently from
that baseline. For every trial record load/rejection text, displayed effect, ordinary forward play,
native re-save, a second clean reload, persistence and a decrypted re-export/F1 diff. Include an
untouched native re-save control; keep private identity fields out of shared reports.

- [ ] Export/import the untouched save first and verify load, native save and reload. This checks
  the surrounding restore workflow separately from counter edits.
- [ ] Reduce one existing positive Hacksilver counter by one; compare balance, purchase/earn behavior
  and persistence. Separately reduce Rawhide or another existing resource by one and use/acquire it.
- [ ] Reduce the raw EconomyXP candidate by one. Record all characters' XP/skills before and after;
  verify skill/menu behavior and normal XP gain. Do not assume the field belongs to a specific hero.
- [ ] Test each raw raven counter separately, reducing a positive counter by one. Record journal,
  collected-raven state, reward eligibility and previously claimed chests; then native save/reload.
  No PS5 completion formula or reward repair is implemented.
- [ ] Test any selected unmapped-ID counter separately on a disposable slot, identifying the exact
  container/row ID and u32 value. Record whether any effect can be identified; do not label it a
  known item/quest without evidence. If a non-hero counter is positive, repeat independently there.
- [ ] Reduce play time by one second, check slot/menu time, play briefly, native save and reload.
  Verify seconds versus other units and whether any other timer is rebuilt.
- [ ] Reduce two existing positive counters in one toolkit document, preview the F6 restore action
  for those selected rows, inspect old/new values, skipped rows and integrity warnings, then apply.
  Verify one Undo restores both reduced values and one Redo restores both imported values. Export
  both states independently from backups and perform the full console trial for each.
- [ ] Repeat restore for a filtered F8 selection with an excluded row; ensure only the selected
  counters change. Repeat document scope, which includes inferred XP/raven and unknown-ID counters.
- [ ] Confirm ranges remain 0..each imported value, including fixed zero imports. Closing/reopening
  an edited save resets the import ceiling to that file's value. Do not treat this as a legal game cap.

## Further format and integrity captures

- [ ] Capture two no-action save/reload controls in the same location, with the UI unchanged.
  These isolate timestamp/counter/churn ranges from gameplay dependencies.
- [ ] Capture one existing Hacksilver stack before/after gaining or spending exactly one known
  amount. Repeat with Rawhide or another existing resource. Do not add/remove unknown records.
- [ ] Capture Kratos XP, then Atreus XP, then Freya/companion XP in separate pairs. Record every
  character's displayed XP and any simultaneous skill unlocks; never combine character changes.
  The imported PC table supplies four XP hashes (two Atreus contexts); verify each PS5 owner/context
  rather than treating its community label as console evidence.
- [ ] Capture one gear item before/after equipping it without upgrading, then a separate pair for
  upgrading that same item by one level. Record exact item/slot names, levels and visible stats.
  Supply a build-matched Ragnarök hash/name crosswalk or a user-supplied F2 pack if available.
  Community factual tables no longer need a licence unblock: the imported 2018 crosswalk has no
  gear/quest joins in the current PS5 nested arrays, so exact identity/ownership evidence is needed.
- [ ] Capture a quest immediately before/after one objective transition with no reward spending,
  recording exact quest/objective names and journal states. Repeat for quest completion. These
  must locate meanings within or outside the 132 raw grouped flag records; do not assume the
  groups are quests merely because their values resemble flags. Four groups now match PC skill-tree
  IDs (Axe/Blades/Spear/Atreus); individual skill hashes, saved flag bits and the fifth group remain
  Unknown. Capture one unlock at a time with exact skill name, weapon level, XP and pre/post state.
- [ ] Capture one Odin's Raven collection with journal count, unclaimed Raven Tree rewards and
  chest states recorded. Separately capture opening exactly one Raven Tree reward chest with
  no new raven collected. These test the two PC-derived raw counters and their relation to the
  PS5 journal; no completion formula is currently accepted.
- [ ] Capture one other collectible (artifact/lore) separately, with exact name/location and
  journal counts. Supply at least one late/Valhalla save with its own build/slot/UI evidence.
- [ ] Capture one userpreferences change alone, with setting name/value before and after.
- [ ] Supply at least two additional regional PS5 title IDs and, where legally available, paired
  PS4 and PC captures for the same gameplay state. Revision 0x00660404 is not accepted as PS5.
- [ ] For integrity research, retain every non-system save file in each pair and note the game's
  load/rejection result verbatim. If a serialization/integrity call trace is available, provide
  algorithm names, input ranges, field exclusions, seeds and key-derivation *structure* with
  secrets redacted. Never send account credentials or secret key values. Keyed MACs/signatures,
  custom seeds, truncated digests, zeroed/omitted embedded fields and compressed coverage remain open.
- [ ] Identify remaining derived byte/length/authentication dependencies from the captures. The
  enabled writer changes only bounded u32 words and preserves all other bytes; unknown dependencies
  are warned about, not silently repaired. An unsuccessful digest search is not acceptance evidence.
- [ ] Only the owner approves Stable after the independent load/effect/re-save/reload trials.
  Keep Experimental while authentication or console acceptance remains unresolved.
