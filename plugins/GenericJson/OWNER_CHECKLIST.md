# GenericJson owner evidence and validation

Write-enable round, 2026-10-28: the owner decision **“Enable writes. Keep warnings”** enables the
title adapters and existing-value presets described in README's **Enabled writes and their evidence**
table. Validation now lives in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31);
these unchecked items do not gate the enabled writes. Everything remains Experimental/Inferred or
Unknown. Byte-identical round trips prove preservation, not console acceptance. External integrity and
game dependencies are unknown, so the game may reject or overwrite edited saves.

Validate these enabled operations independently on spare saves:

- [ ] Cyberpunk scalar bool/number/text; paired choice + index; dynamic bounded raw text + explicit
      index; individual recorded default; selected/all recorded-default preset. Confirm both tokens,
      actual menu effect, native resets and restart/resave behavior. Float steps are advisory because
      the recorded float32 metadata does not divide its own defaults exactly; check actual granularity.
- [ ] It Takes Two individual stored flags (including field-specific casing), integer-string counters
      within the conservative `0..max(imported,67)` range, individual chapter/checkpoint strings and
      the explicit three-field tuple. Verify Continue, Chapter Select, replay and forward progression.
- [ ] It Takes Two present-flag preset (true and false), selected/all counter-zero preset; inspect
      skipped rows and missing minigames, story state, rewards/trophies and each Cody/May counter unit.
      No missing flags, chapter graph, story dependencies or progression state are created.
- [ ] Ridge Racer raw wrapper primitives within zero-to-imported numeric bounds; string booleans;
      selected/all restore-imported preset. Zero-valued unknown enums have only one permitted value;
      this is a conservative range, not a game limit or a named visual preset. Inspect DATA.BIN without
      editing it and compare every companion after restart/native resave.
- [ ] For each operation: keep original/full companion set privately; load, observe the precise effect,
      restart, resave, reload, re-export, run F1 against unchanged controls and restore the original.
      Report rejection, normalization/overwrite, unrelated changes and progression problems in #31.

For every capture pair:

- [ ] Record title ID/region, game build, wrapper/settings version, DLC, file roles and one exact action.
- [ ] Export immediately before and after the action; retain every companion, primary, backup twin and
      duplicate generation privately. Archive ordering is not chronology.
- [ ] Collect an unchanged load/save control pair to identify churn. Record displayed values and expected
      effects without account IDs, UIDs, PSN names, tokens, credentials or private save payloads.
- [ ] Run F1 on the exact pair and record only relevant gameplay/settings paths in a fixture manifest.
      Validate candidate mappings against a second independent pair where possible.

Cyberpunk 2077 (`PPSA04027`):

- [ ] Change one static enum; establish whether `value` and `index` must change together and which is
      authoritative after restart/resave. Include the unchanged control and all companions.
- [ ] Change one bounded scalar, then perform the menu's reset-to-default as a separate pair. Establish
      whether the recorded default/step/range applies on this console build and whether companions change.
- [ ] Change one dynamic language/HDR choice. Capture the actual choice list and its availability on PS5;
      a missing JSON `values` list must not become a guessed enum.
- [ ] Supply a second settings version and screenshots/exact menu labels mapping group + option name
      (names such as PadVibration/HudScaling occur in multiple groups).
- [ ] Identify whether any invariant couples `settings.json`, slot metadata and gameplay files. Retain
      all files from the same generation; do not assume that co-location establishes coupling.

It Takes Two (`PPSA02343`, EU):

- [ ] Capture a fresh profile and one minigame discovery, then playing that game, as separate pairs.
- [ ] Capture one Cody win/score and one May win/score separately. Map `Minigame.*`, `MinigameFlag.*`
      and `MinigameCounter.*` fields and casing per field, including counter units/reset behavior.
- [ ] Capture entry into one chapter and crossing one checkpoint separately. Record Continue, Chapter
      Select and replay outcomes; retain complete chapter/checkpoint strings privately.
- [ ] Supply same-version incomplete/complete profiles, preferably from two owners. Verify each absent
      key's behavior before proposing insertion; absence never automatically means false or zero.
- [ ] Verify display aliases for every observed minigame key and supply the remaining catalogue with
      exact raw-key mappings and permitted provenance. Word splitting is not verified localization.
- [ ] Identify any related file outside the existing Profile/CloudSettings/LocalSettings sections and
      demonstrate a required cross-file invariant before proposing an F7 document group.

Ridge Racer 2 PSP wrapper (`PPSA07696`):

- [ ] Capture one filter/upscale/AA choice per pair, with the exact PSP wrapper build and menu value.
- [ ] Capture one controller option and one rewind option separately. Restore/restart/resave/re-export
      each to establish numeric domains and dependencies; PS1 presets do not establish PSP enum values.
- [ ] Retain `imgui_emu_option.json` and `DATA.BIN` together and determine whether an actual coordinated
      write is required. The binary gameplay file alone is not a GenericJson member.

For future built-in title insertion schemas, complete presets or F7 groups (not implemented here):

- [ ] Derive each required/optional member, legal default and item-count limit from controlled pairs;
      validate any proposed insertion on a fresh/incomplete spare profile, not just a completed save.
- [ ] For F7, identify the smallest proven file group and invariant. Compare separate vs coordinated
      changes on spare saves; retain role/path, generation and fingerprint evidence. Cover missing,
      ambiguous and divergent backup partners explicitly. Coordinate any binary role with its plugin.
- [ ] Load a spare edited PS5 save; verify the exact menu/game effect, Continue/Chapter Select/replay as
      applicable, restart, resave, re-export and compare untouched state plus every companion.
- [ ] Restore the original and verify restoration; document any game normalization/overwrite/rejection.
      Only the owner may promote maturity to Stable. Do not claim trophy guarantees or cross-platform
      conversion from syntax validity or PC editor behavior.

Current framework status: F10/F5/F6/F8 are used; tagged review formatting is fixed in `03b66b7`.
F7 is available (`7ff21ad`), but no clearly coupled companion invariant is identified for the enabled
GenericJson writes. Co-location and identical backup twins do not create such an invariant.
There is no new blocking SDK request.
