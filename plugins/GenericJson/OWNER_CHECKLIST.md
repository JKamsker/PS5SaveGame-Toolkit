# GenericJson owner evidence and validation

Round 2 keeps title overlays read-only and all title-semantic adapters/presets disabled. Generic syntax
editing is Experimental. Byte-identical round trips prove preservation, not console acceptance.

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

Before built-in title templates, semantic writes, presets or F7 adoption:

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
F7 is available (`7ff21ad`) but no production group is justified by the present GenericJson evidence.
There is no new blocking SDK request.
