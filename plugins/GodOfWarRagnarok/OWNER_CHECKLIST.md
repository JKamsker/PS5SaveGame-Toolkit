# Owner evidence checklist — round 2

All writes remain disabled. Byte-identical no-op serialize/reopen is proven on all supplied gameplay
records, but authentication, gameplay semantics and an edited-file round trip are not proven.

For every pair below, retain the original archive and an untouched backup. Use one fixed PS5 title,
build, slot and DLC configuration; record title ID, build, slot, filenames, archive generation,
event and exact UI before/after values. Do not use archive order as a chronology assumption.

- [ ] Capture two no-action save/reload controls in the same location, with the UI unchanged.
  These isolate timestamp/counter/churn ranges from gameplay dependencies.
- [ ] Capture one existing Hacksilver stack before/after gaining or spending exactly one known
  amount. Repeat with Rawhide or another existing resource. Do not add/remove unknown records.
- [ ] Capture Kratos XP, then Atreus XP, then Freya/companion XP in separate pairs. Record every
  character's displayed XP and any simultaneous skill unlocks; never combine character changes.
- [ ] Capture one gear item before/after equipping it without upgrading, then a separate pair for
  upgrading that same item by one level. Record exact item/slot names, levels and visible stats.
  Supply a legally redistributable hash/name crosswalk or a user-supplied F2 pack if available;
  raw nested IDs cannot establish gear names or ownership by themselves.
- [ ] Capture a quest immediately before/after one objective transition with no reward spending,
  recording exact quest/objective names and journal states. Repeat for quest completion. These
  must locate meanings within or outside the 132 raw grouped flag records; do not assume the
  groups are quests merely because their values resemble flags.
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
- [ ] Before proposing any write, identify every derived byte/length/authentication dependency;
  require an edited-file serialize/reopen test that preserves all unrelated bytes. A successful
  no-op test or the absence of candidate hash matches does not satisfy this gate.
- [ ] Only after those gates pass, trial one resulting edit on a restored spare slot: load,
  verify the exact UI effect, re-save, reload, verify persistence, and record rejection text if
  any. The owner alone approves a maturity change; retain ReadOnly until then.
