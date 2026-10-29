# Plugin status

Checked against local `main` on 2026-10-29. The table records `PluginInfo` registrations;
a registered title is not evidence of sample coverage for every build or region.
The [support table](../README.md#supported-games-and-formats) describes current read/write
capabilities. Each plugin README gives its schema gates, confidence, bounds and remaining gaps.

All 17 game/format plugins declare **Experimental** maturity. This does not promise that every
document is writable, every integrity mechanism is understood, or any edit has been accepted on
a console. The raw fallback is **ReadOnly**. Follow the [owner validation guide](OWNER_CHECKLIST.md)
before trusting an edit; only a human may promote validated support to Stable.

| Plugin | Registered titles / format | Maturity |
| --- | --- | --- |
| [ASTRO's PLAYROOM](../plugins/AstroPlayroom/README.md) `astro-playroom` | PPSA01325 | Experimental |
| [Atomic Heart](../plugins/AtomicHeart/README.md) `atomic-heart` | PPSA10695 | Experimental |
| [Baldur's Gate 3](../plugins/BaldursGate3/README.md) `bg3` | PPSA18463 | Experimental |
| [Cyberpunk 2077](../plugins/Cyberpunk2077/README.md) `cyberpunk2077` | PPSA04027 | Experimental |
| [Elden Ring](../plugins/EldenRing/README.md) `eldenring` | PPSA03169, PPSA04608, PPSA04609, PPSA04610 | Experimental |
| [Final Fantasy XVI](../plugins/FinalFantasy16/README.md) `ffxvi` | PPSA10665, PPSA13803 | Experimental |
| [FF VII Remake / Rebirth](../plugins/FinalFantasy7/README.md) `ff7r` | PPSA01911, PPSA08664, PPSA18936 | Experimental |
| [Gearbox](../plugins/Gearbox/README.md) `gearbox` | PPSA01463, PPSA01493 | Experimental |
| [Generic JSON](../plugins/GenericJson/README.md) `format.json` | Content-based JSON fallback; no registered title IDs | Experimental |
| [God of War Ragnarök](../plugins/GodOfWarRagnarok/README.md) `gow-ragnarok` | PPSA08329, PPSA08330, PPSA08331, PPSA08332, PPSA08333, PPSA08334, PPSA25043, PPSA25044 | Experimental |
| [Ratchet & Clank: Rift Apart](../plugins/Insomniac/README.md) `insomniac` | PPSA01474 | Experimental |
| [Pragmata](../plugins/Pragmata/README.md) `pragmata` | PPSA02530 | Experimental |
| [Ghost](../plugins/SuckerPunch/README.md) `suckerpunch` | PPSA02225, PPSA26344 | Experimental |
| [Tales of Arise](../plugins/TalesOfArise/README.md) `tales-of-arise` | PPSA02745 | Experimental |
| [Unreal GVAS](../plugins/UnrealGvas/README.md) `engine.unreal-gvas` | PPSA01288, PPSA01603, PPSA02199; other compatible GVAS by content | Experimental |
| [The Witcher 3](../plugins/Witcher3/README.md) `witcher3` | PPSA04018 | Experimental |
| [Obfuscated JSON / Dungeons II](../plugins/XorJson/README.md) `format.json-xor` | PPSA16064; supported transformed JSON by content | Experimental |
| Raw file inspector `builtin.raw` | Other files, including unsupported variants | ReadOnly |

Read-only gates remain within Experimental plugins: Yōtei has no gameplay writer; wrapped Hogwarts
envelopes and incomplete/invalid database-image pairs are read-only; other unsupported schemas and
file roles have their own gates. FF7 Rebirth demo gameplay has bounded writes; it is not globally
read-only. Unknown checksums, RSA coverage and gameplay dependencies remain explicitly warned.

No dedicated game plugin is registered for Borderlands 4, Star Ocean: The Divine Force,
Immortals Fenyx Rising, Far Cry 6, DOOM Eternal, Overwatch 2 or Kena: Bridge of Spirits.
A compatible generic format can still match a file; otherwise it opens in the raw inspector.
YouTube payloads were excluded from reconnaissance because they may contain account data.
See [historical format reconnaissance](formats/README.md) for the original measurements.
