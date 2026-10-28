# Skill reference dataset attribution

Dataset: `gow-ragnarok-skills`, pack `gow-ragnarok.skills.en` v1.0.0.

God of War Wiki contributors, CC BY-SA 3.0:
- https://godofwar.fandom.com/wiki/Leviathan_Axe — **Move List (Ragnarok)** only (28 entries).
- https://godofwar.fandom.com/wiki/Blades_of_Chaos — **God of War Ragnarok / Skill Trees** only (26 entries).
- https://godofwar.fandom.com/wiki/Draupnir_Spear — **Ability Trees** only (27 entries).
- https://godofwar.fandom.com/wiki/Skill_Labors_%28Ragnar%C3%B6k%29 — additional explicitly listed costs/weapon levels and Chaotic Rampage prerequisites.
- Licence: https://creativecommons.org/licenses/by-sa/3.0/ ; wiki licensing: https://www.fandom.com/licensing .

Retrieved 2026-10-28 from the publicly indexed wiki text (index marked approximately four months old).
Direct HTML/API requests were restricted; **no historical revision ID or current-game-build verification
is claimed**. The pages' own community-content notices state CC BY-SA. This derivative dataset is
available under CC BY-SA 3.0 with attribution/share-alike; no game artwork or copied gameplay prose.
See `pack.json` and the README Sources and attribution table for the same provenance.

Changes: transcribed factual names, three branches, explicitly listed XP/weapon-level requirements
and prerequisite relationships; normalized numbered suffixes to Roman numerals and corrected
Maelstorm to Maelstrom. No 2018 move list was imported. Missing costs/levels are `Unknown`.
Zero XP means the source lists a starting move, **not** that the imported save has unlocked it.
No missing prerequisite edge implies that no prerequisite exists. Blades' numbered upgrades
without explicit prerequisite requirements are deliberately disconnected; the wiki describes
upgrades but does not prove their purchase prerequisites. Sources can differ between patches.

**These are presentation keys, not save IDs.** No PS5 saved skill hash/name crosswalk is proven.
The graph always uses `Unknown` state; no wallet/flag/XP write or unlock action is connected.
No legal resource limits, mod-token rules, Valhalla/NG+ rules or asset rights are inferred.
Catalogue-backed details are read-only. Sources appear in the F2 panel, graph descriptions,
reference fields and detail table. Direct callers load the same embedded attributed records.

The realm map uses original factual realm labels and external guide links only. MapGenie and
wiki maps/art/coordinates are not part of this dataset and are not embedded or fetched.
