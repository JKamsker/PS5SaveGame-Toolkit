# Six sampled factual identities — v1.0.0

Author: PS5 Save Game Toolkit contributors. Retrieved/assembled: **2026-10-28**.
Source/evidence record: <https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/14>;
the independently recorded observations in `docs/plans/EldenRing.md` and this plugin's
`Evidence/README.md` describe the byte checks. These six short ID/name associations are
an independently assembled factual crosswalk, not an imported third-party name database.

The original selection, normalization and evidence annotations are dedicated under
[CC0-1.0](https://creativecommons.org/publicdomain/zero/1.0/). This does not claim ownership
of game names or grant rights to game art, descriptions, FMG, Paramdex or upstream databases.
No prose, icons, game assets, bulk TGA-derived lists or quantity limits are included.
Record descriptions are original explanations of evidence limits.

Research sources (inspected as text, not executed):

- ClayAmore/ER-Save-Editor, revision `014107f0ca1cff867b5f7565cb79d6684f9b35df`:
  [identity rules](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/vm/inventory/mod.rs),
  [weapon cross-check](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/weapon_name.rs),
  [goods cross-check](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/item_name.rs).
  Code: [MIT](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/LICENSE-MIT).
  That code licence is **not** asserted to relicense game content or inherited TGA lists.

Changes: normalized six observed full, category-qualified IDs to `0xXXXXXXXX`;
added coverage/schema/evidence attributes. Weapon upgrades/affinities are **not** normalized
to an unsampled base weapon. Names remain Inferred until captured in game.

Coverage: JP PPSA03169, one v150/profile-v90 snapshot, six identities only; no DLC completeness.
Build-agnostic means **identity labels only**, never build-specific caps or compatibility.
Game/app/regulation versions are unknown. Wider redistribution of databases/art remains gated
by owner requirement ELDENRING-R21. Per-file SHA-256 is in `pack.json` (LF-normalized JSON).
