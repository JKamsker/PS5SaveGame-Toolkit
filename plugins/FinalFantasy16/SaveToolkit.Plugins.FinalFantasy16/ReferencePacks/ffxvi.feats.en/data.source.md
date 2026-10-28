# Vanilla Eikonic feat research — MIT

Author: mThund3R, Logos-Unleashed. Copyright (c) 2025 mThund3R.
Retrieved 2026-10-28. Licence: [MIT](https://github.com/mThund3R/Logos-Unleashed/blob/6424cb4e60bd31005ef7f0cfb42d4e5360660fc6/LICENSE).
Full original notice is retained in [THIRD_PARTY_NOTICES.md](../../../THIRD_PARTY_NOTICES.md).

Source: [Mod.cs, revision 6424cb4e60bd31005ef7f0cfb42d4e5360660fc6](https://github.com/mThund3R/Logos-Unleashed/blob/6424cb4e60bd31005ef7f0cfb42d4e5360660fc6/LogosUnleashed/Mod.cs),
`UndoSpecialAttack` (lines 1208–1271), plus `odinFeatId = 281` and
`SwitchOdin` restoring that vanilla ID (lines 1653–1671).
This code was read as text in scratch and never executed or imported.

Modifications: selected seven vanilla command/name pairs and eight summon-mode
associations into a reviewed TSV and two F2 datasets. Phoenix modes 1 and 6 share
command 201. Omitted all custom/replacement IDs, the dynamic alternate Odin value
3157, the commented-out Bahamut/Wings of Light restoration, and Ultima's ambiguous
restoration path. No icons, descriptions, gameplay effects or mod code are embedded.

The restore assignments support **Inferred reference relationships**, not proven
PS5 save semantics. No learned rank, unlock bit, AP cost, active bank, equipped
command or save-rank index is derived from this crosswalk. Unknowns remain raw.
`ffxvi_commands` is keyed by command ID; `ffxvi_feats` is keyed by summon-mode ID,
with the command ID kept as a separate attribute. They must never be interchanged.

Offline regeneration: `python -I plugins/FinalFantasy16/research/build_feat_catalog.py`.
The reviewed TSV is the input; no network, game dump or upstream executable is needed.
