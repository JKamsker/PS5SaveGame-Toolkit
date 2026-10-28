# Remaining-title format reconnaissance

Research date: **2026-10-28**. All 23 assigned titles are identified below. Pragmata, BG3, Ghost titles and the six assigned `ue4savegame.*.sav` titles were not inspected. Shared `docs/SAMPLES.md` was left unchanged; corrected identities are recorded here.

## Ranked implementation opportunities

This ranking is engineering judgement, not measured popularity. Value 0–5 estimates usefulness to players; feasibility weights are easy=5, medium=3, hard=1, blocked/excluded=0. Product determines rank; ties prefer value then title ID. Easy ratings for Cyberpunk and Ridge Racer apply to their JSON companions; their binary gameplay difficulty is documented separately.

| Rank | Title | Identified game | Shared family | Value | Feasibility | Product |
| ---: | --- | --- | --- | ---: | --- | ---: |
| 1 | [PPSA04027](PPSA04027.md) | Cyberpunk 2077 | JSON; REDengine CSave | 5 | easy JSON | 25 |
| 2 | [PPSA01463](PPSA01463.md) | Borderlands 3 | Gearbox GVAS/protobuf | 5 | medium | 15 |
| 3 | [PPSA01911](PPSA01911.md) | FFVII Remake Intergrade | FF7 linked chunks | 5 | medium | 15 |
| 4 | [PPSA08664](PPSA08664.md) | FFVII Rebirth | FF7 linked chunks | 5 | medium | 15 |
| 5 | [PPSA10665](PPSA10665.md) | Final Fantasy XVI | Faith | 5 | medium | 15 |
| 6 | [PPSA01288](PPSA01288.md) | Sackboy: A Big Adventure | Unreal GVAS | 3 | easy | 15 |
| 7 | [PPSA01325](PPSA01325.md) | ASTRO’s PLAYROOM | Custom XML-like memory | 3 | easy | 15 |
| 8 | [PPSA01474](PPSA01474.md) | Ratchet & Clank: Rift Apart | Insomniac i29/DAT1 | 4 | medium | 12 |
| 9 | [PPSA01493](PPSA01493.md) | Tiny Tina’s Wonderlands | Gearbox GVAS/protobuf | 4 | medium | 12 |
| 10 | [PPSA02745](PPSA02745.md) | Tales of Arise | Tales AES/XOR | 4 | medium | 12 |
| 11 | [PPSA04018](PPSA04018.md) | Witcher 3 Complete Edition | REDengine SNFH/LZ4 | 4 | medium | 12 |
| 12 | [PPSA07696](PPSA07696.md) | Ridge Racer 2 (PSP classic) | Emulator JSON + PSP save | 2 | easy JSON | 10 |
| 13 | [PPSA13803](PPSA13803.md) | Final Fantasy XVI Demo | Faith | 2 | medium | 6 |
| 14 | [PPSA18936](PPSA18936.md) | FFVII Rebirth Demo | FF7 linked chunks | 2 | medium | 6 |
| 15 | [PPSA03169](PPSA03169.md) | Elden Ring (JP) | FromSoftware memory | 5 | hard | 5 |
| 16 | [PPSA08330](PPSA08330.md) | God of War Ragnarök | Santa Monica custom | 5 | hard | 5 |
| 17 | [PPSA01506](PPSA01506.md) | Immortals Fenyx Rising | Ubisoft/Anvil custom | 3 | hard | 3 |
| 18 | [PPSA01876](PPSA01876.md) | Far Cry 6 | Ubisoft/Dunia profile | 3 | hard | 3 |
| 19 | [PPSA01982](PPSA01982.md) | DOOM Eternal | idTech profile | 3 | hard | 3 |
| 20 | [PPSA07821](PPSA07821.md) | Overwatch 2 (settings) | Blizzard SAVE/GFX7 | 1 | medium | 3 |
| 21 | [PPSA01494](PPSA01494.md) | **Borderlands 4** | Separate BL4 format | 5 | blocked | 0 |
| 22 | [PPSA06251](PPSA06251.md) | Star Ocean: The Divine Force | tri-Ace opaque binary | 3 | blocked | 0 |
| 23 | [PPSA01651](PPSA01651.md) | YouTube application | Cobalt/Starboard | 0 | excluded | 0 |

## Shared work packages

- **Immediate JSON:** [GenericJson](../../plugins/GenericJson/README.md) covers Cyberpunk settings/metadata and Ridge Racer emulator options by content. Their binary gameplay saves remain separate work.
- **Faith, PPSA10665 + PPSA13803:** CRC32 at `0x04` over `0x10..EOF` matches **72/72 records**. Supplied inner files are plaintext; PC PNG/XOR/dictionary-zlib wrappers must not be added to PS5 output. Retail/demo need explicit schema variants.
- **FF7, PPSA01911 + PPSA08664 + PPSA18936:** one bounded linked-chunk walker; separate Remake/Rebirth/demo schemas. Representative gameplay chains have 24 versus 275 records. Rebirth/demo agree on dimensions and header fields, not proven gameplay offsets.
- **Gearbox, PPSA01463 + PPSA01493:** share GVAS framing, with distinct game/profile masks and protobuf schemas. **PPSA01494 is BL4**, not BL3; PC BL4 AES/YAML research does not yet validate its PS5 blob.
- **Unreal GVAS, PPSA01288:** Sackboy General has GVAS at zero. Coordinate shared-reader reuse with the Unreal worker; no excluded Unreal title was inspected here.
- **Insomniac i29, PPSA01474:** verified DAT1 framing is reusable. Only Rift Apart is present in this assigned corpus; support for other Insomniac titles needs their own samples.
- **REDengine:** Witcher and Cyberpunk share an LZ4 codec, not a schema or identical container. All supplied Witcher gameplay blocks and all 22 Cyberpunk chunks decode with bounds/output-length checks.
- **ASTRO:** readable XML-like memory is promising but numeric tag names violate XML syntax. Token-preserving editing is safer than a generic XML serializer.
- **Separate follow-ups:** Tales has explicit public PS5 conversion research, including an offset discrepancy found here. Elden Ring, God of War, DOOM and Ubisoft need individual framing/integrity work. Star Ocean is blocked on save-specific decoding. YouTube offers no gameplay editing value.

## Method and limits

The scripts read archives without extraction, retain every duplicate entry, hash/compare primary/backup records, calculate entropy/zero fraction, inspect selected headers and validate known containers. Raw measurements stay in scratch storage; no sample fixtures are committed. Small magic-byte summaries and aggregate measurements in the reports are the requested research evidence.

```powershell
python docs/formats/inspect_samples.py C:/path/to/main/artifacts "$env:TEMP/savetoolkit-recon-measurements.json"
```

`inspect_containers.py` contains independently written read-only LZ4/section/link checks. The [primary LZ4 block grammar](https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md) and the title-specific container sources are cited in the corresponding reports. Each decoded block must match its declared output length; total output is capped at 64 MiB. Downloaded implementations were read as untrusted text and never executed. No third-party implementation was copied into the plugin.

Signature searches cover GVAS/DSSS, LZ4 frame/legacy, zstd, gzip, XZ and SQLite throughout each payload. Zlib candidates starting within the first 4096 bytes are actually decoded and must finish successfully. **An absent signature does not exclude raw LZ4/Deflate, dictionary streams, LZMA, Oodle, custom compression or encryption.** Oodle and raw blocks have no reliable universal magic. An entropy score alone never proves a cipher. This is reconnaissance, not exhaustive cryptanalysis.

H uses the first min(length, 1 MiB), not the whole file. Tables combine related basenames across slots/directories; representatives are the first primary ZIP occurrences. Duplicate-path occurrence order is not necessarily chronological. Backup equality uses whole-file SHA256; unequal-byte counts cover only the common-length region, with separate length deltas. One CRC32 candidate (word at 4, coverage from 16) was tested throughout; failures elsewhere do not establish absence of checksums.

JSON measurements report only root type/count. **YouTube payloads were excluded** because account/session secrets may be present; only ZIP metadata and SFO title metadata were inspected. That exception leaves its byte format intentionally uncharacterised, consistent with the workspace confidentiality rule.

SFO MAINTITLE is often a role, translated quest name or generated placeholder. Some PPSA10665 SFOs even carry unrelated TITLE_ID values; outer paths, independent listings and game-specific structure are stronger evidence there. Every title report links identity evidence and distinguishes measured bytes, upstream claims and unknown semantics.

## Delivery boundary

Only GenericJson is implemented here. It is Experimental after sample edit/reopen conformance; no console edit was tested. Other reports are implementation leads, with licence/platform scope and unknown integrity stated explicitly. PC/PS4 tool support never certifies PS5 compatibility. Common open work is proving game meanings/ranges, all integrity dependencies and successful in-game loading before any Stable designation.
