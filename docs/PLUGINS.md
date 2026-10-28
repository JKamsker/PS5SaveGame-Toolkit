# Plugin status

State on 2026-10-28. **Experimental** = edits pass the conformance suite (byte-identical no-op,
edit → serialise → re-open, integrity recomputed) but nobody has loaded an edited save on a console yet.
**ReadOnly** = the data is shown, writing is disabled until integrity is understood. Each plugin's
README documents formats, evidence, confidence levels and sources.

| Plugin | Titles | Maturity | What you can edit | Main open questions |
| --- | --- | --- | --- | --- |
| [Pragmata](../plugins/Pragmata/README.md) `pragmata` | PPSA02530 | Experimental | Header fields, inventory quantities (incl. bulk action), game clock, any fixed-size primitive in the raw tree | Item names, legal limits, difficulty ids |
| [Unreal GVAS](../plugins/UnrealGvas/README.md) `engine.unreal-gvas` | Hogwarts Legacy, Jedi: Fallen Order, Sackboy (any plain GVAS) | Experimental | Primitive properties incl. strings with size fix-ups | Hogwarts SQLite (Oodle), Jedi gameplay CRC, Sackboy checksum |
| [Gearbox](../plugins/Gearbox/README.md) `gearbox` | Borderlands 3, Tiny Tina's Wonderlands | Experimental | Name, level/XP, currencies, keys, ammo, SDUs | Item serials, missions |
| [Baldur's Gate 3](../plugins/BaldursGate3/README.md) `bg3` | PPSA18463 | Experimental | LSF primitive attributes in the save package (digest recomputed) | Gold/stats semantics, LSMF |
| [Cyberpunk 2077](../plugins/Cyberpunk2077/README.md) `cyberpunk2077` | PPSA04027 | Experimental | Money, attributes, unspent attribute/perk/relic points, item stacks | Level/XP thresholds, facts |
| [The Witcher 3](../plugins/Witcher3/README.md) `witcher3` | PPSA04018 | Experimental | Level, XP, ability points, Crowns, inventory quantities | Level-up side effects |
| [Elden Ring](../plugins/EldenRing/README.md) `eldenring` | PPSA03169 (+ other regions) | Experimental | Held runes (bounded) | Attributes/level dependencies, inventory |
| [FF VII Remake / Rebirth](../plugins/FinalFantasy7/README.md) `ff7r` | PPSA01911, PPSA08664, PPSA18936 | Experimental (demo read-only) | Gil, item quantities, materia AP | Deeper record semantics |
| [Final Fantasy XVI](../plugins/FinalFantasy16/README.md) `ffxvi` | PPSA10665, PPSA13803 | Experimental | Existing inventory stacks (CRC32 repaired) | Stack caps, level/XP |
| [Ghost](../plugins/SuckerPunch/README.md) `suckerpunch` | Tsushima PPSA02225, Yōtei PPSA26344 | Experimental (Yōtei read-only) | Tsushima resources and ammo (anchored, checksum repaired) | Yōtei header/checksum |
| [Tales of Arise](../plugins/TalesOfArise/README.md) `tales-of-arise` | PPSA02745 | Experimental | Gald, level, HP, SP, consumables (AES/XOR + SHA1 redone) | CP, grade, equipment |
| [ASTRO's PLAYROOM](../plugins/AstroPlayroom/README.md) `astro-playroom` | PPSA01325 | Experimental | Coins, play count, collectibles, progress values | Bot flags (newer versions) |
| [Generic JSON](../plugins/GenericJson/README.md) `format.json` | any JSON (Cyberpunk settings, emulator options, PPSA02343) | Experimental | Any primitive, formatting preserved | — |
| [Obfuscated JSON](../plugins/XorJson/README.md) `format.json-xor` | Minecraft Dungeons II settings (PPSA16064) | Experimental | Any primitive, re-encoded | — |
| [God of War Ragnarök](../plugins/GodOfWarRagnarok/README.md) `gow-ragnarok` | PPSA08330 (+ regions) | ReadOnly | — | Nested grammar, checksums |
| [Ratchet & Clank: Rift Apart](../plugins/Insomniac/README.md) `insomniac` | PPSA01474 | ReadOnly | — (bolts, XP, raritanium shown) | Integrity coverage |
| [Atomic Heart](../plugins/AtomicHeart/README.md) `atomic-heart` | PPSA10695 | ReadOnly | — | Oodle-compressed records |
| Raw file inspector `builtin.raw` | everything else | ReadOnly | — | — |

Not covered yet (raw inspector only): Borderlands 4 (blocked on crypto), Star Ocean: The Divine Force
(blocked), Immortals Fenyx Rising, Far Cry 6, DOOM Eternal, Overwatch 2 settings, PPSA01802 (unidentified),
YouTube app (excluded for privacy). See [docs/formats/](formats/README.md) for what is known about each.

Before trusting an Experimental plugin: back up the original dump, edit a spare slot, keep values within
plausible game ranges, restore with your save manager and load it on the console.
